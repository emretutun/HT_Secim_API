using System.Data;
using System.Text;
using HT_Secim_API.Model;
using Microsoft.Data.SqlClient;

namespace HT_Secim_API.Veri;

/// <summary>
/// Ajanstan gelen tam anlik goruntuyu veritabanina uygular.
///
/// Uc kural bu sinifin tamamini belirliyor:
///
///   1. ATOMIK - ya hepsi ya hicbiri. Yayin, yarim uygulanmis bir veriyi
///      ekrana veremez.
///   2. TEK SURUM ARTISI - aktarim boyunca surum baskilanir, sonunda bir
///      kez artar. C ekrani ya tamamen eskiyi ya tamamen yeniyi gorur.
///   3. GERI GITMEZ - acilan sandik ve oylar azalamaz. Bu tek kural,
///      gec gelen eski dosya ve ajans sifirlanmasi gibi hatalarin cogunu
///      yakaliyor.
///
/// Dogrulama YAZMADAN ONCE, ayni transaction icinde yapiliyor: gecici
/// tablolara yuklenip canli tabloyla karsilastiriliyor, sorun varsa
/// geri aliniyor.
/// </summary>
public sealed class BeslemeDeposu
{
    private readonly string baglanti;
    private readonly ILogger<BeslemeDeposu> log;

    public BeslemeDeposu(IConfiguration yapilandirma, ILogger<BeslemeDeposu> log)
    {
        this.log = log;

        baglanti = yapilandirma.GetConnectionString("HtSecim")
                   ?? throw new InvalidOperationException(
                       "appsettings.json icinde ConnectionStrings:HtSecim tanimli degil.");
    }

    public async Task<BeslemeCevabi> UygulaAsync(BeslemeIstegi istek, CancellationToken iptal)
    {
        BeslemeCevabi cevap = new();

        // --- once dosyanin kendi ic tutarliligi; veritabanina hic gitmeden
        string? icHata = IcTutarlilik(istek, cevap);

        if (icHata is not null)
        {
            cevap.Aciklama = icHata;
            await GecmiseYazAsync(istek, cevap, iptal);
            return cevap;
        }

        await using SqlConnection cn = new(baglanti);
        await cn.OpenAsync(iptal);

        await using SqlTransaction tx = (SqlTransaction)await cn.BeginTransactionAsync(iptal);

        bool baskilandi = false;

        try
        {
            await GeciciTablolarAsync(cn, tx, iptal);
            await YukleAsync(cn, tx, istek, iptal);

            // --- geri gitme ve bilinmeyen kod kontrolleri
            string? redSebebi = await CanliylaKarsilastirAsync(cn, tx, cevap, iptal);

            if (redSebebi is not null)
            {
                await tx.RollbackAsync(iptal);

                cevap.Aciklama = redSebebi;
                await GecmiseYazAsync(istek, cevap, iptal);
                return cevap;
            }

            // --- uygulama: buradan sonrasi tek parca
            await Calistir(cn, tx, "EXEC dbo.sp_BeslemeBaslat;", iptal);
            baskilandi = true;

            cevap.IlSayisi = await Calistir(cn, tx, SQL_ILSONUC_MERGE, iptal);
            cevap.OySatiri = await Calistir(cn, tx, SQL_OY_MERGE, iptal);

            string aciklama = $"besleme {istek.Kaynak} #{istek.Sira}";

            await using (SqlCommand bitir = new("dbo.sp_BeslemeBitir", cn, tx)
                         { CommandType = CommandType.StoredProcedure })
            {
                bitir.Parameters.AddWithValue("@aciklama", aciklama);
                await bitir.ExecuteNonQueryAsync(iptal);
            }

            baskilandi = false;

            cevap.Surum = await SurumOkuAsync(cn, tx, iptal);
            cevap.Kabul = true;
            cevap.Aciklama = $"{cevap.IlSayisi} il / {cevap.OySatiri} oy satiri uygulandi";

            await tx.CommitAsync(iptal);
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(iptal); } catch { /* baglanti zaten kopmus olabilir */ }

            log.LogError(ex, "Besleme uygulanamadi. Kaynak {Kaynak} #{Sira}", istek.Kaynak, istek.Sira);

            cevap.Kabul = false;
            cevap.Aciklama = "Veritabani hatasi: " + ex.Message;
        }
        finally
        {
            // Baskilama bayragi baglantiya yapisik: temizlenmezse bu
            // baglanti havuzdan bir daha alindiginda surum artirmaz
            // hale gelir ve ekran guncellenmeyi sessizce birakir.
            if (baskilandi) await BaskilamayiKaldirAsync(cn, iptal);
        }

        await GecmiseYazAsync(istek, cevap, iptal);
        return cevap;
    }

    // ------------------------------------------------------- ic tutarlilik

    /// <summary>
    /// Dosyanin kendi icinde tutarli olup olmadigi. Veritabanina hic
    /// gidilmeden bakiliyor; bozuk dosya SQL'i hic mesgul etmesin.
    ///
    /// Red sebebi doner, sorun yoksa null.
    /// </summary>
    private static string? IcTutarlilik(BeslemeIstegi istek, BeslemeCevabi cevap)
    {
        if (istek.Secimler.Count == 0) return "Istekte secim yok.";

        foreach (BeslemeSecim secim in istek.Secimler)
        {
            if (string.IsNullOrWhiteSpace(secim.Kod)) return "Secim kodu bos.";
            if (secim.Iller.Count == 0) return $"{secim.Kod}: il yok.";

            foreach (BeslemeIl il in secim.Iller)
            {
                string nerede = $"{secim.Kod}/{il.Plaka}";

                // TURKIYE satiri illerden hesaplaniyor, disaridan gelemez.
                if (il.Plaka == 0)
                    return $"{nerede}: TURKIYE satiri (plaka 0) beslemeden gonderilemez.";

                if (il.ToplamSandik < 0 || il.AcilanSandik < 0)
                    return $"{nerede}: sandik sayisi negatif.";

                if (il.ToplamSandik > 0 && il.AcilanSandik > il.ToplamSandik)
                    return $"{nerede}: acilan sandik ({il.AcilanSandik}) toplamdan ({il.ToplamSandik}) fazla.";

                long oyToplami = 0;

                foreach (BeslemeOy oy in il.Oylar)
                {
                    if (oy.Sayi < 0) return $"{nerede}/{oy.Kod}: oy negatif.";
                    oyToplami += oy.Sayi;
                }

                // Ajansin bildirdigi gecerli oy, kalemlerin toplamindan
                // kucuk olamaz. Esit olmasi da gerekmiyor - ajans bazi
                // kucuk partileri gondermeyebilir.
                if (il.GecerliOy > 0 && oyToplami > il.GecerliOy)
                    return $"{nerede}: oylarin toplami ({oyToplami}) gecerli oydan ({il.GecerliOy}) fazla.";

                if (il.SecmenSayisi > 0 && oyToplami + il.GecersizOy > il.SecmenSayisi)
                    return $"{nerede}: kullanilan oy secmen sayisini asiyor.";
            }
        }

        return null;
    }

    // --------------------------------------------------------- yukleme

    private static async Task GeciciTablolarAsync(SqlConnection cn, SqlTransaction tx, CancellationToken iptal)
    {
        // Gecici tablolar kalici staging yerine tercih edildi: iki besleme
        // ayni anda gelirse birbirinin satirlarini gormezler.
        await Calistir(cn, tx, @"
CREATE TABLE #Gelen
(
    secimKod     NVARCHAR(32) NOT NULL,
    plaka        INT          NOT NULL,
    toplamSandik INT          NOT NULL,
    acilanSandik INT          NOT NULL,
    secmenSayisi BIGINT       NOT NULL,
    gecersizOy   BIGINT       NOT NULL,
    PRIMARY KEY (secimKod, plaka)
);

CREATE TABLE #GelenOy
(
    secimKod NVARCHAR(32) NOT NULL,
    plaka    INT          NOT NULL,
    kod      NVARCHAR(32) NOT NULL,
    oy       BIGINT       NOT NULL,
    PRIMARY KEY (secimKod, plaka, kod)
);", iptal);
    }

    private static async Task YukleAsync(SqlConnection cn, SqlTransaction tx,
        BeslemeIstegi istek, CancellationToken iptal)
    {
        DataTable iller = new();
        iller.Columns.Add("secimKod", typeof(string));
        iller.Columns.Add("plaka", typeof(int));
        iller.Columns.Add("toplamSandik", typeof(int));
        iller.Columns.Add("acilanSandik", typeof(int));
        iller.Columns.Add("secmenSayisi", typeof(long));
        iller.Columns.Add("gecersizOy", typeof(long));

        DataTable oylar = new();
        oylar.Columns.Add("secimKod", typeof(string));
        oylar.Columns.Add("plaka", typeof(int));
        oylar.Columns.Add("kod", typeof(string));
        oylar.Columns.Add("oy", typeof(long));

        foreach (BeslemeSecim secim in istek.Secimler)
        foreach (BeslemeIl il in secim.Iller)
        {
            iller.Rows.Add(secim.Kod, il.Plaka, il.ToplamSandik,
                il.AcilanSandik, il.SecmenSayisi, il.GecersizOy);

            foreach (BeslemeOy oy in il.Oylar)
                oylar.Rows.Add(secim.Kod, il.Plaka, oy.Kod, oy.Sayi);
        }

        await KopyalaAsync(cn, tx, iller, "#Gelen", iptal);
        await KopyalaAsync(cn, tx, oylar, "#GelenOy", iptal);
    }

    private static async Task KopyalaAsync(SqlConnection cn, SqlTransaction tx,
        DataTable tablo, string hedef, CancellationToken iptal)
    {
        using SqlBulkCopy kopya = new(cn, SqlBulkCopyOptions.Default, tx)
        {
            DestinationTableName = hedef,
            BulkCopyTimeout = 60
        };

        foreach (DataColumn sutun in tablo.Columns)
            kopya.ColumnMappings.Add(sutun.ColumnName, sutun.ColumnName);

        await kopya.WriteToServerAsync(tablo, iptal);
    }

    // ------------------------------------------------------- dogrulama

    /// <summary>
    /// Gelen veriyi canlı veriyle karsilastirir.
    ///
    /// GERI GITME: bir ilde acilan sandik ya da bir kalemin oyu kayitli
    /// degerden dusukse dosyanin TAMAMI reddedilir. Tek bir ili duzeltip
    /// gerisini almak, ekranda birbirini tutmayan sayilar birakirdi.
    ///
    /// Bilinmeyen secim / il / kalem kodu reddettirmiyor, uyari uretiyor:
    /// ajans yeni bir parti eklediginde butun gece durmamali.
    /// </summary>
    private static async Task<string?> CanliylaKarsilastirAsync(SqlConnection cn, SqlTransaction tx,
        BeslemeCevabi cevap, CancellationToken iptal)
    {
        // --- acilan sandik geriye gitmis mi
        string? geri = await TekDegerAsync(cn, tx, @"
SELECT TOP 1 CONCAT(g.secimKod, '/', g.plaka, ': acilan sandik ',
                    c.acilanSandik, ' -> ', g.acilanSandik, ' (geri gitti)')
  FROM #Gelen g
  JOIN dbo.IlSonucu c ON c.secimKod = g.secimKod AND c.plaka = g.plaka
 WHERE g.acilanSandik < c.acilanSandik;", iptal);

        if (geri is not null) return geri;

        // --- oy geriye gitmis mi
        geri = await TekDegerAsync(cn, tx, @"
SELECT TOP 1 CONCAT(g.secimKod, '/', g.plaka, '/', g.kod, ': oy ',
                    c.oy, ' -> ', g.oy, ' (geri gitti)')
  FROM #GelenOy g
  JOIN dbo.Oy c ON c.secimKod = g.secimKod AND c.plaka = g.plaka AND c.kod = g.kod
 WHERE g.oy < c.oy;", iptal);

        if (geri is not null) return geri;

        // --- tanimsiz secim: bunlar yazilamaz, yabanci anahtar engeller
        string? tanimsizSecim = await TekDegerAsync(cn, tx, @"
SELECT TOP 1 CONCAT('tanimsiz secim kodu: ', g.secimKod)
  FROM #Gelen g
 WHERE NOT EXISTS (SELECT 1 FROM dbo.Secim s WHERE s.kod = g.secimKod);", iptal);

        if (tanimsizSecim is not null) return tanimsizSecim;

        // --- tanimsiz il
        string? tanimsizIl = await TekDegerAsync(cn, tx, @"
SELECT TOP 1 CONCAT('tanimsiz plaka: ', g.plaka)
  FROM #Gelen g
 WHERE NOT EXISTS (SELECT 1 FROM dbo.Il i WHERE i.plaka = g.plaka);", iptal);

        if (tanimsizIl is not null) return tanimsizIl;

        // --- tanimsiz kalem: uyari, red degil. Yazilmadan atiliyor.
        await UyarilariTopla(cn, tx, cevap, iptal);

        return null;
    }

    private static async Task UyarilariTopla(SqlConnection cn, SqlTransaction tx,
        BeslemeCevabi cevap, CancellationToken iptal)
    {
        await using SqlCommand komut = new(@"
SELECT DISTINCT TOP 20 g.secimKod, g.kod
  FROM #GelenOy g
 WHERE NOT EXISTS (SELECT 1 FROM dbo.Parti   p WHERE p.kod = g.kod)
   AND NOT EXISTS (SELECT 1 FROM dbo.Aday    a WHERE a.kod = g.kod)
   AND NOT EXISTS (SELECT 1 FROM dbo.Secenek s WHERE s.kod = g.kod);", cn, tx);

        await using SqlDataReader r = await komut.ExecuteReaderAsync(iptal);

        while (await r.ReadAsync(iptal))
            cevap.Uyarilar.Add($"tanimsiz kalem atlandi: {r.GetString(0)}/{r.GetString(1)}");
    }

    // --------------------------------------------------------- uygulama

    private const string SQL_ILSONUC_MERGE = @"
MERGE dbo.IlSonucu AS h
USING #Gelen AS g
   ON h.secimKod = g.secimKod AND h.plaka = g.plaka
 WHEN MATCHED THEN UPDATE SET
        h.toplamSandik = g.toplamSandik,
        h.acilanSandik = g.acilanSandik,
        h.secmenSayisi = g.secmenSayisi,
        h.gecersizOy   = g.gecersizOy,
        h.guncelleme   = SYSDATETIME()
 WHEN NOT MATCHED BY TARGET THEN
      INSERT (secimKod, plaka, toplamSandik, acilanSandik, secmenSayisi, gecersizOy, guncelleme)
      VALUES (g.secimKod, g.plaka, g.toplamSandik, g.acilanSandik, g.secmenSayisi, g.gecersizOy, SYSDATETIME());";

    /* Tanimsiz kalemler yazilmiyor (uyari olarak bildirildi).
       Gelmeyene DOKUNULMUYOR: ajans bir partiyi o turda gondermediyse
       kayitli degeri silmek yerine yerinde birakiyoruz - eksik gonderim
       ekranda o partiyi sifirlamamali. */
    private const string SQL_OY_MERGE = @"
MERGE dbo.Oy AS h
USING (SELECT g.* FROM #GelenOy g
        WHERE EXISTS (SELECT 1 FROM dbo.Parti   p WHERE p.kod = g.kod)
           OR EXISTS (SELECT 1 FROM dbo.Aday    a WHERE a.kod = g.kod)
           OR EXISTS (SELECT 1 FROM dbo.Secenek s WHERE s.kod = g.kod)) AS g
   ON h.secimKod = g.secimKod AND h.plaka = g.plaka AND h.kod = g.kod
 WHEN MATCHED THEN UPDATE SET h.oy = g.oy
 WHEN NOT MATCHED BY TARGET THEN
      INSERT (secimKod, plaka, kod, oy) VALUES (g.secimKod, g.plaka, g.kod, g.oy);";

    // ----------------------------------------------------------- yardim

    private static async Task<int> Calistir(SqlConnection cn, SqlTransaction tx,
        string sql, CancellationToken iptal)
    {
        await using SqlCommand komut = new(sql, cn, tx) { CommandTimeout = 120 };
        return await komut.ExecuteNonQueryAsync(iptal);
    }

    private static async Task<string?> TekDegerAsync(SqlConnection cn, SqlTransaction tx,
        string sql, CancellationToken iptal)
    {
        await using SqlCommand komut = new(sql, cn, tx);
        object? sonuc = await komut.ExecuteScalarAsync(iptal);

        return sonuc is null || sonuc is DBNull ? null : sonuc.ToString();
    }

    private static async Task<int> SurumOkuAsync(SqlConnection cn, SqlTransaction tx, CancellationToken iptal)
    {
        await using SqlCommand komut = new("SELECT surum FROM dbo.VeriSurum WHERE id = 1;", cn, tx);
        object? sonuc = await komut.ExecuteScalarAsync(iptal);

        return sonuc is int s ? s : 0;
    }

    private static async Task BaskilamayiKaldirAsync(SqlConnection cn, CancellationToken iptal)
    {
        try
        {
            await using SqlCommand komut = new(
                "EXEC sys.sp_set_session_context @key = N'surum_baskila', @value = 0;", cn);

            await komut.ExecuteNonQueryAsync(iptal);
        }
        catch
        {
            // Baglanti zaten kopmussa bayrak da onunla gidiyor.
        }
    }

    /// <summary>
    /// Gecmise yaziyor - kabul da red de.
    ///
    /// AYRI BAGLANTI kullaniliyor: red durumunda ana transaction geri
    /// alindi, ayni transaction icinde yazilsaydi red kaydi da silinirdi.
    /// Halbuki "ajans veri gonderiyor ama ekran degismiyor" durumunun tek
    /// aciklamasi bu kayit olur.
    /// </summary>
    private async Task GecmiseYazAsync(BeslemeIstegi istek, BeslemeCevabi cevap, CancellationToken iptal)
    {
        try
        {
            await using SqlConnection cn = new(baglanti);
            await cn.OpenAsync(iptal);

            await using SqlCommand komut = new(@"
INSERT INTO dbo.BeslemeGecmis(kaynak, dosya, sira, kabul, ilSayisi, oySatiri, aciklama)
VALUES(@kaynak, @dosya, @sira, @kabul, @il, @oy, @aciklama);", cn);

            StringBuilder aciklama = new(cevap.Aciklama);

            foreach (string uyari in cevap.Uyarilar)
            {
                if (aciklama.Length > 400) break;
                aciklama.Append(" | ").Append(uyari);
            }

            komut.Parameters.AddWithValue("@kaynak", istek.Kaynak);
            komut.Parameters.AddWithValue("@dosya", (object?)istek.Dosya ?? DBNull.Value);
            komut.Parameters.AddWithValue("@sira", istek.Sira);
            komut.Parameters.AddWithValue("@kabul", cevap.Kabul);
            komut.Parameters.AddWithValue("@il", cevap.IlSayisi);
            komut.Parameters.AddWithValue("@oy", cevap.OySatiri);
            komut.Parameters.AddWithValue("@aciklama",
                aciklama.Length > 500 ? aciklama.ToString(0, 500) : aciklama.ToString());

            await komut.ExecuteNonQueryAsync(iptal);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Besleme gecmisi yazilamadi.");
        }
    }

    /// <summary>
    /// Secimlerin sayaclarini sifirlar: acilan sandik 0, gecersiz oy 0,
    /// butun oylar 0.
    ///
    /// TOPLAM SANDIK VE SECMEN SAYISI KALIR. Onlar geceden once bilinen
    /// degerler; sayilacak olan sey degil, sayimin paydasi. Silinselerdi
    /// ekran gece boyunca "acilan sandik %0 / 0" gosterirdi.
    ///
    /// Tek transaction, tek surum artisi - sifirlama da yarim gorunmemeli.
    /// </summary>
    public async Task<BeslemeCevabi> SifirlaAsync(List<string> secimler, CancellationToken iptal)
    {
        BeslemeCevabi cevap = new();

        await using SqlConnection cn = new(baglanti);
        await cn.OpenAsync(iptal);

        await using SqlTransaction tx = (SqlTransaction)await cn.BeginTransactionAsync(iptal);

        bool baskilandi = false;

        try
        {
            await Calistir(cn, tx, "EXEC dbo.sp_BeslemeBaslat;", iptal);
            baskilandi = true;

            // Secim kodlari parametreli gecirilyor; string birlestirme ile
            // SQL kurmak enjeksiyona acik olurdu.
            string liste = string.Join(",", secimler.Select((_, i) => $"@s{i}"));

            await using (SqlCommand sonuc = new(
                $@"UPDATE dbo.IlSonucu
                      SET acilanSandik = 0, gecersizOy = 0, guncelleme = SYSDATETIME()
                    WHERE secimKod IN ({liste});", cn, tx))
            {
                for (int i = 0; i < secimler.Count; i++)
                    sonuc.Parameters.AddWithValue($"@s{i}", secimler[i]);

                cevap.IlSayisi = await sonuc.ExecuteNonQueryAsync(iptal);
            }

            await using (SqlCommand oy = new(
                $"UPDATE dbo.Oy SET oy = 0 WHERE secimKod IN ({liste});", cn, tx))
            {
                for (int i = 0; i < secimler.Count; i++)
                    oy.Parameters.AddWithValue($"@s{i}", secimler[i]);

                cevap.OySatiri = await oy.ExecuteNonQueryAsync(iptal);
            }

            await using (SqlCommand bitir = new("dbo.sp_BeslemeBitir", cn, tx)
                         { CommandType = CommandType.StoredProcedure })
            {
                bitir.Parameters.AddWithValue("@aciklama",
                    "sifirlama: " + string.Join(", ", secimler));

                await bitir.ExecuteNonQueryAsync(iptal);
            }

            baskilandi = false;

            cevap.Surum = await SurumOkuAsync(cn, tx, iptal);
            cevap.Kabul = true;
            cevap.Aciklama = $"{cevap.IlSayisi} il satiri / {cevap.OySatiri} oy satiri sifirlandi";

            await tx.CommitAsync(iptal);

            log.LogWarning("SAYACLAR SIFIRLANDI: {Secimler} - {Il} il, {Oy} oy satiri, surum {Surum}",
                string.Join(", ", secimler), cevap.IlSayisi, cevap.OySatiri, cevap.Surum);
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(iptal); } catch { }

            log.LogError(ex, "Sifirlama basarisiz.");

            cevap.Kabul = false;
            cevap.Aciklama = "Veritabani hatasi: " + ex.Message;
        }
        finally
        {
            if (baskilandi) await BaskilamayiKaldirAsync(cn, iptal);
        }

        await GecmiseYazAsync(new BeslemeIstegi
        {
            Kaynak = "SIFIRLAMA",
            Dosya = string.Join(", ", secimler)
        }, cevap, iptal);

        return cevap;
    }

    /// <summary> Saglik ekrani icin: son besleme denemeleri. </summary>
    public async Task<List<BeslemeGecmisSatiri>> GecmisAsync(int adet, CancellationToken iptal)
    {
        List<BeslemeGecmisSatiri> sonuc = new();

        await using SqlConnection cn = new(baglanti);
        await cn.OpenAsync(iptal);

        await using SqlCommand komut = new(@"
SELECT TOP (@adet) zaman, kaynak, dosya, sira, kabul, ilSayisi, oySatiri, aciklama
  FROM dbo.BeslemeGecmis ORDER BY id DESC;", cn);

        komut.Parameters.AddWithValue("@adet", adet);

        await using SqlDataReader r = await komut.ExecuteReaderAsync(iptal);

        while (await r.ReadAsync(iptal))
            sonuc.Add(new BeslemeGecmisSatiri
            {
                Zaman    = r.GetDateTime(0),
                Kaynak   = r.GetString(1),
                Dosya    = r.IsDBNull(2) ? null : r.GetString(2),
                Sira     = r.GetInt64(3),
                Kabul    = r.GetBoolean(4),
                IlSayisi = r.GetInt32(5),
                OySatiri = r.GetInt32(6),
                Aciklama = r.IsDBNull(7) ? "" : r.GetString(7)
            });

        return sonuc;
    }
}

public sealed class BeslemeGecmisSatiri
{
    public DateTime Zaman { get; set; }
    public string Kaynak { get; set; } = "";
    public string? Dosya { get; set; }
    public long Sira { get; set; }
    public bool Kabul { get; set; }
    public int IlSayisi { get; set; }
    public int OySatiri { get; set; }
    public string Aciklama { get; set; } = "";
}
