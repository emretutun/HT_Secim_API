using System.Text.Json.Serialization;

namespace HT_Secim_API.Model;

/// <summary>
/// Reji uygulamasina gonderilen verinin tamami.
///
/// Bu sinif, reji uygulamasindaki SecimVerisi sinifinin karsiligi. Iki proje
/// arasinda kod paylasilmiyor; aralarindaki sozlesme bu JSON semasi.
/// Bir alan adi degistirilirse reji uygulamasi o alani sessizce bos okur,
/// bu yuzden JsonPropertyName degerleri asla keyfi degistirilmemeli.
/// </summary>
public sealed class SecimVerisi
{
    [JsonPropertyName("guncelleme")] public DateTime Guncelleme { get; set; }

    [JsonPropertyName("gruplar")]    public List<Grup> Gruplar { get; set; } = new();
    [JsonPropertyName("iller")]      public List<Il> Iller { get; set; } = new();
    [JsonPropertyName("ittifaklar")] public List<Ittifak> Ittifaklar { get; set; } = new();
    [JsonPropertyName("secenekler")] public List<Secenek> Secenekler { get; set; } = new();
    [JsonPropertyName("partiler")]   public List<Parti> Partiler { get; set; } = new();
    [JsonPropertyName("adaylar")]    public List<Aday> Adaylar { get; set; } = new();
    [JsonPropertyName("secimler")]   public List<Secim> Secimler { get; set; } = new();
}

/// <summary> Il seciciideki kategori butonlari: TURKIYE GENELI / ILLER / BUYUK SEHIRLER ... </summary>
public sealed class Grup
{
    [JsonPropertyName("kod")]      public string? Kod { get; set; }
    [JsonPropertyName("ad")]       public string? Ad { get; set; }
    [JsonPropertyName("plakalar")] public List<int> Plakalar { get; set; } = new();
}

/// <summary> Plaka 0 = Turkiye geneli, 900 = yurtdisi, 901 = gumruk </summary>
public sealed class Il
{
    [JsonPropertyName("plaka")]       public int Plaka { get; set; }
    [JsonPropertyName("ad")]          public string? Ad { get; set; }
    [JsonPropertyName("bolge")]       public string? Bolge { get; set; }

    /// <summary> Ilin cikardigi milletvekili sayisi. D'Hondt dagitimi buna gore yapiliyor. </summary>
    [JsonPropertyName("vekilKotasi")] public int VekilKotasi { get; set; }
}

public sealed class Ittifak
{
    [JsonPropertyName("kod")]          public string? Kod { get; set; }
    [JsonPropertyName("ad")]           public string? Ad { get; set; }
    [JsonPropertyName("renk")]         public string? Renk { get; set; }
    [JsonPropertyName("vizLogoImage")] public string? VizLogoImage { get; set; }
    [JsonPropertyName("vizBarImage")]  public string? VizBarImage { get; set; }
}

/// <summary> Referandum secenegi (EVET / HAYIR). Parti listesini kirletmesin diye ayri. </summary>
public sealed class Secenek
{
    [JsonPropertyName("kod")]         public string? Kod { get; set; }
    [JsonPropertyName("ad")]          public string? Ad { get; set; }
    [JsonPropertyName("renk")]        public string? Renk { get; set; }
    [JsonPropertyName("vizBarImage")] public string? VizBarImage { get; set; }
}

public sealed class Parti
{
    [JsonPropertyName("kod")]                 public string? Kod { get; set; }
    [JsonPropertyName("ad")]                  public string? Ad { get; set; }
    [JsonPropertyName("renk")]                public string? Renk { get; set; }
    [JsonPropertyName("ittifak")]             public string? Ittifak { get; set; }

    [JsonPropertyName("vizImage")]            public string? VizImage { get; set; }
    [JsonPropertyName("vizBarImage")]         public string? VizBarImage { get; set; }
    [JsonPropertyName("vizSatirImage")]       public string? VizSatirImage { get; set; }
    [JsonPropertyName("vizLogoImage")]        public string? VizLogoImage { get; set; }
    [JsonPropertyName("vizKiyasLogoImage")]   public string? VizKiyasLogoImage { get; set; }
    [JsonPropertyName("vizHaritaSeritImage")] public string? VizHaritaSeritImage { get; set; }
    [JsonPropertyName("vizMvRozetImage")]     public string? VizMvRozetImage { get; set; }

    [JsonPropertyName("haritaRenk")]          public string? HaritaRenk { get; set; }
    [JsonPropertyName("haritaRenkGenel")]     public string? HaritaRenkGenel { get; set; }
    [JsonPropertyName("meclisRenk")]          public string? MeclisRenk { get; set; }

    /// <summary> Gercek parti degil, artakalan oylarin toplandigi kalem ("DIGER"). </summary>
    [JsonPropertyName("toplu")]               public bool Toplu { get; set; }
}

public sealed class Aday
{
    [JsonPropertyName("kod")]                   public string? Kod { get; set; }
    [JsonPropertyName("ad")]                    public string? Ad { get; set; }
    [JsonPropertyName("tamAd")]                 public string? TamAd { get; set; }
    [JsonPropertyName("parti")]                 public string? Parti { get; set; }
    [JsonPropertyName("ittifak")]               public string? Ittifak { get; set; }

    [JsonPropertyName("vizImage")]              public string? VizImage { get; set; }
    [JsonPropertyName("vizBarImage")]           public string? VizBarImage { get; set; }
    [JsonPropertyName("vizHaritaImage")]        public string? VizHaritaImage { get; set; }
    [JsonPropertyName("vizSehirImage")]         public string? VizSehirImage { get; set; }
    [JsonPropertyName("vizTurImage")]           public string? VizTurImage { get; set; }
    [JsonPropertyName("vizKarsilastirmaImage")] public string? VizKarsilastirmaImage { get; set; }

    [JsonPropertyName("haritaRenk")]            public string? HaritaRenk { get; set; }
    [JsonPropertyName("haritaRenkGenel")]       public string? HaritaRenkGenel { get; set; }
}

/// <summary> Bir secim veri seti: CB_2023, MV_2023, MV_2018 ... </summary>
public sealed class Secim
{
    [JsonPropertyName("kod")]      public string? Kod { get; set; }
    [JsonPropertyName("ad")]       public string? Ad { get; set; }

    /// <summary> CB / MV / REF </summary>
    [JsonPropertyName("tip")]      public string? Tip { get; set; }
    [JsonPropertyName("yil")]      public int Yil { get; set; }
    [JsonPropertyName("sonuclar")] public List<IlSonucu> Sonuclar { get; set; } = new();

    /// <summary>
    /// Baraj kurali. JSON'a yazilmiyor - reji uygulamasinin isine yaramiyor,
    /// yalnizca vekil hesabinda kullaniliyor.
    /// </summary>
    [JsonIgnore] public string? BarajTipi { get; set; }
    [JsonIgnore] public int BarajOran { get; set; }
}

/// <summary>
/// Bir ilin (veya Turkiye genelinin) bir secimdeki sonucu.
///
/// Veritabani ham ADET tutuyor (sandik sayisi, secmen sayisi, gecersiz oy);
/// yuzdeler burada hesaplaniyor. Reji uygulamasi yalnizca yuzdeleri
/// kullaniyor ama ham sayilar da cevaba konuyor - gercek bir secim API'sinde
/// tuketicinin ikisine de erismesi normaldir.
/// </summary>
public sealed class IlSonucu
{
    [JsonPropertyName("plaka")]           public int Plaka { get; set; }

    // --- ham sayilar (veritabanindan) ---
    [JsonPropertyName("toplamSandik")]    public int ToplamSandik { get; set; }
    [JsonPropertyName("acilanSandikAdet")] public int AcilanSandikAdet { get; set; }
    [JsonPropertyName("secmenSayisi")]    public long SecmenSayisi { get; set; }
    [JsonPropertyName("gecersizOy")]      public long GecersizOy { get; set; }

    // --- hesaplananlar ---

    /// <summary> acilan / toplam. Yuzde x100: 10000 = %100,00 </summary>
    [JsonPropertyName("acilanSandik")]    public int AcilanSandik { get; set; }

    /// <summary> (gecerli + gecersiz) / secmen. Yuzde x100. </summary>
    [JsonPropertyName("katilim")]         public int Katilim { get; set; }

    /// <summary> Oylarin toplami. </summary>
    [JsonPropertyName("gecerliOy")]       public long GecerliOy { get; set; }

    [JsonPropertyName("guncelleme")]      public DateTime Guncelleme { get; set; }

    [JsonPropertyName("oylar")]           public List<Oy> Oylar { get; set; } = new();
}

/// <summary> Bir kalemin tek bir ildeki sonucu </summary>
public sealed class Oy
{
    /// <summary> MV'de parti, CB'de aday, REF'te secenek kodu </summary>
    [JsonPropertyName("kod")]   public string Kod { get; set; } = "";

    /// <summary> Yuzde x100: 4393 = %43,93. Veritabaninda yok, hesaplaniyor. </summary>
    [JsonPropertyName("oran")]  public int Oran { get; set; }

    [JsonPropertyName("oy")]    public long OySayisi { get; set; }

    /// <summary> Sadece MV secimlerinde. Veritabaninda yok, D'Hondt ile hesaplaniyor. </summary>
    [JsonPropertyName("vekil")] public int Vekil { get; set; }
}

/// <summary> /api/surum cevabi. Reji uygulamasi 25 saniyede bir bunu soruyor. </summary>
public sealed class SurumBilgisi
{
    [JsonPropertyName("surum")]      public int Surum { get; set; }
    [JsonPropertyName("guncelleme")] public DateTime Guncelleme { get; set; }
    [JsonPropertyName("aciklama")]   public string? Aciklama { get; set; }
}
