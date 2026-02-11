# JIFAS Assistant - Clean Setup Guide

## ?? QUICK START

### 1. **Create Database**
```bash
# Run this SQL script in your SQL Server Management Studio (SSMS)
# File: JIFAS_Assistant_Database.sql

# Or use command line:
sqlcmd -S localhost -i JIFAS_Assistant_Database.sql
```

**Database Name:** `JIFAS_Assistant`

**Tables Created:**
- `KnowledgeBaseDocuments` - Main KB documents with embeddings
- `KnowledgeBaseChunks` - Chunked content (for future use)
- `Chats` - Chat conversations
- `UserFeedbacks` - User ratings & feedback
- `Metrics` - Analytics data
- `__EFMigrationsHistory` - EF Core migrations tracking

---

### 2. **Update Connection String**
Edit `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=JIFAS_Assistant;Integrated Security=true;Encrypt=false;"
  }
}
```

---

### 3. **Organize Knowledge Base Files**

Create folder structure:
```
knowledge-base/
??? master/          (Master Data)
?   ??? file1.txt
?   ??? file2.txt
??? pum/             (PUM Category)
?   ??? file3.txt
??? invoice/         (Invoice Category)
?   ??? file4.txt
??? general/         (General Category)
    ??? file5.txt
```

**Key Rules:**
- Files MUST be `.txt` or `.md`
- Folder name determines category (master/ ? "Master Data", pum/ ? "PUM", etc)
- Filenames are used as document titles
- Files are discovered recursively

---

### 4. **Start Application**
```bash
dotnet run
```

App runs on: `http://localhost:5180`

---

## ?? Seed Knowledge Base

### Method 1: **Via API Endpoint**
```bash
# POST request to seed
curl -X POST http://localhost:5180/api/kb/admin/seed

# Response:
{
  "success": true,
  "message": "Seeding completed",
  "results": [
    {
      "success": true,
      "fileName": "file1.txt",
      "documentId": 1,
      "message": "Created"
    }
  ]
}
```

### Method 2: **Via Code**
```csharp
// In your controller or service
var seedingService = app.Services.GetService<IKBSeedingService>();
var results = await seedingService.SeedKnowledgeBaseAsync("./knowledge-base");

foreach (var result in results)
{
    Console.WriteLine($"{result.FileName}: {result.Message}");
}
```

---

## ??? Clear Knowledge Base

```bash
# DELETE request to clear all documents
curl -X DELETE http://localhost:5180/api/kb/admin/clear

# Response:
{
  "success": true,
  "message": "All documents cleared"
}
```

---

## ?? Verify Data

### Query KB Documents via SQL
```sql
-- Check how many documents were seeded
SELECT COUNT(*) as Total FROM KnowledgeBaseDocuments;

-- View all documents
SELECT Id, Title, Category, EmbeddingDimensions, CreatedAt 
FROM KnowledgeBaseDocuments
ORDER BY CreatedAt DESC;

-- Filter by category
SELECT * FROM KnowledgeBaseDocuments 
WHERE Category = 'Master Data';

-- Check embeddings (JSON)
SELECT Title, Category, 
       JSON_ARRAY_LENGTH(Embedding) as EmbeddingSize
FROM KnowledgeBaseDocuments;
```

### Check via API
```bash
# List all KB documents
GET http://localhost:5180/api/kb/documents

# Search KB
GET http://localhost:5180/api/kb/search?query=invoice
```

---

## ?? Configuration

### appsettings.json - KB Section
```json
{
  "KnowledgeBase": {
    "FolderPath": "./knowledge-base",
    "ChunkSize": 512,
    "ChunkOverlap": 50
  }
}
```

### appsettings.json - Gemini Section
```json
{
  "Gemini": {
    "ApiKey": "your-gemini-api-key-here",
    "Model": "gemini-1.5-flash",
    "EmbeddingModel": "models/embedding-001"
  }
}
```

---

## ?? How It Works

### KB Seeding Flow
```
1. Scan knowledge-base/ folder (recursive)
   ?
2. Find all .txt/.md files
   ?
3. For each file:
   a. Read content (UTF-8)
   b. Generate embedding via Gemini API (3072 dimensions)
   c. Auto-detect category from folder name
   d. Save to KnowledgeBaseDocuments table (SQL Server)
   ?
4. Done! Ready for search/chat
```

### Folder ? Category Mapping
```
Folder Name     ? Category
master/         ? Master Data
pum/            ? PUM
invoice/        ? Invoice
payment/        ? Payment
guide/          ? User Guide
report/         ? Reports
faq/            ? Troubleshooting
(others)        ? General
```

---

## ?? Troubleshooting

### Issue: "Knowledge base folder not found"
**Solution:** Make sure `knowledge-base/` folder exists in project root or update `appsettings.json`:
```json
"KnowledgeBase": {
  "FolderPath": "C:/full/path/to/knowledge-base"
}
```

### Issue: "Embedding failed"
**Solution:** 
- Check Gemini API key in `appsettings.json`
- Check internet connection
- Check Gemini API quota

### Issue: "File not found in DB after seeding"
**Solution:**
- Verify SQL Server connection string
- Check database is created: `JIFAS_Assistant`
- Verify embeddings generated (check logs)

### Issue: Files not being discovered
**Solution:**
- Ensure files are `.txt` or `.md` (not `.docx`, `.pdf`, etc)
- Check folder structure is correct
- Run seeding again with API endpoint to see detailed logs

---

## ?? Seeding Service Code

Main service: `Jifas.Assistant/Services/KBSeedingService.cs`

**Key Methods:**
- `SeedKnowledgeBaseAsync(folderPath)` - Seed all files in folder
- `SeedDocumentAsync(filePath)` - Seed single file
- `ClearKnowledgeBaseAsync()` - Delete all documents

**No Qdrant required!** - Everything is in SQL Server only. Simpler and faster.

---

## ? Checklist

- [ ] Database created: `JIFAS_Assistant`
- [ ] Connection string updated in `appsettings.json`
- [ ] Knowledge base folder organized with .txt files
- [ ] Gemini API key configured
- [ ] Application starts without errors
- [ ] KB seeding endpoint tested
- [ ] Documents appear in database

---

## ?? Next Steps

1. ? Database setup complete
2. ? Services simplified (KB seeding only, no Qdrant)
3. ? Run seeding: `POST /api/kb/admin/seed`
4. ? Test search functionality
5. ? Integrate with chatbot

---

**Version:** 1.0 (Simplified)  
**Last Updated:** 2024  
**Status:** Ready to use ?
