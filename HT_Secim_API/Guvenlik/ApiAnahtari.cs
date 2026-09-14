using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HT_Secim_API.Guvenlik;

/// <summary> Bir ucun hangi yetkiyi istedigi. </summary>
public enum Yetki
{
    /// <summary> Sonuclari okumak. Reji uygulamasi ve is ortaklari. </summary>
    Okuma = 0,

    /// <summary> Veri yazmak. Besleme servisi ve yonetim paneli. </summary>
    Yazma = 1
}

/// <summary>
/// Uclari "X-API-KEY" basligiyla korur.
///
/// Iki ayri anahtar var:
///   HtSecim:OkumaAnahtari  -> sonuclari okuyanlar (reji, ortaklar)
///   HtSecim:YazmaAnahtari  -> veriyi degistirenler (besleme, yonetim)
///
/// Ayri olmalarinin sebebi: okuma anahtari bircok makineye dagitiliyor ve
/// er gec sizar. Sizan bir anahtarla secim verisinin degistirilebilmesi
/// kabul edilemez. Yazma anahtari yalnizca besleme servisinde durur.
///
/// Yazma anahtari okuma yetkisini de kapsar.
/// </summary>
public sealed class ApiAnahtariFiltresi : IAsyncActionFilter
{
    public const string BASLIK = "X-API-KEY";

    private readonly string okuma;
    private readonly string yazma;
    private readonly ILogger<ApiAnahtariFiltresi> log;

    public ApiAnahtariFiltresi(IConfiguration yapilandirma, ILogger<ApiAnahtariFiltresi> log)
    {
        this.log = log;

        okuma = yapilandirma["HtSecim:OkumaAnahtari"] ?? "";
        yazma = yapilandirma["HtSecim:YazmaAnahtari"] ?? "";

        if (string.IsNullOrEmpty(okuma))
            log.LogWarning("HtSecim:OkumaAnahtari bos - okuma uclari anahtarsiz acik.");

        if (string.IsNullOrEmpty(yazma))
            log.LogWarning("HtSecim:YazmaAnahtari bos - YAZMA UCLARI ANAHTARSIZ ACIK.");
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext baglam, ActionExecutionDelegate sonraki)
    {
        Yetki gereken = Gereken(baglam);

        string gelen = baglam.HttpContext.Request.Headers[BASLIK].ToString();

        if (Gecerli(gereken, gelen))
        {
            await sonraki();
            return;
        }

        log.LogWarning("Yetkisiz istek: {Yol} ({Ip}) - {Yetki} gerekiyordu",
            baglam.HttpContext.Request.Path,
            baglam.HttpContext.Connection.RemoteIpAddress,
            gereken);

        baglam.Result = new UnauthorizedObjectResult(new
        {
            hata = "Gecersiz veya eksik " + BASLIK,
            gerekenYetki = gereken.ToString()
        });
    }

    private bool Gecerli(Yetki gereken, string gelen)
    {
        // Yazma anahtari her seye yeter.
        if (!string.IsNullOrEmpty(yazma) && string.Equals(gelen, yazma, StringComparison.Ordinal))
            return true;

        if (gereken == Yetki.Yazma)
        {
            // Anahtar tanimli degilse kontrol yapilamiyor; gelistirme icin acik.
            return string.IsNullOrEmpty(yazma);
        }

        if (string.IsNullOrEmpty(okuma)) return true;

        return string.Equals(gelen, okuma, StringComparison.Ordinal);
    }

    /// <summary> Ucun uzerindeki YetkiGerekir niteligi; yoksa okuma. </summary>
    private static Yetki Gereken(ActionExecutingContext baglam)
    {
        var nitelik = baglam.ActionDescriptor.EndpointMetadata
            .OfType<YetkiGerekirAttribute>()
            .LastOrDefault();

        return nitelik?.Yetki ?? Yetki.Okuma;
    }
}

/// <summary> Bir ucun yazma yetkisi istedigini belirtir. </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class YetkiGerekirAttribute : Attribute
{
    public Yetki Yetki { get; }

    public YetkiGerekirAttribute(Yetki yetki) => Yetki = yetki;
}
