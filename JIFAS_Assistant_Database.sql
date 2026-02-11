-- ============================================
-- JIFAS Assistant Database Setup
-- Database Name: JIFAS_Assistant
-- Purpose: Knowledge Base + Chat Management
-- ============================================

-- Create Database
CREATE DATABASE JIFAS_Assistant;
GO

USE JIFAS_Assistant;
GO

-- ============================================
-- 1. Knowledge Base Documents Table
-- ============================================
CREATE TABLE KnowledgeBaseDocuments
(
    Id INT PRIMARY KEY IDENTITY(1,1),
    Title NVARCHAR(500) NOT NULL,
    Content NVARCHAR(MAX) NOT NULL,
    Category NVARCHAR(100),
    Tags NVARCHAR(500),
    FilePath NVARCHAR(MAX),
    Embedding NVARCHAR(MAX),  -- JSON array of floats
    EmbeddingDimensions INT DEFAULT 0,
    IsActive BIT DEFAULT 1,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2 DEFAULT GETUTCDATE(),
    ViewCount INT DEFAULT 0,
    RelevanceScore FLOAT DEFAULT 1.0,
    CreatedBy NVARCHAR(100) DEFAULT 'System',
    UpdatedBy NVARCHAR(100) DEFAULT 'System'
);

CREATE INDEX IX_KBDocuments_Category ON KnowledgeBaseDocuments(Category);
CREATE INDEX IX_KBDocuments_IsActive ON KnowledgeBaseDocuments(IsActive);
CREATE INDEX IX_KBDocuments_CreatedAt ON KnowledgeBaseDocuments(CreatedAt);

GO

-- ============================================
-- 2. Knowledge Base Chunks Table
-- (For storing chunked content if needed)
-- ============================================
CREATE TABLE KnowledgeBaseChunks
(
    Id INT PRIMARY KEY IDENTITY(1,1),
    DocumentId INT NOT NULL FOREIGN KEY REFERENCES KnowledgeBaseDocuments(Id) ON DELETE CASCADE,
    ChunkIndex INT NOT NULL,
    Content NVARCHAR(MAX) NOT NULL,
    Embedding NVARCHAR(MAX),  -- JSON array of floats
    EmbeddingDimensions INT DEFAULT 0,
    StartCharPos INT,
    EndCharPos INT,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2 DEFAULT GETUTCDATE()
);

CREATE INDEX IX_KBChunks_DocumentId ON KnowledgeBaseChunks(DocumentId);
CREATE INDEX IX_KBChunks_ChunkIndex ON KnowledgeBaseChunks(ChunkIndex);

GO

-- ============================================
-- 3. Chat Conversations Table
-- ============================================
CREATE TABLE Chats
(
    Id INT PRIMARY KEY IDENTITY(1,1),
    UserId NVARCHAR(100),
    Message NVARCHAR(MAX) NOT NULL,
    Response NVARCHAR(MAX),
    IsOutOfScope BIT DEFAULT 0,
    Confidence FLOAT DEFAULT 0.0,
    RelatedDocumentIds NVARCHAR(MAX),  -- Comma-separated IDs or JSON
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2 DEFAULT GETUTCDATE()
);

CREATE INDEX IX_Chats_UserId ON Chats(UserId);
CREATE INDEX IX_Chats_CreatedAt ON Chats(CreatedAt);

GO

-- ============================================
-- 4. User Feedback Table
-- ============================================
CREATE TABLE UserFeedbacks
(
    Id INT PRIMARY KEY IDENTITY(1,1),
    ChatId INT,
    UserId NVARCHAR(100),
    Rating INT,  -- 1-5 stars
    Comment NVARCHAR(MAX),
    IsHelpful BIT,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE()
);

CREATE INDEX IX_UserFeedbacks_ChatId ON UserFeedbacks(ChatId);
CREATE INDEX IX_UserFeedbacks_CreatedAt ON UserFeedbacks(CreatedAt);

GO

-- ============================================
-- 5. Metrics/Analytics Table
-- ============================================
CREATE TABLE Metrics
(
    Id INT PRIMARY KEY IDENTITY(1,1),
    MetricName NVARCHAR(200) NOT NULL,
    MetricValue FLOAT,
    Category NVARCHAR(100),
    CreatedAt DATETIME2 DEFAULT GETUTCDATE()
);

CREATE INDEX IX_Metrics_MetricName ON Metrics(MetricName);
CREATE INDEX IX_Metrics_CreatedAt ON Metrics(CreatedAt);

GO

-- ============================================
-- 6. Migration History Table
-- (For EF Core migrations tracking)
-- ============================================
CREATE TABLE __EFMigrationsHistory
(
    MigrationId NVARCHAR(150) PRIMARY KEY,
    ProductVersion NVARCHAR(32) NOT NULL
);

GO

-- ============================================
-- Sample Data (Optional)
-- ============================================
-- Insert sample categories
INSERT INTO KnowledgeBaseDocuments (Title, Content, Category, Tags, IsActive, CreatedBy, UpdatedBy)
VALUES 
(
    'Welcome to JIFAS KB',
    'This is the knowledge base for JIFAS Assistant system.',
    'General',
    'jifas,kb,welcome',
    1,
    'System',
    'System'
);

GO

-- ============================================
-- Verification Queries
-- ============================================
-- SELECT * FROM KnowledgeBaseDocuments;
-- SELECT * FROM KnowledgeBaseChunks;
-- SELECT * FROM Chats;
-- SELECT * FROM UserFeedbacks;
-- SELECT * FROM Metrics;
