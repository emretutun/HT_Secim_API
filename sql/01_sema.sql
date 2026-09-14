/* =====================================================================
   HT_SECIM  -  veritabani semasi
   Sunucu: localhost\SQLEXPRESS

   TASARIM KURALI
   --------------
   Bu veritabani yalnizca HAM sayilari tutar. Turetilmis her sey API'de
   hesaplanir:

      oran        = oy / gecerliOy x 10000        (yuzde x100)
      gecerliOy   = o ildeki oylarin toplami
      TURKIYE     = 81 ilin toplami (plaka 0 satiri veride YOK, uretilir)
      vekil       = il bazinda D'Hondt (il kotasi + secimin baraj kurali)

   Boylece "veri kendi icinde tutarsiz" diye bir durum olusamaz: oranlar
   her zaman 10000'e toplanir, ulke satiri her zaman illerin toplamidir.

   Yurtdisi (900) ve Gumruk (901) satirlari veride tutulur ve ekranda
   gosterilir, ama TURKIYE satirinin hesabina KATILMAZ - bugunku veri
   seti de boyle kurulmustu.
   ===================================================================== */

IF DB_ID('HT_SECIM') IS NULL
    CREATE DATABASE HT_SECIM;
GO

/* Okuma anlik goruntusu: API veriyi okurken baska biri yaziyor olsa bile
   yarisi eski bir tablo donmesin.

   READ_COMMITTED_SNAPSHOT tek tek sorgulari korur; API 10 tabloyu arka
   arkaya okudugu icin bunlarin HEPSININ ayni ana ait olmasi gerekiyor.
   Bunu SNAPSHOT izolasyonu sagliyor, o yuzden ikisi de aciliyor. */
ALTER DATABASE HT_SECIM SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
GO
ALTER DATABASE HT_SECIM SET ALLOW_SNAPSHOT_ISOLATION ON;
GO

USE HT_SECIM;
GO

/* ---------------------------------------------------------------
   1) SABIT TANIMLAR
   --------------------------------------------------------------- */

IF OBJECT_ID('dbo.Il') IS NULL
CREATE TABLE dbo.Il
(
    plaka        INT          NOT NULL CONSTRAINT PK_Il PRIMARY KEY,
    ad           NVARCHAR(64) NOT NULL,
    bolge        NVARCHAR(64) NULL,

    /* Ilin cikardigi milletvekili sayisi (YSK). 81 ilin toplami 600.
       D'Hondt dagitimi bu sayiya gore yapilir. */
    vekilKotasi  INT          NOT NULL CONSTRAINT DF_Il_kota DEFAULT(0)
);
GO

IF OBJECT_ID('dbo.Grup') IS NULL
CREATE TABLE dbo.Grup
(
    kod  NVARCHAR(32) NOT NULL CONSTRAINT PK_Grup PRIMARY KEY,
    ad   NVARCHAR(64) NOT NULL,
    sira INT          NOT NULL CONSTRAINT DF_Grup_sira DEFAULT(0)
);
GO

/* Grup - il baglantisi (Grup.plakalar listesi) */
IF OBJECT_ID('dbo.GrupIl') IS NULL
CREATE TABLE dbo.GrupIl
(
    grupKod NVARCHAR(32) NOT NULL,
    plaka   INT          NOT NULL,
    sira    INT          NOT NULL CONSTRAINT DF_GrupIl_sira DEFAULT(0),

    CONSTRAINT PK_GrupIl       PRIMARY KEY (grupKod, plaka),
    CONSTRAINT FK_GrupIl_Grup  FOREIGN KEY (grupKod) REFERENCES dbo.Grup(kod) ON DELETE CASCADE,
    CONSTRAINT FK_GrupIl_Il    FOREIGN KEY (plaka)   REFERENCES dbo.Il(plaka)
);
GO

IF OBJECT_ID('dbo.Ittifak') IS NULL
CREATE TABLE dbo.Ittifak
(
    kod          NVARCHAR(32)  NOT NULL CONSTRAINT PK_Ittifak PRIMARY KEY,
    ad           NVARCHAR(128) NOT NULL,
    renk         NVARCHAR(16)  NULL,   -- "r;g;b"
    vizLogoImage NVARCHAR(256) NULL,
    vizBarImage  NVARCHAR(256) NULL
);
GO

/* Referandum secenekleri (EVET / HAYIR). Parti listesini kirletmesin diye ayri. */
IF OBJECT_ID('dbo.Secenek') IS NULL
CREATE TABLE dbo.Secenek
(
    kod         NVARCHAR(32)  NOT NULL CONSTRAINT PK_Secenek PRIMARY KEY,
    ad          NVARCHAR(128) NOT NULL,
    renk        NVARCHAR(16)  NULL,
    vizBarImage NVARCHAR(256) NULL
);
GO

IF OBJECT_ID('dbo.Parti') IS NULL
CREATE TABLE dbo.Parti
(
    kod                 NVARCHAR(32)  NOT NULL CONSTRAINT PK_Parti PRIMARY KEY,
    ad                  NVARCHAR(128) NOT NULL,
    renk                NVARCHAR(16)  NULL,
    ittifakKod          NVARCHAR(32)  NULL,

    vizImage            NVARCHAR(256) NULL,
    vizBarImage         NVARCHAR(256) NULL,
    vizSatirImage       NVARCHAR(256) NULL,
    vizLogoImage        NVARCHAR(256) NULL,
    vizKiyasLogoImage   NVARCHAR(256) NULL,
    vizHaritaSeritImage NVARCHAR(256) NULL,
    vizMvRozetImage     NVARCHAR(256) NULL,

    haritaRenk          NVARCHAR(16)  NULL,
    haritaRenkGenel     NVARCHAR(16)  NULL,
    meclisRenk          NVARCHAR(16)  NULL,

    /* Gercek bir parti degil, artakalan oylarin toplandigi kalem ("DIGER").
       Secim listelerinde cikmamali ama oy toplaminda yerini almali. */
    toplu               BIT           NOT NULL CONSTRAINT DF_Parti_toplu DEFAULT(0),

    CONSTRAINT FK_Parti_Ittifak FOREIGN KEY (ittifakKod) REFERENCES dbo.Ittifak(kod)
);
GO

IF OBJECT_ID('dbo.Aday') IS NULL
CREATE TABLE dbo.Aday
(
    kod                   NVARCHAR(32)  NOT NULL CONSTRAINT PK_Aday PRIMARY KEY,
    ad                    NVARCHAR(128) NOT NULL,
    tamAd                 NVARCHAR(128) NULL,
    partiKod              NVARCHAR(32)  NULL,
    ittifakKod            NVARCHAR(32)  NULL,

    vizImage              NVARCHAR(256) NULL,
    vizBarImage           NVARCHAR(256) NULL,
    vizHaritaImage        NVARCHAR(256) NULL,
    vizSehirImage         NVARCHAR(256) NULL,
    vizTurImage           NVARCHAR(256) NULL,
    vizKarsilastirmaImage NVARCHAR(256) NULL,

    haritaRenk            NVARCHAR(16)  NULL,
    haritaRenkGenel       NVARCHAR(16)  NULL,

    CONSTRAINT FK_Aday_Parti   FOREIGN KEY (partiKod)   REFERENCES dbo.Parti(kod),
    CONSTRAINT FK_Aday_Ittifak FOREIGN KEY (ittifakKod) REFERENCES dbo.Ittifak(kod)
);
GO

/* ---------------------------------------------------------------
   2) SECIMLER
   --------------------------------------------------------------- */

IF OBJECT_ID('dbo.Secim') IS NULL
CREATE TABLE dbo.Secim
(
    kod       NVARCHAR(32)  NOT NULL CONSTRAINT PK_Secim PRIMARY KEY,
    ad        NVARCHAR(128) NOT NULL,

    /* CB / MV / REF */
    tip       NVARCHAR(8)   NOT NULL,
    yil       INT           NOT NULL,

    /* Baraj kurali secime gore degisiyor:
         2023 / 2018 MV -> ITTIFAK, 700   (%7, ittifak toplami uzerinden)
         2015      MV   -> PARTI,  1000   (%10, parti basina, ittifak yoktu)
         CB / REF       -> NULL           (vekil dagitimi yok)
       Kodda sabitlenmesin diye burada duruyor. */
    barajTipi NVARCHAR(8)   NULL CONSTRAINT CK_Secim_barajTipi
                            CHECK (barajTipi IS NULL OR barajTipi IN ('PARTI','ITTIFAK')),
    barajOran INT           NULL,

    sira      INT           NOT NULL CONSTRAINT DF_Secim_sira DEFAULT(0),

    CONSTRAINT CK_Secim_tip CHECK (tip IN ('CB','MV','REF'))
);
GO

/* Bir ilin bir secimdeki sonucu.
   DIKKAT: gecerliOy burada YOK - oylarin toplami olarak hesaplaniyor.
   plaka 0 (TURKIYE) satiri da YOK - API illerden uretiyor. */
IF OBJECT_ID('dbo.IlSonucu') IS NULL
CREATE TABLE dbo.IlSonucu
(
    secimKod     NVARCHAR(32) NOT NULL,
    plaka        INT          NOT NULL,

    /* Yuzde x100: 10000 = %100,00 */
    acilanSandik INT          NOT NULL CONSTRAINT DF_IlSonucu_sandik  DEFAULT(0),
    katilim      INT          NOT NULL CONSTRAINT DF_IlSonucu_katilim DEFAULT(0),

    CONSTRAINT PK_IlSonucu       PRIMARY KEY (secimKod, plaka),
    CONSTRAINT FK_IlSonucu_Secim FOREIGN KEY (secimKod) REFERENCES dbo.Secim(kod) ON DELETE CASCADE,
    CONSTRAINT FK_IlSonucu_Il    FOREIGN KEY (plaka)    REFERENCES dbo.Il(plaka),
    CONSTRAINT CK_IlSonucu_plaka CHECK (plaka <> 0)
);
GO

/* Tek bir kalemin tek bir ildeki ham oyu.
   kod = MV'de parti, CB'de aday, REF'te secenek kodu.
   Uc ayri tabloya isaret ettigi icin foreign key yok; dogrulamayi API yapiyor.
   oran ve vekil BURADA TUTULMUYOR, API hesapliyor. */
IF OBJECT_ID('dbo.Oy') IS NULL
CREATE TABLE dbo.Oy
(
    secimKod NVARCHAR(32) NOT NULL,
    plaka    INT          NOT NULL,
    kod      NVARCHAR(32) NOT NULL,
    oy       BIGINT       NOT NULL CONSTRAINT DF_Oy_oy DEFAULT(0),

    CONSTRAINT PK_Oy          PRIMARY KEY (secimKod, plaka, kod),
    CONSTRAINT FK_Oy_IlSonucu FOREIGN KEY (secimKod, plaka)
                              REFERENCES dbo.IlSonucu(secimKod, plaka) ON DELETE CASCADE,
    CONSTRAINT CK_Oy_oy       CHECK (oy >= 0)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Oy_secim' AND object_id = OBJECT_ID('dbo.Oy'))
    CREATE NONCLUSTERED INDEX IX_Oy_secim ON dbo.Oy(secimKod) INCLUDE(plaka, kod, oy);
GO

/* ---------------------------------------------------------------
   3) SURUM
   Reji uygulamasi 25 saniyede bir /api/surum'u soruyor. Numara
   degismediyse 1,4 MB'lik veriyi hic indirmiyor.
   Elle guncelleme yapilirken unutulmasin diye trigger ile artiyor.
   --------------------------------------------------------------- */

IF OBJECT_ID('dbo.VeriSurum') IS NULL
BEGIN
    CREATE TABLE dbo.VeriSurum
    (
        id          INT           NOT NULL CONSTRAINT PK_VeriSurum PRIMARY KEY
                                  CONSTRAINT CK_VeriSurum_tek CHECK (id = 1),
        surum       INT           NOT NULL,
        guncelleme  DATETIME2(0)  NOT NULL,
        aciklama    NVARCHAR(200) NULL
    );

    INSERT INTO dbo.VeriSurum(id, surum, guncelleme, aciklama)
    VALUES (1, 1, SYSDATETIME(), N'ilk kurulum');
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_SurumArtir
    @aciklama NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.VeriSurum
       SET surum      = surum + 1,
           guncelleme = SYSDATETIME(),
           aciklama   = ISNULL(@aciklama, aciklama)
     WHERE id = 1;
END
GO

/* Veriyi degistiren her tablo surumu artirir; API degisikligi boylece
   25 saniye icinde gorur. */
CREATE OR ALTER TRIGGER dbo.tr_Oy_surum        ON dbo.Oy        AFTER INSERT, UPDATE, DELETE AS EXEC dbo.sp_SurumArtir;
GO
CREATE OR ALTER TRIGGER dbo.tr_IlSonucu_surum  ON dbo.IlSonucu  AFTER INSERT, UPDATE, DELETE AS EXEC dbo.sp_SurumArtir;
GO
CREATE OR ALTER TRIGGER dbo.tr_Secim_surum     ON dbo.Secim     AFTER INSERT, UPDATE, DELETE AS EXEC dbo.sp_SurumArtir;
GO
CREATE OR ALTER TRIGGER dbo.tr_Parti_surum     ON dbo.Parti     AFTER INSERT, UPDATE, DELETE AS EXEC dbo.sp_SurumArtir;
GO
CREATE OR ALTER TRIGGER dbo.tr_Aday_surum      ON dbo.Aday      AFTER INSERT, UPDATE, DELETE AS EXEC dbo.sp_SurumArtir;
GO
CREATE OR ALTER TRIGGER dbo.tr_Ittifak_surum   ON dbo.Ittifak   AFTER INSERT, UPDATE, DELETE AS EXEC dbo.sp_SurumArtir;
GO
CREATE OR ALTER TRIGGER dbo.tr_Secenek_surum   ON dbo.Secenek   AFTER INSERT, UPDATE, DELETE AS EXEC dbo.sp_SurumArtir;
GO
CREATE OR ALTER TRIGGER dbo.tr_Il_surum        ON dbo.Il        AFTER INSERT, UPDATE, DELETE AS EXEC dbo.sp_SurumArtir;
GO
CREATE OR ALTER TRIGGER dbo.tr_Grup_surum      ON dbo.Grup      AFTER INSERT, UPDATE, DELETE AS EXEC dbo.sp_SurumArtir;
GO
CREATE OR ALTER TRIGGER dbo.tr_GrupIl_surum    ON dbo.GrupIl    AFTER INSERT, UPDATE, DELETE AS EXEC dbo.sp_SurumArtir;
GO

PRINT 'HT_SECIM semasi hazir.';
GO
