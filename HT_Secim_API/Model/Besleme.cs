using System.ComponentModel.DataAnnotations;

namespace HT_Secim_API.Model;

/// <summary>
/// Besleme servisinin gonderdigi TAM ANLIK GORUNTU.
///
/// Fark degil tam goruntu: tek bir kayip paket butun geceyi bozar ve
/// kimse fark etmez. Tam goruntude her istek kendi basina dogru.
///
/// YALNIZCA HAM SAYI tasinir. Yuzde, TURKIYE satiri ve milletvekili
/// dagilimi burada YOK - hepsini API hesapliyor. Tek yerde hesaplanmasi,
/// ekranin altiyla ustunun birbiriyle celismesini imkansiz kiliyor.
/// </summary>
public sealed class BeslemeIstegi
{
    /// <summary> Ajans adi: ANKA, AA, PROVA... Gunluge yaziliyor. </summary>
    [Required, MaxLength(64)]
    public string Kaynak { get; set; } = "";

    /// <summary> Dosyanin ajansta uretildigi an. </summary>
    public DateTime? Uretim { get; set; }

    /// <summary> Ardisik dosya numarasi; eski dosyayi yakalamak icin. </summary>
    public long Sira { get; set; }

    /// <summary> Kaynak dosyanin adi. Yalnizca gunluk icin. </summary>
    [MaxLength(200)]
    public string? Dosya { get; set; }

    [Required]
    public List<BeslemeSecim> Secimler { get; set; } = new();
}

public sealed class BeslemeSecim
{
    [Required, MaxLength(32)]
    public string Kod { get; set; } = "";

    [Required]
    public List<BeslemeIl> Iller { get; set; } = new();
}

public sealed class BeslemeIl
{
    public int Plaka { get; set; }

    public int ToplamSandik { get; set; }
    public int AcilanSandik { get; set; }

    public long SecmenSayisi { get; set; }
    public long GecersizOy { get; set; }

    /// <summary>
    /// Ajansin bildirdigi gecerli oy.
    ///
    /// VERITABANINA YAZILMIYOR: gecerli oy, oy satirlarinin toplami
    /// olarak hesaplaniyor. Burada yalnizca DOGRULAMA icin duruyor -
    /// oylarin toplami bundan buyukse dosyada bir tutarsizlik var
    /// demektir ve dosya reddediliyor.
    /// </summary>
    public long GecerliOy { get; set; }

    [Required]
    public List<BeslemeOy> Oylar { get; set; } = new();
}

public sealed class BeslemeOy
{
    [Required, MaxLength(32)]
    public string Kod { get; set; } = "";

    public long Sayi { get; set; }
}

/// <summary>
/// Sayaclari sifirlama istegi.
///
/// Gece baslamadan once sayaclar sifirlanir: acilan sandik 0, butun
/// oylar 0. Toplam sandik ve secmen sayisi KALIR - onlar geceden once
/// bilinen degerler, sayilacak olan sey degil.
///
/// Prova icin de gerekli ama asil kullanim yeri gercek secim gecesidir.
/// </summary>
public sealed class SifirlamaIstegi
{
    /// <summary> Sifirlanacak secim kodlari. Bos ise hicbir sey yapilmaz. </summary>
    [Required]
    public List<string> Secimler { get; set; } = new();

    /// <summary>
    /// Kazara calistirilmasin diye: tam olarak "SIFIRLA" yazilmali.
    ///
    /// Bu uc butun bir secimin sayimini siliyor. Yanlislikla atilan bir
    /// istekle yayindaki sonuclarin sifirlanmasi kabul edilemez.
    /// </summary>
    [Required]
    public string Onay { get; set; } = "";
}

/// <summary> Besleme isteginin sonucu. </summary>
public sealed class BeslemeCevabi
{
    public bool Kabul { get; set; }

    /// <summary> Kabul edildiyse yeni surum numarasi. </summary>
    public int Surum { get; set; }

    public int IlSayisi { get; set; }
    public int OySatiri { get; set; }

    /// <summary> Reddedildiyse sebep; kabul edildiyse ozet. </summary>
    public string Aciklama { get; set; } = "";

    /// <summary>
    /// Dosyayi reddettirmeyen ama operatorun gormesi gereken durumlar:
    /// tanimsiz parti kodu, ani siçrama, eksik il.
    /// </summary>
    public List<string> Uyarilar { get; set; } = new();
}
