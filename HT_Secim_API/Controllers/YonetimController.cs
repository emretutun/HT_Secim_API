using HT_Secim_API.Guvenlik;
using HT_Secim_API.Model;
using HT_Secim_API.Veri;
using Microsoft.AspNetCore.Mvc;

namespace HT_Secim_API.Controllers;

/// <summary>
/// Yonetim panelinin kullandigi uclar. Hepsi YAZMA anahtari istiyor.
///
/// Panel HAM sayilari duzenliyor: sandik adedi, secmen sayisi, gecersiz oy
/// ve kalem kalem oy. Oran, katilim ve vekil hesaplanan degerler; panelde
/// gorunuyorlar ama elle girilemiyorlar.
/// </summary>
[ApiController]
[Route("api/v1/yonetim")]
[Produces("application/json")]
[YetkiGerekir(Yetki.Yazma)]
public sealed class YonetimController : ControllerBase
{
    private readonly YonetimDeposu depo;
    private readonly VeriDeposu veri;

    public YonetimController(YonetimDeposu depo, VeriDeposu veri)
    {
        this.depo = depo;
        this.veri = veri;
    }

    /// <summary> Panelin acilisinda: secim ve il listesi. </summary>
    [HttpGet("secenekler")]
    public async Task<ActionResult<object>> Secenekler(CancellationToken iptal)
    {
        SecimVerisi v = await veri.VeriAsync(iptal);

        return new
        {
            secimler = v.Secimler.Select(s => new { s.Kod, s.Ad, s.Tip, s.Yil }).ToList(),

            // Plaka 0 duzenlenmiyor: TURKIYE satiri illerden hesaplaniyor.
            iller = v.Iller.Where(i => i.Plaka != 0)
                           .Select(i => new { i.Plaka, i.Ad })
                           .ToList(),

            partiler   = v.Partiler.Select(p => new { p.Kod, p.Ad }).ToList(),
            adaylar    = v.Adaylar.Select(a => new { a.Kod, a.Ad }).ToList(),
            secenekler = v.Secenekler.Select(s => new { s.Kod, s.Ad }).ToList()
        };
    }

    /// <summary> Bir ilin duzenlenebilir ham degerleri. </summary>
    [HttpGet("sonuc/{secim}/{plaka:int}")]
    public async Task<ActionResult<YonetimSatiri>> Oku(string secim, int plaka, CancellationToken iptal)
    {
        if (plaka == 0)
            return BadRequest(new { hata = "TURKIYE satiri illerden hesaplaniyor, elle duzenlenemez." });

        YonetimSatiri? satir = await depo.OkuAsync(secim, plaka, iptal);

        if (satir is null)
            return NotFound(new { hata = $"{secim} seciminde {plaka} plakali il icin kayit yok." });

        // Kalem adlarini veriden tamamla.
        SecimVerisi v = await veri.VeriAsync(iptal);
        Secim? s = Arama.SecimBul(v, secim);

        if (s is not null)
        {
            foreach (YonetimOyu o in satir.Oylar)       o.Ad = Arama.KalemAdi(v, s, o.Kod);
            foreach (YonetimOyu o in satir.Eklenebilir) o.Ad = Arama.KalemAdi(v, s, o.Kod);
        }

        return satir;
    }

    /// <summary>
    /// Bir ilin degerlerini kaydeder.
    ///
    /// Degisen her alan denetim kaydina yaziliyor; trigger surumu artirdigi
    /// icin reji uygulamasi degisikligi 25 saniye icinde goruyor.
    /// </summary>
    [HttpPut("sonuc/{secim}/{plaka:int}")]
    public async Task<ActionResult<object>> Yaz(string secim, int plaka,
        [FromBody] YonetimKayit kayit, CancellationToken iptal)
    {
        if (plaka == 0)
            return BadRequest(new { hata = "TURKIYE satiri illerden hesaplaniyor, elle duzenlenemez." });

        if (kayit.AcilanSandikAdet > kayit.ToplamSandik)
            return BadRequest(new { hata = "Acilan sandik toplam sandiktan fazla olamaz." });

        if (kayit.ToplamSandik < 0 || kayit.SecmenSayisi < 0 || kayit.GecersizOy < 0)
            return BadRequest(new { hata = "Eksi deger girilemez." });

        long toplamOy = kayit.Oylar.Sum(o => o.Oy);

        if (kayit.SecmenSayisi > 0 && toplamOy + kayit.GecersizOy > kayit.SecmenSayisi)
            return BadRequest(new
            {
                hata = "Kullanilan oy secmen sayisindan fazla olamaz.",
                gecerli = toplamOy,
                gecersiz = kayit.GecersizOy,
                secmen = kayit.SecmenSayisi
            });

        try
        {
            int degisen = await depo.YazAsync(secim, plaka, kayit, iptal);

            return new { sonuc = "kaydedildi", degisenAlan = degisen, gecerliOy = toplamOy };
        }
        catch (Exception ex)
        {
            return BadRequest(new { hata = ex.Message });
        }
    }

    /// <summary> Son degisiklikler: kim, ne zaman, neyi degistirdi. </summary>
    [HttpGet("degisiklikler")]
    public async Task<ActionResult<List<DegisiklikSatiri>>> Degisiklikler(
        [FromQuery] int adet = 50, CancellationToken iptal = default)
    {
        return await depo.DegisikliklerAsync(adet, iptal);
    }
}
