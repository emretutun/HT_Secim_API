using System.Text.Json.Serialization;

namespace HT_Secim_API.Model;

/* Ayrintili uclarin donus tipleri.
   Veri modelinden ayri tutuluyorlar: bir uca alan eklemek, reji
   uygulamasinin okudugu toplu JSON'u degistirmesin. */

/// <summary> Tek bir kalemin tek bir ildeki sonucu. </summary>
public sealed class KalemSonucu
{
    [JsonPropertyName("secim")]    public string? Secim { get; set; }
    [JsonPropertyName("secimAdi")] public string? SecimAdi { get; set; }
    [JsonPropertyName("plaka")]    public int Plaka { get; set; }
    [JsonPropertyName("il")]       public string? Il { get; set; }

    [JsonPropertyName("kod")]      public string? Kod { get; set; }
    [JsonPropertyName("ad")]       public string? Ad { get; set; }

    /// <summary> Yuzde x100: 4192 = %41,92 </summary>
    [JsonPropertyName("oran")]     public int Oran { get; set; }

    /// <summary> Ekranda gosterilecek hali: "41,92" </summary>
    [JsonPropertyName("oranYazi")] public string? OranYazi { get; set; }

    [JsonPropertyName("oy")]       public long Oy { get; set; }

    /// <summary> Yalnizca milletvekili secimlerinde anlamli. </summary>
    [JsonPropertyName("vekil")]    public int Vekil { get; set; }

    /// <summary> Ildeki sirasi: 1 = birinci. </summary>
    [JsonPropertyName("sira")]     public int Sira { get; set; }
}

/// <summary> Bir ilin bir secimdeki tam tablosu. </summary>
public sealed class IlOzeti
{
    [JsonPropertyName("secim")]        public string? Secim { get; set; }
    [JsonPropertyName("secimAdi")]     public string? SecimAdi { get; set; }
    [JsonPropertyName("plaka")]        public int Plaka { get; set; }
    [JsonPropertyName("il")]           public string? Il { get; set; }
    [JsonPropertyName("bolge")]        public string? Bolge { get; set; }
    [JsonPropertyName("vekilKotasi")]  public int VekilKotasi { get; set; }

    [JsonPropertyName("toplamSandik")]     public int ToplamSandik { get; set; }
    [JsonPropertyName("acilanSandikAdet")] public int AcilanSandikAdet { get; set; }
    [JsonPropertyName("acilanSandik")]     public int AcilanSandik { get; set; }
    [JsonPropertyName("acilanSandikYazi")] public string? AcilanSandikYazi { get; set; }

    [JsonPropertyName("secmenSayisi")] public long SecmenSayisi { get; set; }
    [JsonPropertyName("gecerliOy")]    public long GecerliOy { get; set; }
    [JsonPropertyName("gecersizOy")]   public long GecersizOy { get; set; }
    [JsonPropertyName("katilim")]      public int Katilim { get; set; }
    [JsonPropertyName("katilimYazi")]  public string? KatilimYazi { get; set; }

    [JsonPropertyName("guncelleme")]   public DateTime Guncelleme { get; set; }

    [JsonPropertyName("sonuclar")]     public List<KalemSonucu> Sonuclar { get; set; } = new();
}

/// <summary> Bir ittifakin bir ildeki toplami. </summary>
public sealed class IttifakSonucu
{
    [JsonPropertyName("kod")]      public string? Kod { get; set; }
    [JsonPropertyName("ad")]       public string? Ad { get; set; }
    [JsonPropertyName("oran")]     public int Oran { get; set; }
    [JsonPropertyName("oranYazi")] public string? OranYazi { get; set; }
    [JsonPropertyName("oy")]       public long Oy { get; set; }
    [JsonPropertyName("vekil")]    public int Vekil { get; set; }

    /// <summary> Ittifaki olusturan partiler. </summary>
    [JsonPropertyName("partiler")] public List<KalemSonucu> Partiler { get; set; } = new();
}

/// <summary> Bir ili kimin kazandigi. Harita sahneleri bunu kullaniyor. </summary>
public sealed class Kazanan
{
    [JsonPropertyName("plaka")]    public int Plaka { get; set; }
    [JsonPropertyName("il")]       public string? Il { get; set; }
    [JsonPropertyName("kod")]      public string? Kod { get; set; }
    [JsonPropertyName("ad")]       public string? Ad { get; set; }
    [JsonPropertyName("oran")]     public int Oran { get; set; }
    [JsonPropertyName("oranYazi")] public string? OranYazi { get; set; }
    [JsonPropertyName("oy")]       public long Oy { get; set; }

    /// <summary> Ikinciyle arasindaki fark, yuzde x100. </summary>
    [JsonPropertyName("fark")]     public int Fark { get; set; }
}

/// <summary> Bir ilin sandik ve katilim durumu. </summary>
public sealed class KatilimSatiri
{
    [JsonPropertyName("plaka")]            public int Plaka { get; set; }
    [JsonPropertyName("il")]               public string? Il { get; set; }
    [JsonPropertyName("toplamSandik")]     public int ToplamSandik { get; set; }
    [JsonPropertyName("acilanSandikAdet")] public int AcilanSandikAdet { get; set; }
    [JsonPropertyName("acilanSandik")]     public int AcilanSandik { get; set; }
    [JsonPropertyName("acilanSandikYazi")] public string? AcilanSandikYazi { get; set; }
    [JsonPropertyName("secmenSayisi")]     public long SecmenSayisi { get; set; }
    [JsonPropertyName("gecerliOy")]        public long GecerliOy { get; set; }
    [JsonPropertyName("gecersizOy")]       public long GecersizOy { get; set; }
    [JsonPropertyName("katilim")]          public int Katilim { get; set; }
    [JsonPropertyName("katilimYazi")]      public string? KatilimYazi { get; set; }
    [JsonPropertyName("guncelleme")]       public DateTime Guncelleme { get; set; }
}

/// <summary> Iki secim arasindaki fark. </summary>
public sealed class Karsilastirma
{
    [JsonPropertyName("plaka")]     public int Plaka { get; set; }
    [JsonPropertyName("il")]        public string? Il { get; set; }
    [JsonPropertyName("kod")]       public string? Kod { get; set; }
    [JsonPropertyName("ad")]        public string? Ad { get; set; }

    [JsonPropertyName("once")]      public KalemSonucu? Once { get; set; }
    [JsonPropertyName("sonra")]     public KalemSonucu? Sonra { get; set; }

    /// <summary> sonra - once, yuzde x100. Eksi ise oy kaybetmis. </summary>
    [JsonPropertyName("fark")]      public int Fark { get; set; }
    [JsonPropertyName("farkYazi")]  public string? FarkYazi { get; set; }
    [JsonPropertyName("yon")]       public string? Yon { get; set; }   // ARTAN / AZALAN / SABIT
    [JsonPropertyName("vekilFark")] public int VekilFark { get; set; }
}

/// <summary>
/// Servisin durumu. Reji uygulamasi ve izleme sistemi bunu okuyor.
///
/// En tehlikeli ariza sessiz olanidir: besleme durur, API saglikli gorunur,
/// ekranda 20 dakikadir ayni sayilar durur ve kimse fark etmez. Bu yuzden
/// "kac dakikadir yeni veri yok" bilgisi burada.
/// </summary>
public sealed class Saglik
{
    /// <summary> calisiyor / bayat / veritabani-yok / veri-yok </summary>
    [JsonPropertyName("durum")]            public string? Durum { get; set; }

    [JsonPropertyName("surum")]            public int Surum { get; set; }

    /// <summary> Verinin kendisinin en son degistigi an. </summary>
    [JsonPropertyName("guncelleme")]       public DateTime Guncelleme { get; set; }

    /// <summary> Veri kac dakikadir degismiyor. Besleme durduysa buyur. </summary>
    [JsonPropertyName("veriYasiDakika")]   public int VeriYasiDakika { get; set; }

    /// <summary> Veritabanina en son ne zaman ulasildi. </summary>
    [JsonPropertyName("sonBasariliOkuma")] public DateTime SonBasariliOkuma { get; set; }

    /// <summary> Veritabanina ulasilamiyorsa true; veri onbellekten servis ediliyor. </summary>
    [JsonPropertyName("bayat")]            public bool Bayat { get; set; }

    [JsonPropertyName("hata")]             public string? Hata { get; set; }

    /// <summary> Dikkat cekilmesi gereken durum varsa aciklamasi. </summary>
    [JsonPropertyName("uyari")]            public string? Uyari { get; set; }

    [JsonPropertyName("secimSayisi")]      public int SecimSayisi { get; set; }
    [JsonPropertyName("ilSayisi")]         public int IlSayisi { get; set; }
}
