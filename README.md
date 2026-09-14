# HT SEÇİM API

Seçim sonuçlarını servis eden REST API. Reji uygulamasının (HT_SECIM_REJI) veri
kaynağı; aynı zamanda elle veri girişi için bir yönetim paneli barındırır.

ASP.NET Core 8, SQL Server.

---

## Tasarımın özü: veritabanı yalnızca ham sayı tutar

Türetilmiş hiçbir değer veritabanında saklanmaz. Oran, geçerli oy, Türkiye satırı
ve milletvekili dağılımı **API'de hesaplanır**.

| Veritabanında | API'nin hesapladığı |
|---|---|
| `Oy.oy` — adet | `oran` = oy / geçerliOy × 10000 |
| `IlSonucu.toplamSandik`, `acilanSandik` — adet | `acilanSandik%` = açılan / toplam |
| `IlSonucu.secmenSayisi`, `gecersizOy` — adet | `katilim%` = (geçerli + geçersiz) / seçmen |
| — | `gecerliOy` = o ildeki oyların toplamı |
| — | **TÜRKİYE satırı** = 81 ilin toplamı (veritabanında yok, üretilir) |
| — | `vekil` = il bazında D'Hondt |

Bunun sonucu: **"veri kendi içinde tutarsız" diye bir durum oluşamaz.** İl
oranları her zaman tam 10000'e toplanır, ülke satırı her zaman illerin
toplamıdır, il vekilleri her zaman ilin kotasını tutar.

### Hesap ayrıntıları

**Yuvarlama.** `oran` tam sayı (yüzde ×100). Her kalem ayrı yuvarlanırsa toplam
9999 ya da 10001 çıkabiliyor. "En büyük kalan" yöntemi kullanılıyor: önce aşağı
yuvarlanır, artan birimler kesirli kısmı en büyük olanlara dağıtılır. Eşitlikte
koda göre sıralanır — aynı veri her zaman aynı sonucu verir.

**Baraj kuralı seçime göre değişir** ve `Secim` tablosunda tutulur, kodda sabit
değildir:

```
MV_2023, MV_2018  ->  ITTIFAK, 700    (%7, ittifak toplami uzerinden)
MV_2015           ->  PARTI,  1000    (%10, parti basina - o yil ittifak yoktu)
CB_*, REF_*       ->  NULL            (vekil dagitimi yok)
```

**Yurtdışı (900) ve gümrük (901)** satırları veride tutulur ve servis edilir,
ama TÜRKİYE toplamının hesabına katılmaz.

---

## Uçlar

Hepsi `X-API-KEY` başlığı ister. Swagger: `http://localhost:5188/swagger`

### Sistem

```
GET  /api/v1/saglik      servis durumu, verinin yasi, bayatlik uyarisi
GET  /api/v1/surum       surum numarasi ve son degisiklik zamani
GET  /api/v1/veri        butun veri tek pakette (~200 KB, ETag / 304)
```

### Tanımlar

```
GET  /api/v1/iller                 /api/v1/iller/{il}
GET  /api/v1/partiler              /api/v1/partiler/{kod}
GET  /api/v1/adaylar               /api/v1/adaylar/{kod}
GET  /api/v1/ittifaklar            /api/v1/ittifaklar/{kod}
GET  /api/v1/gruplar               /api/v1/gruplar/{kod}
GET  /api/v1/secimler              /api/v1/secimler/{secim}
```

### Sonuçlar

```
GET  /api/v1/secimler/{secim}/sonuclar
GET  /api/v1/secimler/{secim}/sonuclar/{il}
GET  /api/v1/secimler/{secim}/sonuclar/{il}/{kod}
GET  /api/v1/secimler/{secim}/siralama/{il}
GET  /api/v1/secimler/{secim}/kazanan/{il}
GET  /api/v1/secimler/{secim}/kazananlar
GET  /api/v1/secimler/{secim}/ittifaklar/{il}
GET  /api/v1/secimler/{secim}/vekiller
GET  /api/v1/secimler/{secim}/katilim            /api/v1/secimler/{secim}/katilim/{il}
GET  /api/v1/secimler/{secim}/kalemler/{kod}
GET  /api/v1/secimler/{secim}/kalemler/{kod}/enler?adet=10
GET  /api/v1/secimler/{secim}/karsilastir/{il}/{kod}?ile=MV_2018
```

`{il}` hem plaka hem ad kabul eder: `6`, `06`, `ankara`, `ANKARA`. Türkçe
büyük/küçük harf farkı için harfler sadeleştirilerek karşılaştırılır (İ/ı tuzağı).

**Örnek**

```
GET /api/v1/secimler/MV_2023/sonuclar/ankara/CHP

{"secim":"MV_2023","secimAdi":"MİLLETVEKİLİ SEÇİMİ","plaka":6,"il":"ANKARA",
 "kod":"CHP","ad":"CHP","oran":2874,"oranYazi":"28,74","oy":659296,
 "vekil":11,"sira":2}
```

### Yönetim (yazma yetkisi ister)

```
GET  /api/v1/yonetim/secenekler
GET  /api/v1/yonetim/sonuc/{secim}/{plaka}
PUT  /api/v1/yonetim/sonuc/{secim}/{plaka}
GET  /api/v1/yonetim/degisiklikler?adet=50
```

---

## Neden hem toplu hem ayrıntılı uç var

Ayrıntılı uçlar API'nin yüzüdür: tek tek denenebilir, başka bir tüketici
geldiğinde işe yarar.

`/api/v1/veri` ise reji uygulaması içindir. Bir grafik sahnesinde 20-30 değer
vardır ve **hepsinin aynı ana ait olması gerekir**. Tek tek çekilirse aralarında
veri güncellenip ekranda yarısı eski bir tablo oluşabilir. Gerçek seçim
beslemeleri (AP, Bundeswahlleiter) de bu yüzden toplu anlık görüntü mantığıyla
çalışır.

İkisi de aynı bellekteki veriden servis edilir, ek SQL sorgusu yoktur.

---

## Önbellek ve dayanıklılık

Veri **sürüm numarasına bağlı** olarak önbellekte tutulur. `VeriSurum` tablosunu
on tablodaki trigger'lar günceller; numara değişmediği sürece veritabanına hiç
gidilmez, hazır JSON döner.

**Veritabanına ulaşılamazsa hata dönmez.** Elimizdeki son sağlam veri servis
edilmeye devam eder, `X-Veri-Bayat: 1` başlığı ve sağlık ucuyla bildirilir. Canlı
yayında SQL'in bir anlık takılması ekranı boşaltmamalıdır.

**Sessiz besleme arızası** en tehlikeli hatadır: veri gelmeyi keser, API sağlıklı
görünür, ekranda 20 dakikadır aynı sayılar durur. `saglik` ucu bunun için
`veriYasiDakika` döner ve eşiği geçerse `"durum":"bayat"` der.

On tablo **tek sorguda ve tek SNAPSHOT'ta** okunur — arka arkaya okunsaydı
aralarında biri veriyi değiştirdiğinde yarısı eski bir tablo elde edilebilirdi.

---

## Güvenlik

İki ayrı anahtar (`appsettings.json` → `HtSecim`):

| Anahtar | Kim kullanır |
|---|---|
| `OkumaAnahtari` | Reji uygulaması, iş ortakları — yalnızca okur |
| `YazmaAnahtari` | Yönetim paneli, ileride besleme servisi. Okuma yetkisini de kapsar |

Ayrı olmalarının sebebi: okuma anahtarı birçok makineye dağıtılır ve er geç
sızar. Sızan bir anahtarla seçim verisinin değiştirilebilmesi kabul edilemez.

---

## Yönetim paneli

```
http://localhost:5188/yonetim/
```

İlk açılışta yazma anahtarını sorar, tarayıcıda saklar. Seçim ve il seçilir,
ham sayılar düzenlenir.

- Girilen her şey **adet**: sandık, seçmen, geçersiz oy, kalem kalem oy
- **Geçerli oy elle girilmez** — oyların toplamıdır, üstte canlı gösterilir
- Yüzdeler yazarken anında hesaplanır; reji kaydetmeden ekrana ne düşeceğini görür
- Tutarsız veri kaydedilemez (açılan > toplam sandık, kullanılan oy > seçmen)
- **TÜRKİYE satırı düzenlenemez** — illerden hesaplanır

### Denetim kaydı

Her değişiklik `Degisiklik` tablosuna yazılır: kim, ne zaman, hangi alan, eski →
yeni değer, açıklama. Canlı yayında ekrana yanlış bir sayı düştüğünde tek sorulan
şey "bunu kim ne zaman girdi" olur; kayıt tutulmazsa cevabı yoktur.

---

## Kurulum

### 1. Veritabanı

`sql/` klasöründeki script'ler sırayla:

| Dosya | İş |
|---|---|
| `01_sema.sql` | Veritabanı, 11 tablo, sürüm trigger'ları, snapshot izolasyonu |
| `02_aktar.ps1` | `veri.json` → veritabanı (başlangıç verisi) |
| `03_ham_sandik.sql` | `IlSonucu`'yu yüzdeden ham adede çevirir |
| `04_degisiklik.sql` | Denetim kaydı tablosu |

```
sqlcmd -S "localhost\SQLEXPRESS" -E -C -i sql\01_sema.sql
powershell -File sql\02_aktar.ps1
sqlcmd -S "localhost\SQLEXPRESS" -E -C -i sql\03_ham_sandik.sql
sqlcmd -S "localhost\SQLEXPRESS" -E -C -i sql\04_degisiklik.sql
```

> **Dikkat:** PowerShell 5.1, BOM'suz dosyaları ANSI sanar ve içindeki Türkçe
> harfleri bozar. Bu klasördeki script'ler UTF-8 **BOM ile** kaydedilmelidir.

### 2. Ayarlar

`appsettings.json`:

```json
"ConnectionStrings": { "HtSecim": "Server=localhost\\SQLEXPRESS;Database=HT_SECIM;..." },
"HtSecim": {
  "OkumaAnahtari": "...",
  "YazmaAnahtari": "...",
  "BayatDakika": 10
}
```

### 3. Çalıştırma

```
dotnet run --project HT_Secim_API --urls http://localhost:5188
```

`UseHttpsRedirection` bilerek kapalıdır: reji uygulaması .NET Framework 4.7.2 ve
yerel ağda düz HTTP ile bağlanır. Gerçek dağıtımda API sertifikalı bir sunucunun
arkasına konur, kodda bir şey değişmez — reji uygulamasının `api` dosyasına
`https://...` yazılır.

---

## Klasörler

```
Controllers/   Veri, Tanimlar, Sonuclar, Yonetim
Model/         JSON semasi ve cevap tipleri
Veri/          VeriDeposu (okuma + onbellek), YonetimDeposu (yazma), Hesap, Arama
Guvenlik/      X-API-KEY filtresi ve yetki ayrimi
wwwroot/       Yonetim paneli
sql/           Sema ve aktarim script'leri
```

---

## Yapılmayanlar

Bu bir çalışma projesidir. Gerçek bir seçim gecesine çıkacaksa eksikler:

- **Besleme ucu** — veri şu an yalnızca elle giriliyor. `POST /api/v1/besleme`
  ve yanında **geriye gitme koruması**: paketler tekrarlanır ve sırasız gelir,
  geç gelen eski bir paket açılan sandığı %60'tan %45'e düşürür
- **Anormal veri alarmı** — bir ilde bir parti tek güncellemede 20 puan
  sıçradıysa bu büyük ihtimalle besleme hatasıdır, ekrana düşmeden yakalanmalı
- **Ambargo bayrağı** — YSK resmi açıklama öncesi ambargo koyar
- **Windows Service** — API elle başlatılıyor, makine yeniden başlarsa gelmiyor
- **Prova** — seçim gecesi ilk kez çalıştırılan hiçbir sistem sağ çıkmaz

---

## Not

Depodaki `veri.json` kaynaklı başlangıç verisi **sentetiktir**, gerçek YSK
sonuçları değildir. Ulusal oranlar gerçek seçimlerden alınmış, il dağılımları
tutarlılık kuralları korunacak şekilde üretilmiştir.
