-- ============================================================
-- Migration: Add AiUsageLogs table
-- Purpose : Persist every Ollama AI call for monitoring dashboard
-- Run     : Execute once against the JIFAS_Assistant database
-- ============================================================

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_NAME = 'AiUsageLogs'
)
BEGIN
    CREATE TABLE [dbo].[AiUsageLogs] (
        [Id]                   BIGINT         IDENTITY(1,1) NOT NULL,

        -- Identity
        [UserId]               NVARCHAR(200)  NULL,
        [SessionId]            NVARCHAR(200)  NULL,
        [ActiveModule]         NVARCHAR(100)  NULL,

        -- Model info
        [Model]                NVARCHAR(100)  NOT NULL CONSTRAINT DF_AiUsageLogs_Model DEFAULT ('qwen3:8b'),
        [CallType]             NVARCHAR(50)   NULL,   -- 'chat' | 'suggestions' | 'scope_check'

        -- Token counts (from Ollama eval_count fields)
        [PromptTokens]         INT            NOT NULL CONSTRAINT DF_AiUsageLogs_PromptTokens    DEFAULT 0,
        [CompletionTokens]     INT            NOT NULL CONSTRAINT DF_AiUsageLogs_CompletionTokens DEFAULT 0,
        [TotalTokens]          INT            NOT NULL CONSTRAINT DF_AiUsageLogs_TotalTokens     DEFAULT 0,

        -- Timing (milliseconds)
        [TotalDurationMs]      BIGINT         NOT NULL CONSTRAINT DF_AiUsageLogs_TotalDurationMs      DEFAULT 0,
        [LoadDurationMs]       BIGINT         NOT NULL CONSTRAINT DF_AiUsageLogs_LoadDurationMs       DEFAULT 0,
        [PromptEvalDurationMs] BIGINT         NOT NULL CONSTRAINT DF_AiUsageLogs_PromptEvalDurationMs DEFAULT 0,
        [EvalDurationMs]       BIGINT         NOT NULL CONSTRAINT DF_AiUsageLogs_EvalDurationMs       DEFAULT 0,

        -- Throughput
        [TokensPerSecond]      FLOAT          NOT NULL CONSTRAINT DF_AiUsageLogs_TokensPerSecond DEFAULT 0,

        -- Quality signals
        [ResponseLengthChars]  INT            NOT NULL CONSTRAINT DF_AiUsageLogs_ResponseLength  DEFAULT 0,
        [PromptLengthChars]    INT            NOT NULL CONSTRAINT DF_AiUsageLogs_PromptLength    DEFAULT 0,
        [ConfidenceScore]      FLOAT          NULL,
        [IsError]              BIT            NOT NULL CONSTRAINT DF_AiUsageLogs_IsError DEFAULT 0,
        [ErrorMessage]         NVARCHAR(500)  NULL,

        -- Timestamp
        [CreatedAt]            DATETIME2      NOT NULL CONSTRAINT DF_AiUsageLogs_CreatedAt DEFAULT (GETUTCDATE()),

        CONSTRAINT PK_AiUsageLogs PRIMARY KEY CLUSTERED ([Id] ASC)
    );

    CREATE INDEX IX_AiUsageLog_CreatedAt ON [dbo].[AiUsageLogs] ([CreatedAt] DESC);
    CREATE INDEX IX_AiUsageLog_UserId    ON [dbo].[AiUsageLogs] ([UserId]);
    CREATE INDEX IX_AiUsageLog_Model     ON [dbo].[AiUsageLogs] ([Model]);

    PRINT 'Table AiUsageLogs created successfully.';
END
ELSE
BEGIN
    PRINT 'Table AiUsageLogs already exists — skipped.';
END
GO
