using System.Data;
using System.Text.Encodings.Web;
using System.Text.Json;
using HT_Secim_API.Model;
using Microsoft.Data.SqlClient;

namespace HT_Secim_API.Veri;

/// <summary>
/// Veritabanini okur, turetilmis degerleri hesaplatir ve sonucu JSON olarak
/// onbellekte tutar.
///
/// Onbellek SURUM numarasina bagli: veri degismedigi surece veritabanina hic
/// gidilmiyor, hazir JSON metni donuyor. Bir oy degistiginde trigger surumu
/// artiriyor, bir sonraki istekte her sey bastan uretiliyor.
///
/// Secim gecesi saniyede birkac istek gelse bile veritabani yalnizca veri
/// gercekten degistiginde okunuyor.
/// </summary>
public sealed class VeriDeposu
{
    private readonly string baglanti;
    private readonly ILogger<VeriDeposu> log;

    private readonly SemaphoreSlim kapi = new(1, 1);

    private int onbellekSurum = -1;
    private string onbellekJson = "";
    private SecimVerisi? onbellekVeri;

    private DateTime sonBasariliOkuma = DateTime.MinValue;
    private DateTime veriZamani = DateTime.MinValue;
    private string? sonHata;

    /// <summary>
    /// Veritabanina en son ne zaman basariyla ulasildi. Elimizdeki veri bundan
    /// eski olamaz; reji uygulamasi ve saglik ucu bunu gosteriyor.
    /// </summary>
    public DateTime SonBasariliOkuma => sonBasariliOkuma;

    /// <summary> Verinin kendisinin en son ne zaman degistigi (VeriSurum.guncelleme). </summary>
    public DateTime VeriZamani => veriZamani;

    /// <summary> Veritabanina ulasilamiyorsa son hata; ulasiliyorsa null. </summary>
    public string? SonHata => sonHata;

    /// <summary> Elimizde servis edilebilir bir veri var mi. </summary>
    public bool VeriVar => onbellekVeri is not null;

    /// <summary>
    /// Turkce karakterler Ü gibi kacislarla degil dogrudan yazilsin.
    /// Varsayilan kodlayici kullanilirsa dosya neredeyse iki katina cikiyor
    /// ve loglarda okunmaz hale geliyor.
    /// </summary>
    private static readonly JsonSerializerOptions JsonAyari = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public VeriDeposu(IConfiguration yapilandirma, ILogger<VeriDeposu> log)
    {
        this.log = log;

        baglanti = yapilandirma.GetConnectionString("HtSecim")
                   ?? throw new InvalidOperationException(
                       "appsettings.json icinde ConnectionStrings:HtSecim tanimli degil.");
    }

    /// <summary> Yalnizca surum satiri. Reji uygulamasi 25 saniyede bir bunu soruyor. </summary>
    public async Task<SurumBilgisi> SurumAsync(CancellationToken iptal = default)
    {
        await using var cn = new SqlConnection(baglanti);
        await cn.OpenAsync(iptal);

        await using var cmd = new SqlCommand(
            "SELECT surum, guncelleme, aciklama FROM dbo.VeriSurum WHERE id = 1", cn);

        await using SqlDataReader r = await cmd.ExecuteReaderAsync(iptal);

        if (!await r.ReadAsync(iptal))
            throw new InvalidOperationException("VeriSurum tablosu bos.");

        return new SurumBilgisi
        {
            Surum      = r.GetInt32(0),
            Guncelleme = r.GetDateTime(1),
            Aciklama   = r.IsDBNull(2) ? null : r.GetString(2)
        };
    }

    /// <summary>
    /// Butun verinin JSON metni. Surum degismediyse onbellekten doner.
    /// </summary>
    public async Task<(int surum, string json)> VeriJsonAsync(CancellationToken iptal = default)
    {
        await TazeleAsync(iptal);
        return (onbellekSurum, onbellekJson);
    }

    /// <summary>
    /// Butun veri, nesne olarak. Ayrintili uclar (bir il, bir parti, siralama...)
    /// bunun uzerinden calisiyor; her istekte veritabanina gidilmiyor.
    ///
    /// DONEN NESNE PAYLASIMLI - yalnizca okunur, degistirilmemeli.
    /// </summary>
    public async Task<SecimVerisi> VeriAsync(CancellationToken iptal = default)
    {
        await TazeleAsync(iptal);
        return onbellekVeri!;
    }

    /// <summary>
    /// Onbellegi surumle karsilastirir, gerekiyorsa yeniden uretir.
    ///
    /// YAYIN KURALI: veritabanina ulasilamazsa hata donmez. Elimizdeki son
    /// saglam veri servis edilmeye devam eder, yalnizca "bayat" isaretlenir.
    /// Canli yayinda SQL'in bir anlik takilmasi ekrani bosaltmamali; reji
    /// verinin eskidigini gostergeden gorur ve kararini kendisi verir.
    ///
    /// Hic veri yoksa (API daha ilk okumasini yapamamissa) hata firlatilir -
    /// gosterecek bir sey olmadigi icin gizlemenin anlami yok.
    /// </summary>
    private async Task TazeleAsync(CancellationToken iptal)
    {
        SurumBilgisi surum;

        try
        {
            surum = await SurumAsync(iptal);
        }
        catch (Exception ex) when (onbellekVeri is not null)
        {
            Bayatladi(ex, "surum okunamadi");
            return;
        }

        if (surum.Surum == onbellekSurum && onbellekVeri is not null)
        {
            Tazelendi(surum);
            return;
        }

        await kapi.WaitAsync(iptal);
        try
        {
            // Kapida beklerken baskasi uretmis olabilir.
            if (surum.Surum == onbellekSurum && onbellekVeri is not null) return;

            SecimVerisi veri = await OkuAsync(iptal);
            veri.Guncelleme = surum.Guncelleme;

            string json = JsonSerializer.Serialize(veri, JsonAyari);

            onbellekVeri  = veri;
            onbellekJson  = json;
            onbellekSurum = surum.Surum;

            Tazelendi(surum);

            log.LogInformation("Veri yeniden uretildi. Surum {Surum}, {Boyut} bayt, {Secim} secim.",
                surum.Surum, json.Length, veri.Secimler.Count);
        }
        catch (Exception ex) when (onbellekVeri is not null)
        {
            Bayatladi(ex, "veri okunamadi");
        }
        finally
        {
            kapi.Release();
        }
    }

    private void Tazelendi(SurumBilgisi surum)
    {
        sonBasariliOkuma = DateTime.Now;
        veriZamani = surum.Guncelleme;
        sonHata = null;
    }

    private void Bayatladi(Exception ex, string nerede)
    {
        sonHata = nerede + ": " + ex.Message;

        log.LogError(ex, "Veritabanina ulasilamadi ({Nerede}). Surum {Surum} onbellekten servis ediliyor.",
            nerede, onbellekSurum);
    }

    /// <summary>
    /// Butun tablolari TEK sorguda, TEK anlik goruntude okur.
    ///
    /// On tablo arka arkaya ayri ayri okunsaydi, aralarinda biri veriyi
    /// degistirdiginde yarisi eski bir tablo elde edilebilirdi. SNAPSHOT
    /// izolasyonu hepsinin ayni ana ait olmasini garanti ediyor.
    /// </summary>
    private async Task<SecimVerisi> OkuAsync(CancellationToken iptal)
    {
        var veri = new SecimVerisi();

        // secimKod -> plaka -> satir  (Oy kayitlarini yerlestirirken lazim)
        var satirlar = new Dictionary<string, Dictionary<int, IlSonucu>>(StringComparer.Ordinal);
        var secimler = new Dictionary<string, Model.Secim>(StringComparer.Ordinal);

        await using var cn = new SqlConnection(baglanti);
        await cn.OpenAsync(iptal);

        await using SqlTransaction tx = (SqlTransaction)await cn.BeginTransactionAsync(
            IsolationLevel.Snapshot, iptal);

        await using var cmd = new SqlCommand(SORGU, cn, tx) { CommandTimeout = 120 };
        await using SqlDataReader r = await cmd.ExecuteReaderAsync(iptal);

        // 1) Grup
        var gruplar = new Dictionary<string, Grup>(StringComparer.Ordinal);
        while (await r.ReadAsync(iptal))
        {
            var g = new Grup { Kod = r.GetString(0), Ad = r.GetString(1) };
            gruplar[g.Kod!] = g;
            veri.Gruplar.Add(g);
        }

        // 2) GrupIl
        await r.NextResultAsync(iptal);
        while (await r.ReadAsync(iptal))
        {
            if (gruplar.TryGetValue(r.GetString(0), out Grup? g)) g.Plakalar.Add(r.GetInt32(1));
        }

        // 3) Il
        await r.NextResultAsync(iptal);
        var kotalar = new Dictionary<int, int>();
        while (await r.ReadAsync(iptal))
        {
            var il = new Il
            {
                Plaka       = r.GetInt32(0),
                Ad          = r.GetString(1),
                Bolge       = r.IsDBNull(2) ? null : r.GetString(2),
                VekilKotasi = r.GetInt32(3)
            };

            veri.Iller.Add(il);
            kotalar[il.Plaka] = il.VekilKotasi;
        }

        // 4) Ittifak
        await r.NextResultAsync(iptal);
        while (await r.ReadAsync(iptal))
        {
            veri.Ittifaklar.Add(new Ittifak
            {
                Kod          = r.GetString(0),
                Ad           = r.GetString(1),
                Renk         = Metin(r, 2),
                VizLogoImage = Metin(r, 3),
                VizBarImage  = Metin(r, 4)
            });
        }

        // 5) Secenek
        await r.NextResultAsync(iptal);
        while (await r.ReadAsync(iptal))
        {
            veri.Secenekler.Add(new Secenek
            {
                Kod         = r.GetString(0),
                Ad          = r.GetString(1),
                Renk        = Metin(r, 2),
                VizBarImage = Metin(r, 3)
            });
        }

        // 6) Parti
        await r.NextResultAsync(iptal);
        var ittifakHaritasi = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await r.ReadAsync(iptal))
        {
            var p = new Parti
            {
                Kod                 = r.GetString(0),
                Ad                  = r.GetString(1),
                Renk                = Metin(r, 2),
                Ittifak             = Metin(r, 3),
                VizImage            = Metin(r, 4),
                VizBarImage         = Metin(r, 5),
                VizSatirImage       = Metin(r, 6),
                VizLogoImage        = Metin(r, 7),
                VizKiyasLogoImage   = Metin(r, 8),
                VizHaritaSeritImage = Metin(r, 9),
                VizMvRozetImage     = Metin(r, 10),
                HaritaRenk          = Metin(r, 11),
                HaritaRenkGenel     = Metin(r, 12),
                MeclisRenk          = Metin(r, 13),
                Toplu               = r.GetBoolean(14)
            };

            veri.Partiler.Add(p);

            if (!string.IsNullOrEmpty(p.Ittifak)) ittifakHaritasi[p.Kod!] = p.Ittifak!;
        }

        // 7) Aday
        await r.NextResultAsync(iptal);
        while (await r.ReadAsync(iptal))
        {
            veri.Adaylar.Add(new Aday
            {
                Kod                   = r.GetString(0),
                Ad                    = r.GetString(1),
                TamAd                 = Metin(r, 2),
                Parti                 = Metin(r, 3),
                Ittifak               = Metin(r, 4),
                VizImage              = Metin(r, 5),
                VizBarImage           = Metin(r, 6),
                VizHaritaImage        = Metin(r, 7),
                VizSehirImage         = Metin(r, 8),
                VizTurImage           = Metin(r, 9),
                VizKarsilastirmaImage = Metin(r, 10),
                HaritaRenk            = Metin(r, 11),
                HaritaRenkGenel       = Metin(r, 12)
            });
        }

        // 8) Secim
        await r.NextResultAsync(iptal);
        while (await r.ReadAsync(iptal))
        {
            var s = new Model.Secim
            {
                Kod       = r.GetString(0),
                Ad        = r.GetString(1),
                Tip       = r.GetString(2),
                Yil       = r.GetInt32(3),
                BarajTipi = Metin(r, 4),
                BarajOran = r.IsDBNull(5) ? 0 : r.GetInt32(5)
            };

            veri.Secimler.Add(s);
            secimler[s.Kod!] = s;
            satirlar[s.Kod!] = new Dictionary<int, IlSonucu>();
        }

        // 9) IlSonucu
        await r.NextResultAsync(iptal);
        while (await r.ReadAsync(iptal))
        {
            string secimKod = r.GetString(0);
            if (!secimler.TryGetValue(secimKod, out Model.Secim? s)) continue;

            var satir = new IlSonucu
            {
                Plaka            = r.GetInt32(1),
                ToplamSandik     = r.GetInt32(2),
                AcilanSandikAdet = r.GetInt32(3),
                SecmenSayisi     = r.GetInt64(4),
                GecersizOy       = r.GetInt64(5),
                Guncelleme       = r.GetDateTime(6)
            };

            s.Sonuclar.Add(satir);
            satirlar[secimKod][satir.Plaka] = satir;
        }

        // 10) Oy
        await r.NextResultAsync(iptal);
        while (await r.ReadAsync(iptal))
        {
            string secimKod = r.GetString(0);
            int plaka = r.GetInt32(1);

            if (!satirlar.TryGetValue(secimKod, out Dictionary<int, IlSonucu>? iller)) continue;
            if (!iller.TryGetValue(plaka, out IlSonucu? satir)) continue;

            satir.Oylar.Add(new Oy { Kod = r.GetString(2), OySayisi = r.GetInt64(3) });
        }

        await r.CloseAsync();
        await tx.CommitAsync(iptal);

        // Turetilmis degerler: oran, gecerli oy, TURKIYE satiri, vekil.
        foreach (Model.Secim s in veri.Secimler) Hesap.Tamamla(s, kotalar, ittifakHaritasi);

        return veri;
    }

    private static string? Metin(SqlDataReader r, int sutun) => r.IsDBNull(sutun) ? null : r.GetString(sutun);

    /// <summary>
    /// On tablo tek pakette. Oy satirlari oyu cok olandan aza dogru siralaniyor;
    /// sahne sayfalari listeleri bu sirada bekliyor.
    /// </summary>
    private const string SORGU = @"
SELECT kod, ad FROM dbo.Grup ORDER BY sira, kod;

SELECT grupKod, plaka FROM dbo.GrupIl ORDER BY grupKod, sira;

SELECT plaka, ad, bolge, vekilKotasi FROM dbo.Il ORDER BY plaka;

SELECT kod, ad, renk, vizLogoImage, vizBarImage FROM dbo.Ittifak ORDER BY kod;

SELECT kod, ad, renk, vizBarImage FROM dbo.Secenek ORDER BY kod;

SELECT kod, ad, renk, ittifakKod, vizImage, vizBarImage, vizSatirImage, vizLogoImage,
       vizKiyasLogoImage, vizHaritaSeritImage, vizMvRozetImage,
       haritaRenk, haritaRenkGenel, meclisRenk, toplu
  FROM dbo.Parti ORDER BY kod;

SELECT kod, ad, tamAd, partiKod, ittifakKod, vizImage, vizBarImage, vizHaritaImage,
       vizSehirImage, vizTurImage, vizKarsilastirmaImage, haritaRenk, haritaRenkGenel
  FROM dbo.Aday ORDER BY kod;

SELECT kod, ad, tip, yil, barajTipi, barajOran FROM dbo.Secim ORDER BY sira, kod;

SELECT secimKod, plaka, toplamSandik, acilanSandik, secmenSayisi, gecersizOy, guncelleme
  FROM dbo.IlSonucu ORDER BY secimKod, plaka;

SELECT secimKod, plaka, kod, oy FROM dbo.Oy ORDER BY secimKod, plaka, oy DESC, kod;
";
}
