using HT_Secim_API.Guvenlik;
using HT_Secim_API.Model;
using HT_Secim_API.Veri;
using Microsoft.AspNetCore.Mvc;

namespace HT_Secim_API.Controllers;

/// <summary>
/// Besleme servisinin kullandigi uclar. YAZMA anahtari istiyor.
///
/// Veriyi degistiren her sey tek kapidan geciyor: dogrulama, gunluk ve
/// atomik uygulama hep burada. Besleme servisinin veritabani semasini
/// bilmesine gerek yok - ajans bicimi degistiginde orada yalnizca
/// uyarlayici degisiyor.
/// </summary>
[ApiController]
[Route("api/v1/besleme")]
[Produces("application/json")]
[YetkiGerekir(Yetki.Yazma)]
public sealed class BeslemeController : ControllerBase
{
    private readonly BeslemeDeposu depo;
    private readonly ILogger<BeslemeController> log;

    public BeslemeController(BeslemeDeposu depo, ILogger<BeslemeController> log)
    {
        this.depo = depo;
        this.log = log;
    }

    /// <summary>
    /// Tam anlik goruntuyu uygular.
    ///
    /// Reddedilen istek de 200 donuyor, 400 degil: red bir istemci hatasi
    /// degil, normal bir is sonucu. Besleme servisi cevabin "kabul"
    /// alanina bakip dosyayi arsive mi hataliya mi koyacagina karar
    /// veriyor; HTTP hata kodu olsaydi ayni durumu hem istisna hem sonuc
    /// olarak islemek gerekirdi.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<BeslemeCevabi>> Uygula(
        [FromBody] BeslemeIstegi istek, CancellationToken iptal)
    {
        if (!ModelState.IsValid)
            return BadRequest(new BeslemeCevabi
            {
                Kabul = false,
                Aciklama = "Istek bicimi gecersiz."
            });

        BeslemeCevabi cevap = await depo.UygulaAsync(istek, iptal);

        if (cevap.Kabul)
            log.LogInformation("Besleme kabul: {Kaynak} #{Sira} - {Il} il, surum {Surum}",
                istek.Kaynak, istek.Sira, cevap.IlSayisi, cevap.Surum);
        else
            log.LogWarning("Besleme RED: {Kaynak} #{Sira} - {Sebep}",
                istek.Kaynak, istek.Sira, cevap.Aciklama);

        return cevap;
    }

    /// <summary>
    /// Secimlerin sayaclarini sifirlar. GECE BASLAMADAN ONCE calistirilir.
    ///
    /// Acilan sandik ve butun oylar sifirlanir; toplam sandik ve secmen
    /// sayisi kalir - onlar geceden once bilinen degerler.
    ///
    /// Kazara calistirilmasin diye istegin icinde tam olarak "SIFIRLA"
    /// yazmali. Bu uc butun bir secimin sayimini siliyor; yanlislikla
    /// atilan bir istekle yayindaki sonuclarin gitmesi kabul edilemez.
    /// </summary>
    [HttpPost("sifirla")]
    public async Task<ActionResult<BeslemeCevabi>> Sifirla(
        [FromBody] SifirlamaIstegi istek, CancellationToken iptal)
    {
        if (!string.Equals(istek.Onay, "SIFIRLA", StringComparison.Ordinal))
            return BadRequest(new BeslemeCevabi
            {
                Kabul = false,
                Aciklama = "Onay alani tam olarak \"SIFIRLA\" olmali."
            });

        List<string> secimler = istek.Secimler
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (secimler.Count == 0)
            return BadRequest(new BeslemeCevabi
            {
                Kabul = false,
                Aciklama = "Sifirlanacak secim belirtilmedi."
            });

        log.LogWarning("Sifirlama istegi: {Secimler} ({Ip})",
            string.Join(", ", secimler), HttpContext.Connection.RemoteIpAddress);

        return await depo.SifirlaAsync(secimler, iptal);
    }

    /// <summary>
    /// Son besleme denemeleri: ne zaman geldi, kabul mu red mi, neden.
    ///
    /// Yayin gecesi "ajans veri gonderiyor ama ekranda degismiyor"
    /// durumunun tek aciklamasi burasi.
    /// </summary>
    [HttpGet("gecmis")]
    [YetkiGerekir(Yetki.Okuma)]
    public async Task<ActionResult<List<BeslemeGecmisSatiri>>> Gecmis(
        [FromQuery] int adet = 20, CancellationToken iptal = default)
    {
        return await depo.GecmisAsync(Math.Clamp(adet, 1, 200), iptal);
    }
}
