using System.Globalization;
using HT_Secim_API.Model;

namespace HT_Secim_API.Veri;

/// <summary>
/// URL'den gelen metinleri veri icindeki kayitlara baglar.
///
/// Il hem plakayla hem adla gelebiliyor: 6, 06, ankara, ANKARA, Ankara.
/// Turkce'de buyuk/kucuk harf cevrimi tuzakli - "ISTANBUL".ToLower() Turkce
/// kulturde "ıstanbul" verir, Ingilizce kulturde "istanbul". Bu yuzden
/// karsilastirmadan once harfler sadelestiriliyor (i/ı/İ/I hepsi ayni kabul).
/// </summary>
public static class Arama
{
    private static readonly CultureInfo TR = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>
    /// Karsilastirma icin sadelestirir: buyuk harfe cevirir, Turkce'ye ozgu
    /// harfleri Latin karsiliklariyla degistirir, bosluk ve tireleri atar.
    /// "İstanbul" ve "ISTANBUL" ve "istanbul" -> "ISTANBUL"
    /// </summary>
    public static string Sadelestir(string? metin)
    {
        if (string.IsNullOrWhiteSpace(metin)) return "";

        string buyuk = metin.Trim().ToUpper(TR);
        var sb = new System.Text.StringBuilder(buyuk.Length);

        foreach (char h in buyuk)
        {
            char c = h switch
            {
                'İ' or 'I' or 'Ι' => 'I',
                'Ş' => 'S',
                'Ğ' => 'G',
                'Ü' => 'U',
                'Ö' => 'O',
                'Ç' => 'C',
                _ => h
            };

            if (c is ' ' or '-' or '_' or '.') continue;

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary> Plaka ya da ad ile il bulur. Bulamazsa null. </summary>
    public static Il? IlBul(SecimVerisi veri, string? anahtar)
    {
        if (string.IsNullOrWhiteSpace(anahtar)) return null;

        if (int.TryParse(anahtar.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int plaka))
            return veri.Iller.FirstOrDefault(i => i.Plaka == plaka);

        string aranan = Sadelestir(anahtar);

        return veri.Iller.FirstOrDefault(i => Sadelestir(i.Ad) == aranan);
    }

    /// <summary> Kod ile secim bulur. "mv_2023" de kabul edilir. </summary>
    public static Secim? SecimBul(SecimVerisi veri, string? kod)
    {
        string aranan = Sadelestir(kod);

        return veri.Secimler.FirstOrDefault(s => Sadelestir(s.Kod) == aranan);
    }

    /// <summary> Bir secimde bir ilin satiri. </summary>
    public static IlSonucu? SatirBul(Secim secim, int plaka)
    {
        return secim.Sonuclar.FirstOrDefault(s => s.Plaka == plaka);
    }

    /// <summary> Bir satirdaki kalem (parti / aday / secenek). </summary>
    public static Oy? OyBul(IlSonucu satir, string? kod)
    {
        string aranan = Sadelestir(kod);

        return satir.Oylar.FirstOrDefault(o => Sadelestir(o.Kod) == aranan);
    }

    /// <summary>
    /// Bir kalemin ekranda gosterilecek adi. Secimin tipine gore parti,
    /// aday ya da referandum secenegi listesinden bulunuyor.
    /// </summary>
    public static string KalemAdi(SecimVerisi veri, Secim secim, string kod)
    {
        string aranan = Sadelestir(kod);

        if (secim.Tip == "CB")
        {
            Aday? a = veri.Adaylar.FirstOrDefault(x => Sadelestir(x.Kod) == aranan);
            if (a is not null) return a.Ad ?? kod;
        }
        else if (secim.Tip == "REF")
        {
            Secenek? s = veri.Secenekler.FirstOrDefault(x => Sadelestir(x.Kod) == aranan);
            if (s is not null) return s.Ad ?? kod;
        }
        else
        {
            Parti? p = veri.Partiler.FirstOrDefault(x => Sadelestir(x.Kod) == aranan);
            if (p is not null) return p.Ad ?? kod;
        }

        return kod;
    }

    /// <summary> Yuzde x100 degerini "41,92" olarak yazar. </summary>
    public static string Yuzde(int oranX100)
    {
        return (oranX100 / 100.0).ToString("F2", TR);
    }
}
