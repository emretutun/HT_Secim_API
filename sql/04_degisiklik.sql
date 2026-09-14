/* =====================================================================
   DEGISIKLIK KAYDI

   Veriye kim, ne zaman, neyi, hangi degerden hangi degere cevirdi.

   Canli yayinda ekrana yanlis bir sayi dustugunde tek sorulan sey
   "bunu kim ne zaman girdi" olur. Kayit tutulmazsa cevap yoktur.
   Yonetim panelinden yapilan her degisiklik ve beslemeden gelen her
   guncelleme buraya yaziliyor.
   ===================================================================== */

USE HT_SECIM;
GO

IF OBJECT_ID('dbo.Degisiklik') IS NULL
CREATE TABLE dbo.Degisiklik
(
    id        BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Degisiklik PRIMARY KEY,
    zaman     DATETIME2(0)  NOT NULL CONSTRAINT DF_Degisiklik_zaman DEFAULT(SYSDATETIME()),

    /* YONETIM = panelden elle, BESLEME = YSK/AA beslemesinden */
    kaynak    NVARCHAR(16)  NOT NULL,

    /* Panelde girilen operator adi; besleme icin servis adi. */
    kullanici NVARCHAR(64)  NULL,

    secimKod  NVARCHAR(32)  NOT NULL,
    plaka     INT           NOT NULL,

    /* "oy:CHP", "acilanSandik", "gecersizOy" ... */
    alan      NVARCHAR(64)  NOT NULL,

    eski      BIGINT        NULL,
    yeni      BIGINT        NULL,

    aciklama  NVARCHAR(200) NULL
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Degisiklik_zaman' AND object_id = OBJECT_ID('dbo.Degisiklik'))
    CREATE NONCLUSTERED INDEX IX_Degisiklik_zaman ON dbo.Degisiklik(zaman DESC)
        INCLUDE(kaynak, kullanici, secimKod, plaka, alan, eski, yeni);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Degisiklik_il' AND object_id = OBJECT_ID('dbo.Degisiklik'))
    CREATE NONCLUSTERED INDEX IX_Degisiklik_il ON dbo.Degisiklik(secimKod, plaka, zaman DESC);
GO

PRINT 'Degisiklik tablosu hazir.';
GO
