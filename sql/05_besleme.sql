/* =====================================================================
   BESLEME

   Ajanstan gelen tam anlik goruntunun ATOMIK olarak uygulanmasi.

   SORUN:
   Veriyi degistiren her tabloda "AFTER INSERT, UPDATE, DELETE" tetikleyicisi
   var ve her biri sp_SurumArtir'i cagiriyor. Yonetim panelinden tek bir
   sayi degistirildiginde bu dogru davranis. Ama besleme 2000 satiri birden
   yaziyor: surum defalarca artar ve daha kotusu, C ekrani tam aradaki bir
   ani yakalayip YARIM UYGULANMIS veriyi ekrana verebilir - Adana yeni,
   Ankara eski.

   COZUM:
   Aktarim suresince surum artisi baskilanir, is bitince surum BIR KEZ
   artirilir. Boylece okuyan taraf ya tamamen eskiyi ya tamamen yeniyi
   gorur, arasi yoktur.

   Baskilama SESSION_CONTEXT ile yapiliyor: yalnizca aktarimi yapan
   baglantiyi etkiler, ayni anda yonetim panelinden yapilan bir degisiklik
   surumu normal sekilde artirmaya devam eder.

   Bu betik on tetikleyiciye dokunmuyor; yalnizca sp_SurumArtir'a bir
   kontrol ekliyor.
   ===================================================================== */

USE HT_SECIM;
GO

/* ---------------------------------------------------------------------
   sp_SurumArtir - baskilama kontrolu eklendi

   Davranis aynen korunuyor; yalnizca "surum_baskila" bayragi acikken
   sessizce cikiyor.
   --------------------------------------------------------------------- */

CREATE OR ALTER PROCEDURE dbo.sp_SurumArtir
    @aciklama NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    /* Toplu aktarim suruyor: her ifadede surum artmasin. */
    IF CAST(ISNULL(SESSION_CONTEXT(N'surum_baskila'), 0) AS BIT) = 1
        RETURN;

    UPDATE dbo.VeriSurum
       SET surum      = surum + 1,
           guncelleme = SYSDATETIME(),
           aciklama   = ISNULL(@aciklama, aciklama)
     WHERE id = 1;
END
GO

/* ---------------------------------------------------------------------
   sp_BeslemeBaslat / sp_BeslemeBitir

   Aktarimi yapan baglanti bunlari cagirir. Bitir, surumu bir kez artirir.

   DIKKAT: Baslat cagrildiktan sonra Bitir MUTLAKA cagrilmali, yoksa o
   baglanti surum artirmaz hale gelir. Bagglanti havuzda tekrar
   kullanildiginda bu sessiz bir arizaya donusur; C# tarafi bunu
   try/finally icinde yapiyor.
   --------------------------------------------------------------------- */

CREATE OR ALTER PROCEDURE dbo.sp_BeslemeBaslat
AS
BEGIN
    SET NOCOUNT ON;
    EXEC sys.sp_set_session_context @key = N'surum_baskila', @value = 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BeslemeBitir
    @aciklama NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    EXEC sys.sp_set_session_context @key = N'surum_baskila', @value = 0;

    EXEC dbo.sp_SurumArtir @aciklama;
END
GO

/* ---------------------------------------------------------------------
   Besleme gecmisi

   Gelen her dosyanin sonucu: kabul mu red mi, kac il, neden reddedildi.
   Saglik ekrani buradan besleniyor - "son dosya ne zaman geldi, kabul
   edildi mi" sorusunun cevabi tek sorguda.

   Reddedilen dosyalar da yaziliyor: yayin gecesi "ajans veri gonderiyor
   ama ekranda degismiyor" durumunun tek aciklamasi burasi olur.
   --------------------------------------------------------------------- */

IF OBJECT_ID('dbo.BeslemeGecmis') IS NULL
BEGIN
    CREATE TABLE dbo.BeslemeGecmis
    (
        id         BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BeslemeGecmis PRIMARY KEY,
        zaman      DATETIME2(0)  NOT NULL CONSTRAINT DF_BeslemeGecmis_zaman DEFAULT(SYSDATETIME()),
        kaynak     NVARCHAR(64)  NOT NULL,
        dosya      NVARCHAR(200) NULL,
        sira       BIGINT        NOT NULL CONSTRAINT DF_BeslemeGecmis_sira DEFAULT(0),
        kabul      BIT           NOT NULL,
        ilSayisi   INT           NOT NULL CONSTRAINT DF_BeslemeGecmis_il DEFAULT(0),
        oySatiri   INT           NOT NULL CONSTRAINT DF_BeslemeGecmis_oy DEFAULT(0),
        aciklama   NVARCHAR(500) NULL
    );

    CREATE NONCLUSTERED INDEX IX_BeslemeGecmis_zaman ON dbo.BeslemeGecmis(zaman DESC);
END
GO

/* BeslemeGecmis'e tetikleyici KOYULMUYOR: besleme gecmisi veri degil,
   gunluk. Yazilmasi surumu artirmamali, yoksa her red bile C ekranini
   bosuna veri indirmeye zorlar. */
GO

PRINT '05_besleme.sql tamam.';
GO
