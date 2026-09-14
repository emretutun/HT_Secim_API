/* =====================================================================
   IlSonucu tablosunu HAM sayiya cevirir.

   NEDEN
   -----
   Semanin kurali "veritabani yalnizca ham sayi tutar" idi, ama IlSonucu
   iki tane TURETILMIS yuzde tutuyordu:

       acilanSandik = 9781   (%97,81)
       katilim      = 8478   (%84,78)

   Gercek bir secim beslemesinden yuzde gelmez, ADET gelir:
       "12.450 sandigin 11.892'si acildi"
       "3.640.000 secmenin 2.365.000'i oy kullandi, 71.000'i gecersiz"

   Yuzdeleri API hesaplar:
       acilanSandik% = acilan / toplam                  x 10000
       katilim%      = (gecerliOy + gecersizOy) / secmen x 10000
       gecerliOy     = SUM(Oy.oy)

   ESKI DEGERLERDEN ADET URETIMI
   -----------------------------
   Elimizde yalnizca yuzdeler vardi, adetler yoktu. Bugunku yuzdeler
   korunacak sekilde geriye dogru uretiliyor:

     gecersizOy   = gecerli x 3 / 97          (Turkiye'de gecersiz oy ~%3)
     secmenSayisi = (gecerli + gecersiz) x 10000 / katilim
     toplamSandik = secmen / 400              (sandik basina ~400 secmen)
     acilanSandik = toplamSandik x eskiYuzde / 10000

   Boylece API'nin hesapladigi yuzdeler eskisiyle ayni cikar; fark
   yuvarlamadan kaynakli en fazla 1 birim olur.
   ===================================================================== */

USE HT_SECIM;
GO

/* Trigger'lar donusum boyunca sussun, sonunda surum bir kez artsin. */
ALTER TABLE dbo.IlSonucu DISABLE TRIGGER ALL;
GO

/* ---- 1) yeni kolonlar ---- */

IF COL_LENGTH('dbo.IlSonucu','toplamSandik') IS NULL
    ALTER TABLE dbo.IlSonucu ADD toplamSandik INT NOT NULL CONSTRAINT DF_IlSonucu_toplamSandik DEFAULT(0);
GO
IF COL_LENGTH('dbo.IlSonucu','secmenSayisi') IS NULL
    ALTER TABLE dbo.IlSonucu ADD secmenSayisi BIGINT NOT NULL CONSTRAINT DF_IlSonucu_secmen DEFAULT(0);
GO
IF COL_LENGTH('dbo.IlSonucu','gecersizOy') IS NULL
    ALTER TABLE dbo.IlSonucu ADD gecersizOy BIGINT NOT NULL CONSTRAINT DF_IlSonucu_gecersiz DEFAULT(0);
GO
IF COL_LENGTH('dbo.IlSonucu','guncelleme') IS NULL
    ALTER TABLE dbo.IlSonucu ADD guncelleme DATETIME2(0) NOT NULL CONSTRAINT DF_IlSonucu_guncelleme DEFAULT(SYSDATETIME());
GO

/* ---- 2) eski yuzdelerden adet uret ---- */

IF COL_LENGTH('dbo.IlSonucu','acilanSandikYuzde') IS NULL
BEGIN
    /* acilanSandik simdilik yuzde tutuyor; adedi hesaplayabilmek icin
       once bir kenara aliniyor. */
    EXEC sp_rename 'dbo.IlSonucu.acilanSandik', 'acilanSandikYuzde', 'COLUMN';

    ALTER TABLE dbo.IlSonucu ADD acilanSandik INT NOT NULL CONSTRAINT DF_IlSonucu_acilan DEFAULT(0);
END
GO

WITH g AS (
    SELECT secimKod, plaka, SUM(oy) AS gecerli
      FROM dbo.Oy
     GROUP BY secimKod, plaka
)
UPDATE s
   SET gecersizOy   = CAST(g.gecerli * 3 / 97 AS BIGINT),

       secmenSayisi = CASE WHEN s.katilim > 0
                           THEN CAST((g.gecerli + g.gecerli * 3 / 97) * 10000.0 / s.katilim AS BIGINT)
                           ELSE 0 END,

       toplamSandik = CASE WHEN s.katilim > 0
                           THEN CAST((g.gecerli + g.gecerli * 3 / 97) * 10000.0 / s.katilim / 400 AS INT)
                           ELSE 0 END
  FROM dbo.IlSonucu s
  JOIN g ON g.secimKod = s.secimKod AND g.plaka = s.plaka;
GO

/* Yurtdisi / gumruk gibi sandik sayisi anlamsiz olan satirlarda en az 1 sandik. */
UPDATE dbo.IlSonucu SET toplamSandik = 1 WHERE toplamSandik < 1;
GO

UPDATE dbo.IlSonucu
   SET acilanSandik = CASE
                        WHEN acilanSandikYuzde >= 10000 THEN toplamSandik
                        ELSE CAST(toplamSandik * acilanSandikYuzde / 10000.0 AS INT)
                      END;
GO

/* ---- 3) turetilmis kolonlari kaldir ---- */

IF COL_LENGTH('dbo.IlSonucu','acilanSandikYuzde') IS NOT NULL
BEGIN
    ALTER TABLE dbo.IlSonucu DROP CONSTRAINT DF_IlSonucu_sandik;
    ALTER TABLE dbo.IlSonucu DROP COLUMN acilanSandikYuzde;
END
GO

IF COL_LENGTH('dbo.IlSonucu','katilim') IS NOT NULL
BEGIN
    ALTER TABLE dbo.IlSonucu DROP CONSTRAINT DF_IlSonucu_katilim;
    ALTER TABLE dbo.IlSonucu DROP COLUMN katilim;
END
GO

/* ---- 4) tutarlilik kurallari ---- */

IF OBJECT_ID('CK_IlSonucu_sandik') IS NULL
    ALTER TABLE dbo.IlSonucu ADD CONSTRAINT CK_IlSonucu_sandik
        CHECK (acilanSandik >= 0 AND toplamSandik >= 0 AND acilanSandik <= toplamSandik);
GO

IF OBJECT_ID('CK_IlSonucu_secmen') IS NULL
    ALTER TABLE dbo.IlSonucu ADD CONSTRAINT CK_IlSonucu_secmen
        CHECK (secmenSayisi >= 0 AND gecersizOy >= 0);
GO

ALTER TABLE dbo.IlSonucu ENABLE TRIGGER ALL;
GO

EXEC dbo.sp_SurumArtir N'IlSonucu ham sayiya cevrildi';
GO

SELECT TOP 5 secimKod, plaka, acilanSandik, toplamSandik, secmenSayisi, gecersizOy
  FROM dbo.IlSonucu
 WHERE secimKod = 'MV_2018' AND plaka IN (6, 34, 35)
 ORDER BY plaka;
GO
