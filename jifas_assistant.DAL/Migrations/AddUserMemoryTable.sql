-- ============================================================
-- Migration: AddUserMemoryTable
-- Jalankan script ini sekali di database JIFAS Assistant
-- ============================================================

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_NAME = 'UserMemory'
)
BEGIN
    CREATE TABLE [UserMemory] (
        [Id]                        INT             NOT NULL IDENTITY(1,1),
        [UserId]                    NVARCHAR(100)   NOT NULL,
        [FavoriteModules]           NVARCHAR(MAX)   NULL,
        [FrequentTopics]            NVARCHAR(MAX)   NULL,
        [RecentQuestions]           NVARCHAR(MAX)   NULL,
        [ExpertiseLevel]            NVARCHAR(20)    NOT NULL DEFAULT 'Beginner',
        [PreferredLanguage]         NVARCHAR(5)     NOT NULL DEFAULT 'id',
        [DetectedDepartment]        NVARCHAR(100)   NULL,
        [DetectedRole]              NVARCHAR(100)   NULL,
        [TotalSessions]             INT             NOT NULL DEFAULT 0,
        [TotalQuestions]            INT             NOT NULL DEFAULT 0,
        [HowToCount]                INT             NOT NULL DEFAULT 0,
        [TroubleshootingCount]      INT             NOT NULL DEFAULT 0,
        [AverageConfidenceReceived] FLOAT           NOT NULL DEFAULT 0.0,
        [FirstSeenAt]               DATETIME2       NOT NULL DEFAULT GETUTCDATE(),
        [LastSeenAt]                DATETIME2       NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt]                 DATETIME2       NOT NULL DEFAULT GETUTCDATE(),

        CONSTRAINT [PK_UserMemory] PRIMARY KEY ([Id])
    );

    CREATE UNIQUE INDEX [IX_UserMemory_UserId]
        ON [UserMemory] ([UserId]);

    PRINT 'Table UserMemory created successfully.';
END
ELSE
BEGIN
    PRINT 'Table UserMemory already exists — skipped.';
END
