-- =============================================================
--  BCPFinAnalytics — Database Setup Script
--  Run this script once against the MRI PMX database.
--  All BCPFinAnalytics tables are prefixed with 'BCPFinAnalytics'
--  to clearly separate them from native MRI tables.
-- =============================================================

-- ── BCPFinAnalyticsSavedSettings ──────────────────────────────
--  Stores user-saved report configurations.
--  The SettingsJson column holds a serialized SavedSettingOptions
--  object — all report options in a single JSON string.
--
--  Design rationale for JSON column:
--    - Variable-length lists (entities, basis selections) stored naturally
--    - Adding new options in future requires no schema migration
--    - One simple row per saved setting
--    - Requires SQL Server 2016+ (JSON support) — standard for MRI PMX 10.5
-- =============================================================

IF NOT EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_NAME = 'BCPFinAnalyticsSavedSettings'
)
BEGIN
    CREATE TABLE BCPFinAnalyticsSavedSettings (
        SettingId    INT           IDENTITY(1,1)  NOT NULL,
        SettingName  VARCHAR(100)                 NOT NULL,
        UserId       VARCHAR(50)                  NOT NULL,
        IsPublic     BIT                          NOT NULL  CONSTRAINT DF_BCPFinAnalyticsSavedSettings_IsPublic     DEFAULT (0),
        CreatedDate  DATETIME                     NOT NULL  CONSTRAINT DF_BCPFinAnalyticsSavedSettings_CreatedDate  DEFAULT (GETDATE()),
        UpdatedDate  DATETIME                     NOT NULL  CONSTRAINT DF_BCPFinAnalyticsSavedSettings_UpdatedDate  DEFAULT (GETDATE()),
        SettingsJson NVARCHAR(MAX)                NOT NULL,

        CONSTRAINT PK_BCPFinAnalyticsSavedSettings PRIMARY KEY CLUSTERED (SettingId ASC)
    );

    -- Index: look up all settings for a given user efficiently
    CREATE NONCLUSTERED INDEX IX_BCPFinAnalyticsSavedSettings_UserId
        ON BCPFinAnalyticsSavedSettings (UserId ASC)
        INCLUDE (SettingName, IsPublic);

    PRINT 'BCPFinAnalyticsSavedSettings table created successfully.';
END
ELSE
BEGIN
    PRINT 'BCPFinAnalyticsSavedSettings table already exists — skipped.';
END
GO
