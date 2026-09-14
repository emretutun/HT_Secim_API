using HT_Secim_API.Model;
using HT_Secim_API.Veri;
using Microsoft.AspNetCore.Mvc;

namespace HT_Secim_API.Controllers;

/// <summary>
/// Secim sonuclari. Hepsi bellekteki onbellekten servis ediliyor; bir istek
/// veritabanina yalnizca surum numarasi degistiginde gidiyor.
///
/// Ornek:  GET /api/v1/secimler/MV_2023/sonuclar/ankara/CHP
/// </summary>
[ApiController]
[Route("api/v1/secimler/{secim}")]
[Produces("application/json")]
public sealed class SonuclarController : ControllerBase
{
    private readonly VeriDeposu depo;

    public SonuclarController(VeriDeposu depo) => this.depo = depo;

    // ============================================================ sonuclar

    /// <summary> Butun illerin ozeti (kalem listesi olmadan). </summary>
    [HttpGet("sonuclar")]
    public async Task<ActionResult<object>> Hepsi(string secim, CancellationToken iptal)
    {
        var (v, s, hata) = await Coz(secim, iptal);
        if (hata is not null) return hata;

        return s!.Sonuclar.Select(satir => new
        {
            satir.Plaka,
            il = IlAdi(v!, satir.Plaka),
            satir.GecerliOy,
            satir.AcilanSandik,
            satir.Katilim,
            birinci = Birinci(satir)?.Kod
        }).ToList();
    }

    /// <summary> Bir ilin tam tablosu: sandik, katilim ve butun kalemler. </summary>
    [HttpGet("sonuclar/{il}")]
    public async Task<ActionResult<IlOzeti>> IlSonuclari(string secim, string il, CancellationToken iptal)
    {
        var (v, s, hata) = await Coz(secim, iptal);
        if (hata is not null) return hata;

        var (satir, il2, hata2) = IlCoz(v!, s!, il);
        if (hata2 is not null) return hata2;

        var ozet = new IlOzeti
        {
            Secim            = s!.Kod,
            SecimAdi         = s.Ad,
            Plaka            = satir!.Plaka,
            Il               = il2!.Ad,
            Bolge            = il2.Bolge,
            VekilKotasi      = il2.VekilKotasi,
            ToplamSandik     = satir.ToplamSandik,
            AcilanSandikAdet = satir.AcilanSandikAdet,
            AcilanSandik     = satir.AcilanSandik,
            AcilanSandikYazi = Arama.Yuzde(satir.AcilanSandik),
            SecmenSayisi     = satir.SecmenSayisi,
            GecerliOy        = satir.GecerliOy,
            GecersizOy       = satir.GecersizOy,
            Katilim          = satir.Katilim,
            KatilimYazi      = Arama.Yuzde(satir.Katilim),
            Guncelleme       = satir.Guncelleme
        };

        ozet.Sonuclar = Siralanmis(v!, s, satir);
        return ozet;
    }

    /// <summary>
    /// Tek kalem: bir ilde bir partinin / adayin / secenegin sonucu.
    /// Ornek: /api/v1/secimler/MV_2023/sonuclar/06/CHP
    /// </summary>
    [HttpGet("sonuclar/{il}/{kod}")]
    public async Task<ActionResult<KalemSonucu>> Kalem(string secim, string il, string kod, CancellationToken iptal)
    {
        var (v, s, hata) = await Coz(secim, iptal);
        if (hata is not null) return hata;

        var (satir, _, hata2) = IlCoz(v!, s!, il);
        if (hata2 is not null) return hata2;

        List<KalemSonucu> liste = Siralanmis(v!, s!, satir!);
        string aranan = Arama.Sadelestir(kod);

        KalemSonucu? bulunan = liste.FirstOrDefault(x => Arama.Sadelestir(x.Kod) == aranan);

        if (bulunan is null)
            return NotFound(new { hata = $"{s!.Kod} seciminde {kod} kalemi yok." });

        return bulunan;
    }

    // ============================================================ siralama

    /// <summary> Bir ildeki siralama, cok oydan aza. </summary>
    [HttpGet("siralama/{il}")]
    public async Task<ActionResult<List<KalemSonucu>>> Siralama(string secim, string il, CancellationToken iptal)
    {
        var (v, s, hata) = await Coz(secim, iptal);
        if (hata is not null) return hata;

        var (satir, _, hata2) = IlCoz(v!, s!, il);
        if (hata2 is not null) return hata2;

        return Siralanmis(v!, s!, satir!);
    }

    // ============================================================ kazanan

    /// <summary> Bir ili kim kazandi. </summary>
    [HttpGet("kazanan/{il}")]
    public async Task<ActionResult<Kazanan>> KazananTek(string secim, string il, CancellationToken iptal)
    {
        var (v, s, hata) = await Coz(secim, iptal);
        if (hata is not null) return hata;

        var (satir, il2, hata2) = IlCoz(v!, s!, il);
        if (hata2 is not null) return hata2;

        Kazanan? k = KazananUret(v!, s!, satir!, il2!.Ad);
        if (k is null) return NotFound(new { hata = "Bu ilde oy kaydi yok." });

        return k;
    }

    /// <summary> Butun illerin kazananlari. Harita sahneleri bunu kullaniyor. </summary>
    [HttpGet("kazananlar")]
    public async Task<ActionResult<List<Kazanan>>> Kazananlar(string secim, CancellationToken iptal)
    {
        var (v, s, hata) = await Coz(secim, iptal);
        if (hata is not null) return hata;

        var liste = new List<Kazanan>();

        foreach (IlSonucu satir in s!.Sonuclar)
        {
            Kazanan? k = KazananUret(v!, s, satir, IlAdi(v!, satir.Plaka));
            if (k is not null) liste.Add(k);
        }

        return liste;
    }

    // ========================================================== ittifaklar

    /// <summary> Bir ilde ittifak bazinda toplam oran ve vekil. </summary>
    [HttpGet("ittifaklar/{il}")]
    public async Task<ActionResult<List<IttifakSonucu>>> IttifakSonuclari(string secim, string il, CancellationToken iptal)
    {
        var (v, s, hata) = await Coz(secim, iptal);
        if (hata is not null) return hata;

        if (s!.Tip != "MV")
            return BadRequest(new { hata = "Ittifak dagilimi yalnizca milletvekili secimlerinde anlamli." });

        var (satir, _, hata2) = IlCoz(v!, s, il);
        if (hata2 is not null) return hata2;

        List<KalemSonucu> kalemler = Siralanmis(v!, s, satir!);

        var sonuc = new List<IttifakSonucu>();

        foreach (Ittifak it in v!.Ittifaklar)
        {
            string itKod = Arama.Sadelestir(it.Kod);

            List<KalemSonucu> uyeler = kalemler
                .Where(k => Arama.Sadelestir(PartiIttifaki(v, k.Kod)) == itKod)
                .ToList();

            if (uyeler.Count == 0) continue;

            int oran = uyeler.Sum(u => u.Oran);

            sonuc.Add(new IttifakSonucu
            {
                Kod      = it.Kod,
                Ad       = it.Ad,
                Oran     = oran,
                OranYazi = Arama.Yuzde(oran),
                Oy       = uyeler.Sum(u => u.Oy),
                Vekil    = uyeler.Sum(u => u.Vekil),
                Partiler = uyeler
            });
        }

        return sonuc.OrderByDescending(x => x.Oy).ToList();
    }

    // ============================================================= vekiller

    /// <summary> Ulke geneli milletvekili dagilimi. </summary>
    [HttpGet("vekiller")]
    public async Task<ActionResult<object>> Vekiller(string secim, CancellationToken iptal)
    {
        var (v, s, hata) = await Coz(secim, iptal);
        if (hata is not null) return hata;

        if (s!.Tip != "MV")
            return BadRequest(new { hata = "Bu secimde milletvekili dagitimi yok." });

        IlSonucu? ulke = Arama.SatirBul(s, Hesap.TURKIYE);
        if (ulke is null) return NotFound(new { hata = "Turkiye satiri yok." });

        List<KalemSonucu> liste = Siralanmis(v!, s, ulke)
            .Where(k => k.Vekil > 0)
            .OrderByDescending(k => k.Vekil)
            .ToList();

        return new { toplam = liste.Sum(k => k.Vekil), dagilim = liste };
    }

    // ============================================================== katilim

    /// <summary> Il il sandik ve katilim durumu. </summary>
    [HttpGet("katilim")]
    public async Task<ActionResult<List<KatilimSatiri>>> Katilim(string secim, CancellationToken iptal)
    {
        var (v, s, hata) = await Coz(secim, iptal);
        if (hata is not null) return hata;

        return s!.Sonuclar.Select(satir => KatilimUret(v!, satir)).ToList();
    }

    [HttpGet("katilim/{il}")]
    public async Task<ActionResult<KatilimSatiri>> KatilimTek(string secim, string il, CancellationToken iptal)
    {
        var (v, s, hata) = await Coz(secim, iptal);
        if (hata is not null) return hata;

        var (satir, _, hata2) = IlCoz(v!, s!, il);
        if (hata2 is not null) return hata2;

        return KatilimUret(v!, satir!);
    }

    // ============================================================= kalemler

    /// <summary> Bir kalemin butun illerdeki sonucu. </summary>
    [HttpGet("kalemler/{kod}")]
    public async Task<ActionResult<List<KalemSonucu>>> KalemIller(string secim, string kod, CancellationToken iptal)
    {
        var (v, s, hata) = await Coz(secim, iptal);
        if (hata is not null) return hata;

        return KalemTablosu(v!, s!, kod);
    }

    /// <summary> Bir kalemin en cok oy aldigi iller. </summary>
    [HttpGet("kalemler/{kod}/enler")]
    public async Task<ActionResult<List<KalemSonucu>>> KalemEnler(string secim, string kod,
        [FromQuery] int adet = 10, CancellationToken iptal = default)
    {
        var (v, s, hata) = await Coz(secim, iptal);
        if (hata is not null) return hata;

        if (adet < 1) adet = 1;
        if (adet > 81) adet = 81;

        return KalemTablosu(v!, s!, kod)
            .Where(k => Hesap.UlkeToplaminaGirer(k.Plaka))
            .OrderByDescending(k => k.Oran)
            .ThenBy(k => k.Plaka)
            .Take(adet)
            .ToList();
    }

    // ========================================================= karsilastir

    /// <summary>
    /// Ayni kalemin iki secimdeki farki.
    /// Ornek: /api/v1/secimler/MV_2023/karsilastir/06/AKP?ile=MV_2018
    /// </summary>
    [HttpGet("karsilastir/{il}/{kod}")]
    public async Task<ActionResult<Karsilastirma>> Karsilastir(string secim, string il, string kod,
        [FromQuery] string ile, CancellationToken iptal)
    {
        if (string.IsNullOrWhiteSpace(ile))
            return BadRequest(new { hata = "Karsilastirilacak secim 'ile' parametresiyle verilmeli." });

        var (v, sonra, hata) = await Coz(secim, iptal);
        if (hata is not null) return hata;

        Secim? once = Arama.SecimBul(v!, ile);
        if (once is null) return NotFound(new { hata = $"Secim bulunamadi: {ile}" });

        var (satirSonra, il2, hata2) = IlCoz(v!, sonra!, il);
        if (hata2 is not null) return hata2;

        IlSonucu? satirOnce = Arama.SatirBul(once, satirSonra!.Plaka);
        if (satirOnce is null) return NotFound(new { hata = $"{once.Kod} seciminde bu il yok." });

        string aranan = Arama.Sadelestir(kod);

        KalemSonucu? a = Siralanmis(v!, once, satirOnce).FirstOrDefault(x => Arama.Sadelestir(x.Kod) == aranan);
        KalemSonucu? b = Siralanmis(v!, sonra!, satirSonra).FirstOrDefault(x => Arama.Sadelestir(x.Kod) == aranan);

        int fark = (b?.Oran ?? 0) - (a?.Oran ?? 0);

        return new Karsilastirma
        {
            Plaka     = satirSonra.Plaka,
            Il        = il2!.Ad,
            Kod       = kod,
            Ad        = Arama.KalemAdi(v!, sonra!, kod),
            Once      = a,
            Sonra     = b,
            Fark      = fark,
            FarkYazi  = (fark > 0 ? "+" : "") + Arama.Yuzde(fark),
            Yon       = fark > 0 ? "ARTAN" : fark < 0 ? "AZALAN" : "SABIT",
            VekilFark = (b?.Vekil ?? 0) - (a?.Vekil ?? 0)
        };
    }

    // =============================================================== ortak

    /// <summary> Secimi cozer; bulamazsa hazir 404 dondurur. </summary>
    private async Task<(SecimVerisi? veri, Secim? secim, ActionResult? hata)> Coz(
        string secimKod, CancellationToken iptal)
    {
        SecimVerisi v = await depo.VeriAsync(iptal);
        Secim? s = Arama.SecimBul(v, secimKod);

        if (s is null)
        {
            string liste = string.Join(", ", v.Secimler.Select(x => x.Kod));
            return (null, null, NotFound(new { hata = $"Secim bulunamadi: {secimKod}", secimler = liste }));
        }

        return (v, s, null);
    }

    private static (IlSonucu? satir, Il? il, ActionResult? hata) IlCoz(SecimVerisi v, Secim s, string il)
    {
        Il? bulunan = Arama.IlBul(v, il);
        if (bulunan is null)
            return (null, null, new NotFoundObjectResult(new { hata = $"Il bulunamadi: {il}" }));

        IlSonucu? satir = Arama.SatirBul(s, bulunan.Plaka);
        if (satir is null)
            return (null, null, new NotFoundObjectResult(
                new { hata = $"{s.Kod} seciminde {bulunan.Ad} icin sonuc yok." }));

        return (satir, bulunan, null);
    }

    private static string IlAdi(SecimVerisi v, int plaka)
    {
        return v.Iller.FirstOrDefault(i => i.Plaka == plaka)?.Ad ?? plaka.ToString();
    }

    private static Oy? Birinci(IlSonucu satir)
    {
        return satir.Oylar.OrderByDescending(o => o.OySayisi).FirstOrDefault();
    }

    private static string? PartiIttifaki(SecimVerisi v, string? partiKod)
    {
        string aranan = Arama.Sadelestir(partiKod);
        return v.Partiler.FirstOrDefault(p => Arama.Sadelestir(p.Kod) == aranan)?.Ittifak;
    }

    /// <summary> Bir satirin kalemlerini siralayip cevap tipine cevirir. </summary>
    private static List<KalemSonucu> Siralanmis(SecimVerisi v, Secim s, IlSonucu satir)
    {
        var liste = new List<KalemSonucu>(satir.Oylar.Count);
        int sira = 0;

        foreach (Oy o in satir.Oylar.OrderByDescending(x => x.OySayisi).ThenBy(x => x.Kod, StringComparer.Ordinal))
        {
            sira++;

            liste.Add(new KalemSonucu
            {
                Secim    = s.Kod,
                SecimAdi = s.Ad,
                Plaka    = satir.Plaka,
                Il       = IlAdi(v, satir.Plaka),
                Kod      = o.Kod,
                Ad       = Arama.KalemAdi(v, s, o.Kod),
                Oran     = o.Oran,
                OranYazi = Arama.Yuzde(o.Oran),
                Oy       = o.OySayisi,
                Vekil    = o.Vekil,
                Sira     = sira
            });
        }

        return liste;
    }

    private static Kazanan? KazananUret(SecimVerisi v, Secim s, IlSonucu satir, string? ilAdi)
    {
        List<Oy> sirali = satir.Oylar
            .OrderByDescending(o => o.OySayisi)
            .ThenBy(o => o.Kod, StringComparer.Ordinal)
            .ToList();

        if (sirali.Count == 0) return null;

        Oy birinci = sirali[0];
        int ikinciOran = sirali.Count > 1 ? sirali[1].Oran : 0;

        return new Kazanan
        {
            Plaka    = satir.Plaka,
            Il       = ilAdi,
            Kod      = birinci.Kod,
            Ad       = Arama.KalemAdi(v, s, birinci.Kod),
            Oran     = birinci.Oran,
            OranYazi = Arama.Yuzde(birinci.Oran),
            Oy       = birinci.OySayisi,
            Fark     = birinci.Oran - ikinciOran
        };
    }

    private static KatilimSatiri KatilimUret(SecimVerisi v, IlSonucu satir)
    {
        return new KatilimSatiri
        {
            Plaka            = satir.Plaka,
            Il               = IlAdi(v, satir.Plaka),
            ToplamSandik     = satir.ToplamSandik,
            AcilanSandikAdet = satir.AcilanSandikAdet,
            AcilanSandik     = satir.AcilanSandik,
            AcilanSandikYazi = Arama.Yuzde(satir.AcilanSandik),
            SecmenSayisi     = satir.SecmenSayisi,
            GecerliOy        = satir.GecerliOy,
            GecersizOy       = satir.GecersizOy,
            Katilim          = satir.Katilim,
            KatilimYazi      = Arama.Yuzde(satir.Katilim),
            Guncelleme       = satir.Guncelleme
        };
    }

    private static List<KalemSonucu> KalemTablosu(SecimVerisi v, Secim s, string kod)
    {
        string aranan = Arama.Sadelestir(kod);
        var liste = new List<KalemSonucu>();

        foreach (IlSonucu satir in s.Sonuclar)
        {
            KalemSonucu? k = Siralanmis(v, s, satir).FirstOrDefault(x => Arama.Sadelestir(x.Kod) == aranan);
            if (k is not null) liste.Add(k);
        }

        return liste;
    }
}
