# ?? Knowledge Base Seeding Guide

## Overview

Simple KB seeding sistem untuk load documents dari Markdown files ke database dengan embeddings.

```
FLOW:
????????????????     ????????????????????     ????????????????
?  MD Files    ??????? Generate         ??????? SQL Server   ?
? knowledge-   ?     ? Embedding        ?     ? KnowledgeBase?
? base/ folder ?     ? (Gemini API)     ?     ? Documents    ?
????????????????     ????????????????????     ????????????????
```

---

## ?? Setup Knowledge Base Folder

Create folder structure:

```bash
# Create knowledge base directory
mkdir knowledge-base

# Add your MD files
cd knowledge-base
# Place your markdown files here:
# - master-data.md
# - user-guide.md
# - invoice-procedures.md
# - payment-guide.md
# - troubleshooting-faq.md
```

Example document: `knowledge-base/user-guide.md`

```markdown
# JIFAS User Guide

## Introduction
JIFAS is a comprehensive accounting system...

## Getting Started
To access JIFAS, follow these steps:
1. Navigate to login page
2. Enter credentials
3. Click login

## Modules
- Accounts Receivable (AR)
- Accounts Payable (AP)
- General Ledger (GL)
```

---

## ?? Configuration

Update `appsettings.json`:

```json
{
  "KnowledgeBase": {
    "FolderPath": "./knowledge-base",
    "EnableChunking": true,
    "ChunkSize": 512,
    "ChunkOverlap": 50
  }
}
```

---

## ?? API Endpoints

### 1. **Seed Knowledge Base** (Load MD files)

```bash
# POST /api/kb/admin/seed
curl -X POST http://localhost:5180/api/kb/admin/seed

# With custom folder path
curl -X POST "http://localhost:5180/api/kb/admin/seed?folderPath=/custom/path"
```

**Response:**
```json
{
  "total": 3,
  "success": 3,
  "failed": 0,
  "documents": [
    {
      "fileName": "user-guide.md",
      "success": true,
      "documentId": 1,
      "message": "Saved to database"
    },
    {
      "fileName": "master-data.md",
      "success": true,
      "documentId": 2,
      "message": "Saved to database"
    }
  ]
}
```

### 2. **Clear Knowledge Base** (Delete all documents)

```bash
# DELETE /api/kb/admin/clear
curl -X DELETE http://localhost:5180/api/kb/admin/clear
```

**Response:**
```json
{
  "success": true,
  "message": "Knowledge base cleared"
}
```

### 3. **List Documents**

```bash
curl http://localhost:5180/api/kb/documents
```

### 4. **Search Knowledge Base**

```bash
curl "http://localhost:5180/api/kb/search?query=how+to+create+invoice&topK=5"
```

---

## ?? Database Schema

Documents are stored in `KnowledgeBaseDocuments` table:

```sql
SELECT TOP (1000) [Id]
      ,[Title]
      ,[Content]
      ,[Category]
      ,[Tags]
      ,[Embedding]          -- Stored as JSON (3072 dimensions)
      ,[EmbeddingDimensions]
      ,[IsActive]
      ,[CreatedAt]
      ,[UpdatedAt]
      ,[ViewCount]
      ,[RelevanceScore]
      ,[CreatedBy]
      ,[UpdatedBy]
  FROM [JifasAssistant].[dbo].[KnowledgeBaseDocuments]
```

---

## ?? Seeding Flow Explained

### Step-by-Step:

**1. Read MD File**
```csharp
var content = await File.ReadAllTextAsync(filePath);
// Reads full document content
```

**2. Generate Embedding (Gemini API)**
```csharp
var embedding = await _embeddingService.GenerateEmbeddingAsync(content);
// Returns List<float> with 3072 dimensions
```

**3. Store to Database**
```sql
INSERT INTO KnowledgeBaseDocuments (
  Title, Content, Category, Tags,
  Embedding, EmbeddingDimensions,
  IsActive, CreatedAt, UpdatedAt,
  ViewCount, RelevanceScore,
  CreatedBy, UpdatedBy
) VALUES (
  @Title, @Content, @Category, @Tags,
  @Embedding, 3072, 1, GETUTCDATE(),
  GETUTCDATE(), 0, 1.0, 'System', 'System'
);
```

---

## ?? Understanding the Result

When seeding, each document:

```json
{
  "fileName": "master-data.md",          // Original file name
  "success": true,                        // Seeding successful
  "documentId": 1,                        // Database record ID
  "message": "Saved to database"          // Status message
}
```

**Document in Database:**
```
ID: 1
Title: master-data
Content: [Full markdown content]
Category: Master Data  (auto-detected from filename)
Tags: jifas,kb,master
Embedding: [3072 float values as JSON]
EmbeddingDimensions: 3072
IsActive: true
CreatedAt: 2026-02-11T08:20:00Z
CreatedBy: System
```

---

## ?? Chunking Strategy

**Current Approach: NO Chunking**

Why?
- Simple & maintainable
- Fast processing
- Works well for most documents
- Embeddings stored in SQL Server (searchable)

**Flow:**
```
Document ? One Embedding ? Database
```

**Alternative (Future):**
```
Document ? Split into Chunks ? Embed each chunk ? Database
```

For now: **1 Document = 1 Embedding (3072 dims)**

---

## ?? Where Embeddings Are Stored

### Option 1: **SQL Server** (Current)
? Embeddings stored as JSON in `Embedding` column
? Full-text search capability
? Searchable via REST API
? No external dependencies

### Option 2: **Qdrant** (Optional)
? Fast vector similarity search
? Separate vector DB
? For production scale

**Currently:** Using **SQL Server only**

---

## ?? Document Categories (Auto-detected)

| Filename Pattern | Category |
|--|--|
| `*master*` | Master Data |
| `*guide*` or `*user*` | User Guide |
| `*invoice*` | Invoice |
| `*payment*` | Payment |
| `*report*` | Reports |
| `*troubleshoot*` or `*faq*` | Troubleshooting |
| _(other)_ | General |

---

## ?? How It Works: Services

### 1. **IKBSeedingService**
- `SeedKnowledgeBaseAsync()` - Batch load all MD files
- `SeedDocumentAsync()` - Load single MD file
- `ClearKnowledgeBaseAsync()` - Delete all documents

### 2. **GeminiEmbeddingService** (IEmbeddingService)
- `GenerateEmbeddingAsync()` - Create embedding vector
- `GenerateBatchEmbeddingsAsync()` - Batch embeddings

### 3. **JifasAssistantDbContext**
- Saves documents to `KnowledgeBaseDocuments` table

---

## ?? Integration

**Registered in Program.cs:**
```csharp
builder.Services.AddScoped<IKBSeedingService, KBSeedingService>();
```

**Injected in KnowledgeBaseController:**
```csharp
private readonly IKBSeedingService _seedingService;

// POST /api/kb/admin/seed
public async Task<IActionResult> SeedKnowledgeBase(string folderPath = "")
{
    var results = await _seedingService.SeedKnowledgeBaseAsync(folderPath);
    return Ok(results);
}
```

---

## ?? Testing

### Test 1: Seed Documents
```bash
# 1. Create knowledge-base folder with MD files
mkdir knowledge-base
echo "# Test Document" > knowledge-base/test.md

# 2. Seed
curl -X POST http://localhost:5180/api/kb/admin/seed

# 3. Verify - check response, should show documentId
```

### Test 2: List Documents
```bash
curl http://localhost:5180/api/kb/documents

# Should return array with embedded documents
```

### Test 3: Search
```bash
curl "http://localhost:5180/api/kb/search?query=test&topK=5"

# Should return matching documents
```

### Test 4: Clear
```bash
curl -X DELETE http://localhost:5180/api/kb/admin/clear

# Should return success

# Verify empty
curl http://localhost:5180/api/kb/documents
# Should return empty array []
```

---

## ?? Database Verification

```sql
-- Check seeded documents
SELECT COUNT(*) FROM KnowledgeBaseDocuments;

-- View document details
SELECT Id, Title, Category, EmbeddingDimensions, IsActive, CreatedAt
FROM KnowledgeBaseDocuments;

-- Check embedding size
SELECT Id, Title, LEN(Embedding) as EmbeddingSize
FROM KnowledgeBaseDocuments;
```

---

## ?? Troubleshooting

| Issue | Solution |
|-------|----------|
| "Folder not found" | Create `./knowledge-base` folder |
| "Embedding failed" | Check Gemini API key in appsettings |
| "Database error" | Ensure SQL Server is running |
| "No documents returned" | Run seeding endpoint first |

---

## ? Summary

```
SIMPLE KB SEEDING:
1. Place MD files in knowledge-base/ folder
2. Call POST /api/kb/admin/seed
3. Embeddings generated (Gemini API)
4. Documents stored in SQL Server
5. Available for search via /api/kb/search

NO CHUNKING: Entire document = 1 embedding (3072 dims)
STORAGE: SQL Server (KnowledgeBaseDocuments table)
OPTIONAL: Push to Qdrant for vector similarity
```

---

**Status**: ? Ready to use
**Created**: Feb 11, 2026
**Version**: 2.0.0
