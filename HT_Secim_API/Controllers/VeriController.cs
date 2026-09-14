using HT_Secim_API.Model;
using HT_Secim_API.Veri;
using Microsoft.AspNetCore.Mvc;

namespace HT_Secim_API.Controllers;

/// <summary>
/// Reji uygulamasinin kullandigi sistem uclari.
///
/// Akis:
///   1. Uygulama 25 saniyede bir /api/v1/surum'u sorar (birkac yuz bayt).
///   2. Numara degistiyse /api/v1/veri'yi ceker (~200 KB).
/// Boylece veri degismedigi surece ag uzerinde neredeyse hic trafik olmuyor.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class VeriController : ControllerBase
{
    private readonly VeriDeposu depo;
    private readonly int bayatDakika;

    public VeriController(VeriDeposu depo, IConfiguration yapilandirma)
    {
        this.depo = depo;

        // Veri bu kadar dakikadir degismiyorsa besleme durmus olabilir.
        bayatDakika = yapilandirma.GetValue("HtSecim:BayatDakika", 10);
    }

    /// <summary>
    /// Servis ayakta mi, elindeki veri ne durumda.
    /// Izleme sistemi ve reji gostergesi bunu okuyor.
    /// </summary>
    [HttpGet("saglik")]
    [Produces("application/json")]
    public async Task<ActionResult<Saglik>> SaglikDurumu(CancellationToken iptal)
    {
        SecimVerisi? v = null;
        int surum = 0;

        try
        {
            v = await depo.VeriAsync(iptal);
            surum = (await depo.VeriJsonAsync(iptal)).surum;
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new Saglik
            {
                Durum = "veri-yok",
                Hata  = ex.Message,
                Uyari = "API hic veri okuyamadi; veritabani baglantisini kontrol et."
            });
        }

        int yas = depo.VeriZamani == DateTime.MinValue
            ? -1
            : (int)Math.Round((DateTime.Now - depo.VeriZamani).TotalMinutes);

        bool bayat = depo.SonHata is not null;

        var s = new Saglik
        {
            Durum            = bayat ? "veritabani-yok" : (yas > bayatDakika ? "bayat" : "calisiyor"),
            Surum            = surum,
            Guncelleme       = depo.VeriZamani,
            VeriYasiDakika   = yas,
            SonBasariliOkuma = depo.SonBasariliOkuma,
            Bayat            = bayat,
            Hata             = depo.SonHata,
            SecimSayisi      = v.Secimler.Count,
            IlSayisi         = v.Iller.Count
        };

        if (bayat)
            s.Uyari = "Veritabanina ulasilamiyor, veri onbellekten servis ediliyor.";
        else if (yas > bayatDakika)
            s.Uyari = $"Veri {yas} dakikadir degismedi; besleme durmus olabilir.";

        return s;
    }

    /// <summary> Verinin surum numarasi ve son degisiklik zamani. </summary>
    [HttpGet("surum")]
    [Produces("application/json")]
    public async Task<ActionResult<SurumBilgisi>> Surum(CancellationToken iptal)
    {
        // Onbellek uzerinden gidiyor: veritabani dususe bile son bilinen
        // surum donuyor, reji uygulamasi bosluga dusmuyor.
        await depo.VeriAsync(iptal);

        return new SurumBilgisi
        {
            Surum      = (await depo.VeriJsonAsync(iptal)).surum,
            Guncelleme = depo.VeriZamani,
            Aciklama   = depo.SonHata is null ? null : "onbellekten (veritabani yok)"
        };
    }

    /// <summary>
    /// Butun secim verisi tek pakette.
    ///
    /// Reji uygulamasi bunu kullaniyor: bir sahnede 20-30 deger var ve
    /// hepsinin ayni ana ait olmasi gerekiyor. Tek tek cekilirse aralarinda
    /// veri degisip ekranda yarisi eski bir tablo olusabilir.
    ///
    /// Cevap onbellekten hazir metin olarak donuyor. ETag da veriliyor:
    /// istemci If-None-Match gonderirse ve surum degismediyse 304 aliyor.
    /// </summary>
    [HttpGet("veri")]
    [Produces("application/json")]
    public async Task<IActionResult> Veri(CancellationToken iptal)
    {
        (int surum, string json) = await depo.VeriJsonAsync(iptal);

        string etag = "\"v" + surum + "\"";

        if (Request.Headers.IfNoneMatch.ToString() == etag)
            return StatusCode(StatusCodes.Status304NotModified);

        Response.Headers.ETag = etag;
        Response.Headers["X-Veri-Surum"] = surum.ToString();

        // Veritabanina ulasilamiyorsa istemci bunu bilsin.
        if (depo.SonHata is not null) Response.Headers["X-Veri-Bayat"] = "1";

        return Content(json, "application/json; charset=utf-8");
    }
}
