using HT_Secim_API.Model;
using HT_Secim_API.Veri;
using Microsoft.AspNetCore.Mvc;

namespace HT_Secim_API.Controllers;

/// <summary>
/// Degismeyen tanimlar: iller, partiler, adaylar, ittifaklar, gruplar, secimler.
/// Hepsi bellekteki onbellekten donuyor, veritabanina gidilmiyor.
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class TanimlarController : ControllerBase
{
    private readonly VeriDeposu depo;

    public TanimlarController(VeriDeposu depo) => this.depo = depo;

    // ------------------------------------------------------------ iller

    /// <summary> Butun iller. Plaka 0 = Turkiye geneli, 900 = yurtdisi, 901 = gumruk. </summary>
    [HttpGet("iller")]
    public async Task<ActionResult<List<Il>>> Iller(CancellationToken iptal)
    {
        SecimVerisi v = await depo.VeriAsync(iptal);
        return v.Iller;
    }

    /// <summary> Tek il. Plaka ya da ad verilebilir: 6, 06, ankara, ANKARA. </summary>
    [HttpGet("iller/{il}")]
    public async Task<ActionResult<Il>> IlTek(string il, CancellationToken iptal)
    {
        SecimVerisi v = await depo.VeriAsync(iptal);

        Il? bulunan = Arama.IlBul(v, il);
        if (bulunan is null) return NotFound(new { hata = $"Il bulunamadi: {il}" });

        return bulunan;
    }

    // ---------------------------------------------------------- partiler

    [HttpGet("partiler")]
    public async Task<ActionResult<List<Parti>>> Partiler(CancellationToken iptal)
    {
        SecimVerisi v = await depo.VeriAsync(iptal);
        return v.Partiler;
    }

    [HttpGet("partiler/{kod}")]
    public async Task<ActionResult<Parti>> PartiTek(string kod, CancellationToken iptal)
    {
        SecimVerisi v = await depo.VeriAsync(iptal);

        string aranan = Arama.Sadelestir(kod);
        Parti? p = v.Partiler.FirstOrDefault(x => Arama.Sadelestir(x.Kod) == aranan);

        if (p is null) return NotFound(new { hata = $"Parti bulunamadi: {kod}" });

        return p;
    }

    // ----------------------------------------------------------- adaylar

    [HttpGet("adaylar")]
    public async Task<ActionResult<List<Aday>>> Adaylar(CancellationToken iptal)
    {
        SecimVerisi v = await depo.VeriAsync(iptal);
        return v.Adaylar;
    }

    [HttpGet("adaylar/{kod}")]
    public async Task<ActionResult<Aday>> AdayTek(string kod, CancellationToken iptal)
    {
        SecimVerisi v = await depo.VeriAsync(iptal);

        string aranan = Arama.Sadelestir(kod);
        Aday? a = v.Adaylar.FirstOrDefault(x => Arama.Sadelestir(x.Kod) == aranan);

        if (a is null) return NotFound(new { hata = $"Aday bulunamadi: {kod}" });

        return a;
    }

    // -------------------------------------------------------- ittifaklar

    [HttpGet("ittifaklar")]
    public async Task<ActionResult<List<Ittifak>>> Ittifaklar(CancellationToken iptal)
    {
        SecimVerisi v = await depo.VeriAsync(iptal);
        return v.Ittifaklar;
    }

    [HttpGet("ittifaklar/{kod}")]
    public async Task<ActionResult<object>> IttifakTek(string kod, CancellationToken iptal)
    {
        SecimVerisi v = await depo.VeriAsync(iptal);

        string aranan = Arama.Sadelestir(kod);
        Ittifak? it = v.Ittifaklar.FirstOrDefault(x => Arama.Sadelestir(x.Kod) == aranan);

        if (it is null) return NotFound(new { hata = $"Ittifak bulunamadi: {kod}" });

        // Ittifakin hangi partilerden olustugu ayrica tutulmuyor, partilerden cikariliyor.
        var partiler = v.Partiler
            .Where(p => Arama.Sadelestir(p.Ittifak) == aranan)
            .ToList();

        return new { it.Kod, it.Ad, it.Renk, it.VizLogoImage, it.VizBarImage, partiler };
    }

    // ----------------------------------------------------------- gruplar

    /// <summary> Il secicideki kategoriler: TURKIYE GENELI, ILLER, BUYUK SEHIRLER ... </summary>
    [HttpGet("gruplar")]
    public async Task<ActionResult<List<Grup>>> Gruplar(CancellationToken iptal)
    {
        SecimVerisi v = await depo.VeriAsync(iptal);
        return v.Gruplar;
    }

    /// <summary> Bir grubun icindeki iller (plaka degil, tam kayit). </summary>
    [HttpGet("gruplar/{kod}")]
    public async Task<ActionResult<object>> GrupTek(string kod, CancellationToken iptal)
    {
        SecimVerisi v = await depo.VeriAsync(iptal);

        string aranan = Arama.Sadelestir(kod);
        Grup? g = v.Gruplar.FirstOrDefault(x => Arama.Sadelestir(x.Kod) == aranan);

        if (g is null) return NotFound(new { hata = $"Grup bulunamadi: {kod}" });

        var iller = g.Plakalar
            .Select(p => v.Iller.FirstOrDefault(i => i.Plaka == p))
            .Where(i => i is not null)
            .ToList();

        return new { g.Kod, g.Ad, iller };
    }

    // ---------------------------------------------------------- secimler

    [HttpGet("secimler")]
    public async Task<ActionResult<object>> Secimler(CancellationToken iptal)
    {
        SecimVerisi v = await depo.VeriAsync(iptal);

        // Sonuc listesi cok buyuk; burada yalnizca basliklar donuyor.
        return v.Secimler
            .Select(s => new { s.Kod, s.Ad, s.Tip, s.Yil, ilSayisi = s.Sonuclar.Count })
            .ToList();
    }

    [HttpGet("secimler/{secim}")]
    public async Task<ActionResult<object>> SecimTek(string secim, CancellationToken iptal)
    {
        SecimVerisi v = await depo.VeriAsync(iptal);

        Secim? s = Arama.SecimBul(v, secim);
        if (s is null) return NotFound(new { hata = $"Secim bulunamadi: {secim}" });

        return new
        {
            s.Kod, s.Ad, s.Tip, s.Yil,
            barajTipi = s.BarajTipi,
            barajOran = s.BarajOran,
            barajYazi = s.BarajOran > 0 ? Arama.Yuzde(s.BarajOran) : null,
            ilSayisi = s.Sonuclar.Count
        };
    }
}
