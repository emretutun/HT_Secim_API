using HT_Secim_API.Model;

namespace HT_Secim_API.Veri;

/// <summary>
/// Ham oy sayilarindan turetilmis degerleri uretir.
///
/// Veritabani yalnizca "kim kac oy aldi" bilgisini tutuyor. Oran, gecerli oy,
/// TURKIYE satiri ve milletvekili dagilimi burada hesaplaniyor. Boylece
/// "veri kendi icinde tutarsiz" diye bir durum olusamiyor:
///   - il oranlari her zaman tam 10000'e toplanir
///   - TURKIYE satiri her zaman illerin toplamidir
///   - il vekilleri her zaman ilin kotasini tutar
/// </summary>
public static class Hesap
{
    /// <summary> Yuzdeler tam sayi olarak x100 tutuluyor: 10000 = %100,00 </summary>
    public const int TAM = 10000;

    /// <summary> TURKIYE satirinin plakasi. </summary>
    public const int TURKIYE = 0;

    /// <summary>
    /// Yurtdisi (900) ve gumruk (901) satirlari ekranda gosteriliyor ama
    /// TURKIYE toplamina katilmiyor - veri seti bastan boyle kuruldu.
    /// </summary>
    public static bool UlkeToplaminaGirer(int plaka) => plaka >= 1 && plaka <= 81;

    /// <summary> Yuzde x100 olarak oran. Bolen sifirsa 0. </summary>
    public static int Yuzde(long pay, long payda)
    {
        if (payda <= 0) return 0;

        return (int)Math.Round((double)pay * TAM / payda, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Bir secimin butun satirlarini tamamlar: gecerli oy, oranlar,
    /// TURKIYE satiri ve (milletvekili secimiyse) vekil dagilimi.
    /// </summary>
    public static void Tamamla(Secim secim,
                               IReadOnlyDictionary<int, int> vekilKotasi,
                               IReadOnlyDictionary<string, string> partininIttifaki)
    {
        foreach (IlSonucu satir in secim.Sonuclar)
        {
            satir.GecerliOy = satir.Oylar.Sum(o => o.OySayisi);

            // Veritabani adet tutuyor; ekranda gosterilen yuzdeler burada uretiliyor.
            satir.AcilanSandik = Yuzde(satir.AcilanSandikAdet, satir.ToplamSandik);
            satir.Katilim      = Yuzde(satir.GecerliOy + satir.GecersizOy, satir.SecmenSayisi);

            OranlariYaz(satir);
        }

        IlSonucu ulke = UlkeSatiri(secim);
        secim.Sonuclar.Insert(0, ulke);

        if (secim.Tip == "MV") VekilleriDagit(secim, ulke, vekilKotasi, partininIttifaki);
    }

    /// <summary>
    /// Oranlari yazar ve toplamin tam 10000 olmasini garanti eder.
    ///
    /// Her kalemi ayri ayri yuvarlarsak toplam 9999 ya da 10001 cikabiliyor.
    /// "En buyuk kalan" yontemi: once asagi yuvarlanir, artan birimler kesirli
    /// kismi en buyuk olanlara dagitilir. Esitlikte kod'a gore siralanir ki
    /// ayni veri her zaman ayni sonucu versin.
    /// </summary>
    private static void OranlariYaz(IlSonucu satir)
    {
        if (satir.GecerliOy <= 0)
        {
            foreach (Oy o in satir.Oylar) o.Oran = 0;
            return;
        }

        var kesirler = new List<(Oy oy, double kesir)>(satir.Oylar.Count);
        int toplam = 0;

        foreach (Oy o in satir.Oylar)
        {
            double tam = (double)o.OySayisi * TAM / satir.GecerliOy;
            int taban = (int)Math.Floor(tam);

            o.Oran = taban;
            toplam += taban;

            kesirler.Add((o, tam - taban));
        }

        int artan = TAM - toplam;
        if (artan <= 0) return;

        foreach (var (oy, _) in kesirler
                     .OrderByDescending(x => x.kesir)
                     .ThenBy(x => x.oy.Kod, StringComparer.Ordinal)
                     .Take(artan))
        {
            oy.Oran++;
        }
    }

    /// <summary>
    /// TURKIYE satiri: 81 ilin oy toplami. Acilan sandik ve katilim oranlari
    /// ise oy agirlikli ortalama - kucuk bir ilin %100'u ile Istanbul'un
    /// %60'i esit agirlikta olmamali.
    /// </summary>
    private static IlSonucu UlkeSatiri(Secim secim)
    {
        var ulke = new IlSonucu { Plaka = TURKIYE };

        var toplamOy = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (IlSonucu satir in secim.Sonuclar)
        {
            if (!UlkeToplaminaGirer(satir.Plaka)) continue;

            // Ulke satirinin sandik ve katilim oranlari da adetten hesaplaniyor,
            // illerin yuzdelerinin ortalamasi alinmiyor: kucuk bir ilin %100'u
            // ile Istanbul'un %60'i esit agirlikta olmamali.
            ulke.ToplamSandik     += satir.ToplamSandik;
            ulke.AcilanSandikAdet += satir.AcilanSandikAdet;
            ulke.SecmenSayisi     += satir.SecmenSayisi;
            ulke.GecersizOy       += satir.GecersizOy;

            if (satir.Guncelleme > ulke.Guncelleme) ulke.Guncelleme = satir.Guncelleme;

            foreach (Oy o in satir.Oylar)
            {
                toplamOy.TryGetValue(o.Kod, out long onceki);
                toplamOy[o.Kod] = onceki + o.OySayisi;
            }
        }

        // Kalemler oyu cok olandan aza dogru; il satirlari da boyle siralaniyor.
        foreach (string kod in toplamOy.Keys
                     .OrderByDescending(k => toplamOy[k])
                     .ThenBy(k => k, StringComparer.Ordinal))
        {
            ulke.Oylar.Add(new Oy { Kod = kod, OySayisi = toplamOy[kod] });
        }

        ulke.GecerliOy    = ulke.Oylar.Sum(o => o.OySayisi);
        ulke.AcilanSandik = Yuzde(ulke.AcilanSandikAdet, ulke.ToplamSandik);
        ulke.Katilim      = Yuzde(ulke.GecerliOy + ulke.GecersizOy, ulke.SecmenSayisi);

        OranlariYaz(ulke);
        return ulke;
    }

    /// <summary>
    /// Milletvekili dagitimi: baraji gecen kalemler arasinda, her ilde o ilin
    /// kotasi kadar sandalye D'Hondt ile paylastirilir.
    ///
    /// Baraj ULKE GENELINDEKI orana bakar ve secime gore degisir:
    ///   2023 / 2018 -> ittifak toplami %7'yi gecmeli
    ///   2015        -> partinin kendisi %10'u gecmeli (o yil ittifak yoktu)
    /// Kural veritabanindaki Secim satirindan geliyor, kodda sabit degil.
    /// </summary>
    private static void VekilleriDagit(Secim secim, IlSonucu ulke,
                                       IReadOnlyDictionary<int, int> vekilKotasi,
                                       IReadOnlyDictionary<string, string> partininIttifaki)
    {
        HashSet<string> girenler = BarajiGecenler(ulke, secim, partininIttifaki);

        var ulkeVekil = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (IlSonucu satir in secim.Sonuclar)
        {
            if (!UlkeToplaminaGirer(satir.Plaka)) continue;
            if (!vekilKotasi.TryGetValue(satir.Plaka, out int kota) || kota <= 0) continue;

            DHondt(satir, kota, girenler);

            foreach (Oy o in satir.Oylar)
            {
                ulkeVekil.TryGetValue(o.Kod, out int onceki);
                ulkeVekil[o.Kod] = onceki + o.Vekil;
            }
        }

        foreach (Oy o in ulke.Oylar)
            o.Vekil = ulkeVekil.TryGetValue(o.Kod, out int v) ? v : 0;
    }

    private static HashSet<string> BarajiGecenler(IlSonucu ulke, Secim secim,
                                                  IReadOnlyDictionary<string, string> partininIttifaki)
    {
        var girenler = new HashSet<string>(StringComparer.Ordinal);

        // Baraj tanimli degilse herkes yarisir.
        if (secim.BarajOran <= 0)
        {
            foreach (Oy o in ulke.Oylar) girenler.Add(o.Kod);
            return girenler;
        }

        bool ittifakUzerinden = secim.BarajTipi == "ITTIFAK";

        // Ittifak barajinda olcut, ittifakin butun partilerinin toplam orani.
        var ittifakOrani = new Dictionary<string, int>(StringComparer.Ordinal);

        if (ittifakUzerinden)
        {
            foreach (Oy o in ulke.Oylar)
            {
                if (!partininIttifaki.TryGetValue(o.Kod, out string? it) || string.IsNullOrEmpty(it)) continue;

                ittifakOrani.TryGetValue(it, out int onceki);
                ittifakOrani[it] = onceki + o.Oran;
            }
        }

        foreach (Oy o in ulke.Oylar)
        {
            int olcut = o.Oran;

            if (ittifakUzerinden &&
                partininIttifaki.TryGetValue(o.Kod, out string? it) &&
                !string.IsNullOrEmpty(it))
            {
                olcut = ittifakOrani[it];
            }

            if (olcut >= secim.BarajOran) girenler.Add(o.Kod);
        }

        return girenler;
    }

    /// <summary>
    /// Klasik D'Hondt: her turda "oy / (kazanilan + 1)" degeri en yuksek olan
    /// kalem bir sandalye alir. Esitlikte oyu, o da esitse kodu belirler -
    /// ayni veri her zaman ayni meclisi versin diye.
    /// </summary>
    private static void DHondt(IlSonucu satir, int sandalye, HashSet<string> girenler)
    {
        foreach (Oy o in satir.Oylar) o.Vekil = 0;

        List<Oy> yarisanlar = satir.Oylar
            .Where(o => girenler.Contains(o.Kod) && o.OySayisi > 0)
            .ToList();

        if (yarisanlar.Count == 0) return;

        for (int s = 0; s < sandalye; s++)
        {
            Oy? enIyi = null;
            double enIyiDeger = -1;

            foreach (Oy o in yarisanlar)
            {
                double deger = (double)o.OySayisi / (o.Vekil + 1);

                if (deger > enIyiDeger || (deger == enIyiDeger && enIyi != null && Onde(o, enIyi)))
                {
                    enIyiDeger = deger;
                    enIyi = o;
                }
            }

            if (enIyi != null) enIyi.Vekil++;
        }
    }

    /// <summary> Esitlik bozucu: once cok oy alan, o da esitse kodu once gelen. </summary>
    private static bool Onde(Oy aday, Oy mevcut)
    {
        if (aday.OySayisi != mevcut.OySayisi) return aday.OySayisi > mevcut.OySayisi;

        return string.CompareOrdinal(aday.Kod, mevcut.Kod) < 0;
    }
}
