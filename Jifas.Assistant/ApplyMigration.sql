-- JifasAssistant Database Schema Creation Script
-- Created from EF Core Migration: InitialCreate

-- Create Chats table
CREATE TABLE [Chats] (
    [Id] int NOT NULL IDENTITY(1, 1),
    [UserId] nvarchar(255) NOT NULL,
    [UserMessage] nvarchar(500) NOT NULL,
    [AssistantResponse] nvarchar(max) NOT NULL,
    [SessionId] nvarchar(100) NOT NULL,
    [Source] nvarchar(100) NOT NULL,
    [ConfidenceScore] float NULL,
    [IsFromKnowledgeBase] bit NOT NULL,
    [Category] nvarchar(50) NOT NULL,
    [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] datetime2 NULL,
    [Remarks] nvarchar(500) NOT NULL,
    CONSTRAINT [PK_Chats] PRIMARY KEY ([Id])
);

-- Create KnowledgeBaseDocuments table
CREATE TABLE [KnowledgeBaseDocuments] (
    [Id] int NOT NULL IDENTITY(1, 1),
    [Title] nvarchar(500) NOT NULL,
    [Content] nvarchar(max) NOT NULL,
    [Category] nvarchar(100) NOT NULL,
    [Tags] nvarchar(500) NOT NULL,
    [Embedding] nvarchar(max) NOT NULL,
    [EmbeddingDimensions] int NOT NULL,
    [RelevanceScore] float NOT NULL,
    [ViewCount] int NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] datetime2 NULL,
    [CreatedBy] nvarchar(255) NOT NULL,
    [UpdatedBy] nvarchar(255) NOT NULL,
    CONSTRAINT [PK_KnowledgeBaseDocuments] PRIMARY KEY ([Id])
);

-- Create Metrics table
CREATE TABLE [Metrics] (
    [Id] int NOT NULL IDENTITY(1, 1),
    [MetricType] nvarchar(100) NOT NULL,
    [MetricName] nvarchar(255) NOT NULL,
    [Count] int NOT NULL,
    [Value] float NOT NULL,
    [Category] nvarchar(255) NOT NULL,
    [Tags] nvarchar(500) NOT NULL,
    [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] datetime2 NULL,
    CONSTRAINT [PK_Metrics] PRIMARY KEY ([Id])
);

-- Create UserFeedbacks table
CREATE TABLE [UserFeedbacks] (
    [Id] int NOT NULL IDENTITY(1, 1),
    [ChatId] int NOT NULL,
    [UserId] nvarchar(255) NOT NULL,
    [Rating] nvarchar(100) NOT NULL,
    [Comment] nvarchar(max) NOT NULL,
    [IsHelpful] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [PK_UserFeedbacks] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_UserFeedbacks_Chats_ChatId] FOREIGN KEY ([ChatId]) REFERENCES [Chats] ([Id]) ON DELETE CASCADE
);

-- Create indexes
CREATE INDEX [IX_KnowledgeBaseDocuments_Category] ON [KnowledgeBaseDocuments] ([Category]);
CREATE INDEX [IX_KnowledgeBaseDocuments_Title] ON [KnowledgeBaseDocuments] ([Title]);
CREATE INDEX [IX_Metrics_Category] ON [Metrics] ([Category]);
CREATE INDEX [IX_Metrics_MetricType] ON [Metrics] ([MetricType]);
CREATE INDEX [IX_UserFeedbacks_ChatId] ON [UserFeedbacks] ([ChatId]);

-- Insert migration history
INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260211075526_InitialCreate', N'10.0.3');
