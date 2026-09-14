using System.Text.Json.Serialization;

namespace HT_Secim_API.Model;

/* Yonetim panelinin kullandigi tipler.
   Buradaki degerler HAM - panelde girilen sey adet, yuzde degil. */

/// <summary> Panelde duzenlenen bir ilin ham sayilari. </summary>
public sealed class YonetimSatiri
{
    [JsonPropertyName("secim")]            public string? Secim { get; set; }
    [JsonPropertyName("secimAdi")]         public string? SecimAdi { get; set; }
    [JsonPropertyName("tip")]              public string? Tip { get; set; }
    [JsonPropertyName("plaka")]            public int Plaka { get; set; }
    [JsonPropertyName("il")]               public string? Il { get; set; }

    [JsonPropertyName("toplamSandik")]     public int ToplamSandik { get; set; }
    [JsonPropertyName("acilanSandikAdet")] public int AcilanSandikAdet { get; set; }
    [JsonPropertyName("secmenSayisi")]     public long SecmenSayisi { get; set; }
    [JsonPropertyName("gecersizOy")]       public long GecersizOy { get; set; }
    [JsonPropertyName("guncelleme")]       public DateTime Guncelleme { get; set; }

    [JsonPropertyName("oylar")]            public List<YonetimOyu> Oylar { get; set; } = new();

    /// <summary> Bu secimde tanimli olup bu ilde kaydi olmayan kalemler. </summary>
    [JsonPropertyName("eklenebilir")]      public List<YonetimOyu> Eklenebilir { get; set; } = new();
}

public sealed class YonetimOyu
{
    [JsonPropertyName("kod")] public string Kod { get; set; } = "";
    [JsonPropertyName("ad")]  public string? Ad { get; set; }
    [JsonPropertyName("oy")]  public long Oy { get; set; }
}

/// <summary> Panelden gelen kaydetme istegi. </summary>
public sealed class YonetimKayit
{
    [JsonPropertyName("toplamSandik")]     public int ToplamSandik { get; set; }
    [JsonPropertyName("acilanSandikAdet")] public int AcilanSandikAdet { get; set; }
    [JsonPropertyName("secmenSayisi")]     public long SecmenSayisi { get; set; }
    [JsonPropertyName("gecersizOy")]       public long GecersizOy { get; set; }

    [JsonPropertyName("oylar")]            public List<YonetimOyu> Oylar { get; set; } = new();

    /// <summary> Degisiklik kaydina yazilacak operator adi. </summary>
    [JsonPropertyName("kullanici")]        public string? Kullanici { get; set; }

    [JsonPropertyName("aciklama")]         public string? Aciklama { get; set; }
}

/// <summary> Denetim kaydi satiri. </summary>
public sealed class DegisiklikSatiri
{
    [JsonPropertyName("id")]        public long Id { get; set; }
    [JsonPropertyName("zaman")]     public DateTime Zaman { get; set; }
    [JsonPropertyName("kaynak")]    public string? Kaynak { get; set; }
    [JsonPropertyName("kullanici")] public string? Kullanici { get; set; }
    [JsonPropertyName("secim")]     public string? Secim { get; set; }
    [JsonPropertyName("plaka")]     public int Plaka { get; set; }
    [JsonPropertyName("il")]        public string? Il { get; set; }
    [JsonPropertyName("alan")]      public string? Alan { get; set; }
    [JsonPropertyName("eski")]      public long? Eski { get; set; }
    [JsonPropertyName("yeni")]      public long? Yeni { get; set; }
    [JsonPropertyName("aciklama")]  public string? Aciklama { get; set; }
}
