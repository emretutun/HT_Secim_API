$ErrorActionPreference = 'Stop'

# veri.json -> HT_SECIM veritabani.
#
# Sadece HAM alanlar aktariliyor:
#   Oy.oy              (adet)            -> oran ve vekil aktarilmiyor, API hesapliyor
#   IlSonucu           (sandik, katilim) -> gecerliOy aktarilmiyor, oylarin toplami
#   plaka 0 (TURKIYE)                    -> hic aktarilmiyor, API illerden uretiyor
#
# Aktarim boyunca surum trigger'lari kapali; sonunda surum bir kez artiriliyor.
#
# Kullanim:  powershell -File 02_aktar.ps1

$json   = 'C:\Users\metutun\Desktop\HT_SECIM\HT_SECIM\veri.json'
$sunucu = 'localhost\SQLEXPRESS'
$cs     = "Server=$sunucu;Database=HT_SECIM;Integrated Security=true;TrustServerCertificate=true"

# Secime gore baraj kurali. 2015'te ittifak yoktu, baraj parti basina %10'du.
$baraj = @{
    'MV_2023' = @{ tip = 'ITTIFAK'; oran = 700  }
    'MV_2018' = @{ tip = 'ITTIFAK'; oran = 700  }
    'MV_2015' = @{ tip = 'PARTI';   oran = 1000 }
}

$v = Get-Content $json -Raw -Encoding UTF8 | ConvertFrom-Json

$cn = New-Object System.Data.SqlClient.SqlConnection $cs
$cn.Open()

function Calistir([string]$sql) {
    $cmd = $cn.CreateCommand()
    $cmd.CommandText = $sql
    $cmd.CommandTimeout = 300
    [void]$cmd.ExecuteNonQuery()
}

$tablolar = @('Oy','IlSonucu','Secim','Aday','Parti','Secenek','Ittifak','GrupIl','Grup','Il')

Write-Output '--- trigger kapatiliyor'
foreach ($t in $tablolar) { Calistir "ALTER TABLE dbo.$t DISABLE TRIGGER ALL" }

Write-Output '--- eski kayitlar siliniyor'
foreach ($t in $tablolar) { Calistir "DELETE FROM dbo.$t" }

function TabloYaz([string]$hedef, [System.Data.DataTable]$dt) {
    if ($dt.Rows.Count -eq 0) { Write-Output ("  {0,-10} 0" -f $hedef); return }

    $bc = New-Object System.Data.SqlClient.SqlBulkCopy $cn
    $bc.DestinationTableName = "dbo.$hedef"
    $bc.BulkCopyTimeout = 600
    foreach ($c in $dt.Columns) { [void]$bc.ColumnMappings.Add($c.ColumnName, $c.ColumnName) }
    $bc.WriteToServer($dt)
    $bc.Close()

    Write-Output ("  {0,-10} {1}" -f $hedef, $dt.Rows.Count)
}

function YeniTablo([string[]]$kolonlar, [Type[]]$tipler) {
    $dt = New-Object System.Data.DataTable
    for ($i = 0; $i -lt $kolonlar.Count; $i++) { [void]$dt.Columns.Add($kolonlar[$i], $tipler[$i]) }
    return ,$dt
}

# Bos metin foreign key'de NULL olmali
function Bos($x)   { if ([string]::IsNullOrWhiteSpace($x)) { return [DBNull]::Value } else { return $x } }
function Metin($x) { if ($null -eq $x) { return [DBNull]::Value } else { return [string]$x } }

Write-Output '--- yaziliyor'

# ---- Il ----
$dt = YeniTablo @('plaka','ad','bolge','vekilKotasi') @([int],[string],[string],[int])
foreach ($il in $v.iller) {
    [void]$dt.Rows.Add([int]$il.plaka, [string]$il.ad, (Metin $il.bolge), [int]$il.vekilKotasi)
}
TabloYaz 'Il' $dt

# ---- Grup + GrupIl ----
$dtG  = YeniTablo @('kod','ad','sira') @([string],[string],[int])
$dtGI = YeniTablo @('grupKod','plaka','sira') @([string],[int],[int])
$s = 0
foreach ($g in $v.gruplar) {
    [void]$dtG.Rows.Add([string]$g.kod, [string]$g.ad, $s); $s++
    $p = 0
    foreach ($plaka in $g.plakalar) { [void]$dtGI.Rows.Add([string]$g.kod, [int]$plaka, $p); $p++ }
}
TabloYaz 'Grup'   $dtG
TabloYaz 'GrupIl' $dtGI

# ---- Ittifak ----
$dt = YeniTablo @('kod','ad','renk','vizLogoImage','vizBarImage') @([string],[string],[string],[string],[string])
foreach ($i in $v.ittifaklar) {
    [void]$dt.Rows.Add([string]$i.kod, [string]$i.ad, (Metin $i.renk), (Metin $i.vizLogoImage), (Metin $i.vizBarImage))
}
TabloYaz 'Ittifak' $dt

# ---- Secenek ----
$dt = YeniTablo @('kod','ad','renk','vizBarImage') @([string],[string],[string],[string])
foreach ($x in $v.secenekler) {
    [void]$dt.Rows.Add([string]$x.kod, [string]$x.ad, (Metin $x.renk), (Metin $x.vizBarImage))
}
TabloYaz 'Secenek' $dt

# ---- Parti ----
$kolonlar = @('kod','ad','renk','ittifakKod','vizImage','vizBarImage','vizSatirImage','vizLogoImage',
              'vizKiyasLogoImage','vizHaritaSeritImage','vizMvRozetImage','haritaRenk','haritaRenkGenel',
              'meclisRenk','toplu')
$dt = YeniTablo $kolonlar (@([string]) * 14 + @([bool]))
foreach ($p in $v.partiler) {
    [void]$dt.Rows.Add(
        [string]$p.kod, [string]$p.ad, (Metin $p.renk), (Bos $p.ittifak),
        (Metin $p.vizImage), (Metin $p.vizBarImage), (Metin $p.vizSatirImage), (Metin $p.vizLogoImage),
        (Metin $p.vizKiyasLogoImage), (Metin $p.vizHaritaSeritImage), (Metin $p.vizMvRozetImage),
        (Metin $p.haritaRenk), (Metin $p.haritaRenkGenel), (Metin $p.meclisRenk),
        [bool]$p.toplu)
}
TabloYaz 'Parti' $dt

# ---- Aday ----
$kolonlar = @('kod','ad','tamAd','partiKod','ittifakKod','vizImage','vizBarImage','vizHaritaImage',
              'vizSehirImage','vizTurImage','vizKarsilastirmaImage','haritaRenk','haritaRenkGenel')
$dt = YeniTablo $kolonlar (@([string]) * 13)
foreach ($a in $v.adaylar) {
    [void]$dt.Rows.Add(
        [string]$a.kod, [string]$a.ad, (Metin $a.tamAd), (Bos $a.parti), (Bos $a.ittifak),
        (Metin $a.vizImage), (Metin $a.vizBarImage), (Metin $a.vizHaritaImage),
        (Metin $a.vizSehirImage), (Metin $a.vizTurImage), (Metin $a.vizKarsilastirmaImage),
        (Metin $a.haritaRenk), (Metin $a.haritaRenkGenel))
}
TabloYaz 'Aday' $dt

# ---- Secim ----
$dt = YeniTablo @('kod','ad','tip','yil','barajTipi','barajOran','sira') @([string],[string],[string],[int],[string],[int],[int])
$s = 0
foreach ($sec in $v.secimler) {
    $b  = $baraj[[string]$sec.kod]
    $bt = if ($b) { $b.tip }  else { [DBNull]::Value }
    $bo = if ($b) { $b.oran } else { [DBNull]::Value }

    [void]$dt.Rows.Add([string]$sec.kod, [string]$sec.ad, [string]$sec.tip, [int]$sec.yil, $bt, $bo, $s)
    $s++
}
TabloYaz 'Secim' $dt

# ---- IlSonucu + Oy (plaka 0 haric) ----
$dtS = YeniTablo @('secimKod','plaka','acilanSandik','katilim') @([string],[int],[int],[int])
$dtO = YeniTablo @('secimKod','plaka','kod','oy') @([string],[int],[string],[long])

foreach ($sec in $v.secimler) {
    foreach ($son in $sec.sonuclar) {
        $plaka = [int]$son.plaka
        if ($plaka -eq 0) { continue }   # TURKIYE satiri API'de uretiliyor

        [void]$dtS.Rows.Add([string]$sec.kod, $plaka, [int]$son.acilanSandik, [int]$son.katilim)

        foreach ($o in $son.oylar) {
            [void]$dtO.Rows.Add([string]$sec.kod, $plaka, [string]$o.kod, [long]$o.oy)
        }
    }
}
TabloYaz 'IlSonucu' $dtS
TabloYaz 'Oy'       $dtO

Write-Output '--- trigger aciliyor'
foreach ($t in $tablolar) { Calistir "ALTER TABLE dbo.$t ENABLE TRIGGER ALL" }

Calistir "EXEC dbo.sp_SurumArtir N'veri.json aktarildi'"

$cmd = $cn.CreateCommand()
$cmd.CommandText = "SELECT surum, guncelleme, aciklama FROM dbo.VeriSurum WHERE id=1"
$r = $cmd.ExecuteReader(); [void]$r.Read()
Write-Output ("--- surum {0}  {1}  ({2})" -f $r[0], $r[1], $r[2])
$r.Close()

$cn.Close()
Write-Output 'bitti'
