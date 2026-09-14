using System.Data;
using HT_Secim_API.Model;
using Microsoft.Data.SqlClient;

namespace HT_Secim_API.Veri;

/// <summary>
/// Yonetim panelinin veritabani islemleri.
///
/// Okuma tarafi VeriDeposu'ndan AYRI: panel HAM sayilari duzenliyor,
/// hesaplanmis oranlari degil. Onbellek de kullanilmiyor, her zaman
/// veritabanindaki guncel hali gosteriyor.
///
/// Her yazma islemi:
///   - tek transaction icinde yapilir
///   - onceki degerlerle karsilastirilip Degisiklik tablosuna yazilir
///   - trigger'lar sayesinde surumu artirir, reji 25 sn icinde gorur
/// </summary>
public sealed class YonetimDeposu
{
    private readonly string baglanti;
    private readonly ILogger<YonetimDeposu> log;

    public YonetimDeposu(IConfiguration yapilandirma, ILogger<YonetimDeposu> log)
    {
        this.log = log;

        baglanti = yapilandirma.GetConnectionString("HtSecim")
                   ?? throw new InvalidOperationException(
                       "appsettings.json icinde ConnectionStrings:HtSecim tanimli degil.");
    }

    /// <summary> Bir ilin duzenlenebilir ham degerleri. Kayit yoksa null. </summary>
    public async Task<YonetimSatiri?> OkuAsync(string secimKod, int plaka, CancellationToken iptal)
    {
        await using var cn = new SqlConnection(baglanti);
        await cn.OpenAsync(iptal);

        var satir = new YonetimSatiri { Secim = secimKod, Plaka = plaka };

        await using (var cmd = new SqlCommand(@"
SELECT s.ad, s.tip, i.ad, o.toplamSandik, o.acilanSandik, o.secmenSayisi, o.gecersizOy, o.guncelleme
  FROM dbo.IlSonucu o
  JOIN dbo.Secim s ON s.kod = o.secimKod
  JOIN dbo.Il    i ON i.plaka = o.plaka
 WHERE o.secimKod = @secim AND o.plaka = @plaka;", cn))
        {
            cmd.Parameters.AddWithValue("@secim", secimKod);
            cmd.Parameters.AddWithValue("@plaka", plaka);

            await using SqlDataReader r = await cmd.ExecuteReaderAsync(iptal);
            if (!await r.ReadAsync(iptal)) return null;

            satir.SecimAdi         = r.GetString(0);
            satir.Tip              = r.GetString(1);
            satir.Il               = r.GetString(2);
            satir.ToplamSandik     = r.GetInt32(3);
            satir.AcilanSandikAdet = r.GetInt32(4);
            satir.SecmenSayisi     = r.GetInt64(5);
            satir.GecersizOy       = r.GetInt64(6);
            satir.Guncelleme       = r.GetDateTime(7);
        }

        // Mevcut kalemler, oyu cok olandan aza.
        await using (var cmd = new SqlCommand(
            "SELECT kod, oy FROM dbo.Oy WHERE secimKod = @secim AND plaka = @plaka ORDER BY oy DESC, kod;", cn))
        {
            cmd.Parameters.AddWithValue("@secim", secimKod);
            cmd.Parameters.AddWithValue("@plaka", plaka);

            await using SqlDataReader r = await cmd.ExecuteReaderAsync(iptal);
            while (await r.ReadAsync(iptal))
                satir.Oylar.Add(new YonetimOyu { Kod = r.GetString(0), Oy = r.GetInt64(1) });
        }

        // Bu secimde baska illerde gecen ama burada kaydi olmayan kalemler.
        await using (var cmd = new SqlCommand(@"
SELECT DISTINCT kod FROM dbo.Oy
 WHERE secimKod = @secim
   AND kod NOT IN (SELECT kod FROM dbo.Oy WHERE secimKod = @secim AND plaka = @plaka)
 ORDER BY kod;", cn))
        {
            cmd.Parameters.AddWithValue("@secim", secimKod);
            cmd.Parameters.AddWithValue("@plaka", plaka);

            await using SqlDataReader r = await cmd.ExecuteReaderAsync(iptal);
            while (await r.ReadAsync(iptal))
                satir.Eklenebilir.Add(new YonetimOyu { Kod = r.GetString(0), Oy = 0 });
        }

        return satir;
    }

    /// <summary>
    /// Bir ilin degerlerini kaydeder. Degisen her alan denetim kaydina yazilir.
    /// Kac alanin degistigini dondurur.
    /// </summary>
    public async Task<int> YazAsync(string secimKod, int plaka, YonetimKayit kayit, CancellationToken iptal)
    {
        await using var cn = new SqlConnection(baglanti);
        await cn.OpenAsync(iptal);

        await using SqlTransaction tx = (SqlTransaction)await cn.BeginTransactionAsync(iptal);

        int degisen = 0;
        string kullanici = string.IsNullOrWhiteSpace(kayit.Kullanici) ? "bilinmiyor" : kayit.Kullanici.Trim();

        try
        {
            // --- eski degerler ---
            int eskiToplam = 0, eskiAcilan = 0;
            long eskiSecmen = 0, eskiGecersiz = 0;

            await using (var cmd = new SqlCommand(
                "SELECT toplamSandik, acilanSandik, secmenSayisi, gecersizOy FROM dbo.IlSonucu WHERE secimKod=@s AND plaka=@p;",
                cn, tx))
            {
                cmd.Parameters.AddWithValue("@s", secimKod);
                cmd.Parameters.AddWithValue("@p", plaka);

                await using SqlDataReader r = await cmd.ExecuteReaderAsync(iptal);
                if (!await r.ReadAsync(iptal))
                    throw new InvalidOperationException($"{secimKod} / {plaka} icin satir yok.");

                eskiToplam   = r.GetInt32(0);
                eskiAcilan   = r.GetInt32(1);
                eskiSecmen   = r.GetInt64(2);
                eskiGecersiz = r.GetInt64(3);
            }

            var eskiOylar = new Dictionary<string, long>(StringComparer.Ordinal);

            await using (var cmd = new SqlCommand(
                "SELECT kod, oy FROM dbo.Oy WHERE secimKod=@s AND plaka=@p;", cn, tx))
            {
                cmd.Parameters.AddWithValue("@s", secimKod);
                cmd.Parameters.AddWithValue("@p", plaka);

                await using SqlDataReader r = await cmd.ExecuteReaderAsync(iptal);
                while (await r.ReadAsync(iptal)) eskiOylar[r.GetString(0)] = r.GetInt64(1);
            }

            // --- sandik / secmen ---
            if (eskiToplam != kayit.ToplamSandik || eskiAcilan != kayit.AcilanSandikAdet ||
                eskiSecmen != kayit.SecmenSayisi || eskiGecersiz != kayit.GecersizOy)
            {
                await using var cmd = new SqlCommand(@"
UPDATE dbo.IlSonucu
   SET toplamSandik = @toplam, acilanSandik = @acilan,
       secmenSayisi = @secmen, gecersizOy = @gecersiz,
       guncelleme   = SYSDATETIME()
 WHERE secimKod = @s AND plaka = @p;", cn, tx);

                cmd.Parameters.AddWithValue("@toplam",   kayit.ToplamSandik);
                cmd.Parameters.AddWithValue("@acilan",   kayit.AcilanSandikAdet);
                cmd.Parameters.AddWithValue("@secmen",   kayit.SecmenSayisi);
                cmd.Parameters.AddWithValue("@gecersiz", kayit.GecersizOy);
                cmd.Parameters.AddWithValue("@s", secimKod);
                cmd.Parameters.AddWithValue("@p", plaka);

                await cmd.ExecuteNonQueryAsync(iptal);

                degisen += await KaydetAsync(cn, tx, kullanici, secimKod, plaka, "toplamSandik", eskiToplam, kayit.ToplamSandik, kayit.Aciklama, iptal);
                degisen += await KaydetAsync(cn, tx, kullanici, secimKod, plaka, "acilanSandik", eskiAcilan, kayit.AcilanSandikAdet, kayit.Aciklama, iptal);
                degisen += await KaydetAsync(cn, tx, kullanici, secimKod, plaka, "secmenSayisi", eskiSecmen, kayit.SecmenSayisi, kayit.Aciklama, iptal);
                degisen += await KaydetAsync(cn, tx, kullanici, secimKod, plaka, "gecersizOy",   eskiGecersiz, kayit.GecersizOy, kayit.Aciklama, iptal);
            }

            // --- oylar ---
            foreach (YonetimOyu o in kayit.Oylar)
            {
                if (string.IsNullOrWhiteSpace(o.Kod)) continue;
                if (o.Oy < 0) throw new InvalidOperationException($"{o.Kod} icin eksi oy olamaz.");

                eskiOylar.TryGetValue(o.Kod, out long eski);
                bool vardi = eskiOylar.ContainsKey(o.Kod);

                if (vardi && eski == o.Oy) continue;

                await using var cmd = new SqlCommand(vardi
                    ? "UPDATE dbo.Oy SET oy = @oy WHERE secimKod=@s AND plaka=@p AND kod=@k;"
                    : "INSERT INTO dbo.Oy(secimKod, plaka, kod, oy) VALUES(@s, @p, @k, @oy);", cn, tx);

                cmd.Parameters.AddWithValue("@oy", o.Oy);
                cmd.Parameters.AddWithValue("@s", secimKod);
                cmd.Parameters.AddWithValue("@p", plaka);
                cmd.Parameters.AddWithValue("@k", o.Kod);

                await cmd.ExecuteNonQueryAsync(iptal);

                degisen += await KaydetAsync(cn, tx, kullanici, secimKod, plaka,
                    "oy:" + o.Kod, vardi ? eski : (long?)null, o.Oy, kayit.Aciklama, iptal);
            }

            await tx.CommitAsync(iptal);

            log.LogInformation("Yonetim kaydi: {Secim}/{Plaka}, {Adet} alan degisti, kullanici {Kullanici}.",
                secimKod, plaka, degisen, kullanici);

            return degisen;
        }
        catch
        {
            await tx.RollbackAsync(iptal);
            throw;
        }
    }

    /// <summary> Denetim kaydina bir satir. Deger degismediyse yazmaz. </summary>
    private static async Task<int> KaydetAsync(SqlConnection cn, SqlTransaction tx, string kullanici,
        string secimKod, int plaka, string alan, long? eski, long? yeni, string? aciklama,
        CancellationToken iptal)
    {
        if (eski == yeni) return 0;

        await using var cmd = new SqlCommand(@"
INSERT INTO dbo.Degisiklik(kaynak, kullanici, secimKod, plaka, alan, eski, yeni, aciklama)
VALUES('YONETIM', @kullanici, @s, @p, @alan, @eski, @yeni, @aciklama);", cn, tx);

        cmd.Parameters.AddWithValue("@kullanici", kullanici);
        cmd.Parameters.AddWithValue("@s", secimKod);
        cmd.Parameters.AddWithValue("@p", plaka);
        cmd.Parameters.AddWithValue("@alan", alan);
        cmd.Parameters.AddWithValue("@eski", (object?)eski ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@yeni", (object?)yeni ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@aciklama", (object?)aciklama ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(iptal);
        return 1;
    }

    /// <summary> Son degisiklikler, yeniden eskiye. </summary>
    public async Task<List<DegisiklikSatiri>> DegisikliklerAsync(int adet, CancellationToken iptal)
    {
        if (adet < 1) adet = 1;
        if (adet > 500) adet = 500;

        var liste = new List<DegisiklikSatiri>();

        await using var cn = new SqlConnection(baglanti);
        await cn.OpenAsync(iptal);

        await using var cmd = new SqlCommand(@"
SELECT TOP (@adet) d.id, d.zaman, d.kaynak, d.kullanici, d.secimKod, d.plaka,
       i.ad, d.alan, d.eski, d.yeni, d.aciklama
  FROM dbo.Degisiklik d
  LEFT JOIN dbo.Il i ON i.plaka = d.plaka
 ORDER BY d.id DESC;", cn);

        cmd.Parameters.AddWithValue("@adet", adet);

        await using SqlDataReader r = await cmd.ExecuteReaderAsync(iptal);

        while (await r.ReadAsync(iptal))
        {
            liste.Add(new DegisiklikSatiri
            {
                Id        = r.GetInt64(0),
                Zaman     = r.GetDateTime(1),
                Kaynak    = r.GetString(2),
                Kullanici = r.IsDBNull(3) ? null : r.GetString(3),
                Secim     = r.GetString(4),
                Plaka     = r.GetInt32(5),
                Il        = r.IsDBNull(6) ? null : r.GetString(6),
                Alan      = r.GetString(7),
                Eski      = r.IsDBNull(8) ? null : r.GetInt64(8),
                Yeni      = r.IsDBNull(9) ? null : r.GetInt64(9),
                Aciklama  = r.IsDBNull(10) ? null : r.GetString(10)
            });
        }

        return liste;
    }
}
