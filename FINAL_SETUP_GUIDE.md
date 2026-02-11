# JIFAS Assistant - FINAL SETUP (Step-by-Step)

**Status:** Database deleted ?  
**Goal:** Fresh, clean setup dengan Gemini Embeddings  

---

## ?? STEP 1: Create Database

### Option A: Using SSMS (SQL Server Management Studio)

1. Open **SSMS**
2. Connect to your SQL Server instance
3. **New Query** ? Copy content dari `JIFAS_Assistant_Database.sql`
4. **Execute** (F5)
5. Verify: Database `JIFAS_Assistant` created ?

### Option B: Using Command Line

```bash
sqlcmd -S localhost -U sa -P YourPassword -i JIFAS_Assistant_Database.sql
```

### Option C: Using dotnet CLI (Manual)

```bash
# Connect to SQL Server
sqlcmd -S localhost

# In sqlcmd prompt:
1> CREATE DATABASE JIFAS_Assistant;
2> GO
3> USE JIFAS_Assistant;
4> GO
# (then copy/paste table definitions from SQL file)
```

**Result:** ? Database dengan 6 tables ready

---

## ?? STEP 2: Update Connection String

Edit **`appsettings.json`**:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=JIFAS_Assistant;Integrated Security=true;Encrypt=false;TrustServerCertificate=true;"
  },
  
  "Gemini": {
    "ApiKey": "PASTE_YOUR_GEMINI_API_KEY_HERE",
    "Model": "gemini-1.5-flash",
    "EmbeddingModel": "models/embedding-001"
  },
  
  "KnowledgeBase": {
    "FolderPath": "./knowledge-base"
  },
  
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

**Note:** 
- Replace `PASTE_YOUR_GEMINI_API_KEY_HERE` with actual key
- SQL Server connection string: adjust hostname if needed

---

## ?? STEP 3: Organize Knowledge Base Files

Create folder structure:

```
Project Root/
??? knowledge-base/
    ??? master/
    ?   ??? master_data_1.txt
    ?   ??? master_data_2.txt
    ?   ??? ...
    ??? pum/
    ?   ??? pum_guide.txt
    ?   ??? ...
    ??? invoice/
    ?   ??? invoice_template.txt
    ?   ??? ...
    ??? general/
        ??? other_docs.txt
```

**Important:**
- Files MUST be `.txt` or `.md`
- Folder name = Category (master/ ? "Master Data", etc)
- Content can be any text (plain text, markdown, JSON, etc)

---

## ?? STEP 4: Run Application

```bash
# From project root
dotnet run

# Or in Visual Studio
# F5 (Debug) or Ctrl+F5 (Release)
```

**Expected Output:**
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5180
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

? App is running!

---

## ?? STEP 5: Seed Knowledge Base

### Option A: Via API (Recommended)

```bash
# Terminal/PowerShell
curl -X POST http://localhost:5180/api/kb/admin/seed

# Or using VS Code REST Client:
POST http://localhost:5180/api/kb/admin/seed
```

**Response:**
```json
{
  "success": true,
  "results": [
    {
      "success": true,
      "fileName": "master_data_1.txt",
      "documentId": 1,
      "message": "Created"
    },
    {
      "success": true,
      "fileName": "invoice_template.txt",
      "documentId": 2,
      "message": "Created"
    }
  ]
}
```

### Option B: Programmatically

```csharp
// In any controller/service
public class TestController : ControllerBase
{
    private readonly IKBSeedingService _seeding;
    
    public TestController(IKBSeedingService seeding)
    {
        _seeding = seeding;
    }
    
    [HttpPost("test-seed")]
    public async Task<IActionResult> TestSeed()
    {
        var results = await _seeding.SeedKnowledgeBaseAsync();
        return Ok(results);
    }
}
```

? All files seeded!

---

## ?? STEP 6: Verify Data in Database

### Via SQL Server

```sql
-- Check total documents
SELECT COUNT(*) as TotalDocuments FROM KnowledgeBaseDocuments;

-- View all documents
SELECT 
    Id,
    Title,
    Category,
    Tags,
    EmbeddingDimensions,
    CreatedAt
FROM KnowledgeBaseDocuments
ORDER BY CreatedAt DESC;

-- Check specific category
SELECT * FROM KnowledgeBaseDocuments 
WHERE Category = 'Master Data';

-- View embedding (first 100 chars)
SELECT 
    Title,
    Category,
    SUBSTRING(Embedding, 1, 100) as EmbeddingPreview,
    EmbeddingDimensions
FROM KnowledgeBaseDocuments
LIMIT 5;
```

### Via API

```bash
# List all documents
GET http://localhost:5180/api/kb/documents

# Search
GET http://localhost:5180/api/kb/search?query=invoice

# Response should show documents with embeddings
```

? Data is there!

---

## ?? STEP 7: Cleanup Extra Services (Optional)

If you want to clean up unused services:

1. Read **`CLEANUP_GUIDE.md`**
2. Delete ~10 unused service files
3. Update `Program.cs` to remove their registrations
4. Build & test

```bash
dotnet clean
dotnet build
dotnet run
```

---

## ?? STEP 8: Verify Everything

Run these checks:

```bash
# 1. Application starts
dotnet run
# Should see: "Now listening on: http://localhost:5180"

# 2. Health check
curl http://localhost:5180/api/health
# Should return 200 OK

# 3. List KB documents
curl http://localhost:5180/api/kb/documents
# Should return list of documents

# 4. Database has data
sqlcmd -S localhost -d JIFAS_Assistant -Q "SELECT COUNT(*) FROM KnowledgeBaseDocuments"
# Should show your document count > 0
```

? Everything works!

---

## ?? QUICK REFERENCE

### Create Database
```bash
# Run SQL script
sqlcmd -S localhost -i JIFAS_Assistant_Database.sql
```

### Seed KB
```bash
curl -X POST http://localhost:5180/api/kb/admin/seed
```

### Clear KB
```bash
curl -X DELETE http://localhost:5180/api/kb/admin/clear
```

### Check Data
```sql
SELECT * FROM KnowledgeBaseDocuments;
```

### Verify Embeddings
```sql
SELECT Title, EmbeddingDimensions FROM KnowledgeBaseDocuments;
-- Should show EmbeddingDimensions = 3072 (Gemini default)
```

---

## ?? Troubleshooting

| Problem | Solution |
|---------|----------|
| "Cannot connect to database" | Check connection string in appsettings.json, verify SQL Server running |
| "Embedding failed" | Check Gemini API key, verify internet connection |
| "Files not found" | Verify knowledge-base/ folder exists with .txt/.md files |
| "Duplicate key error" | Run `DELETE FROM KnowledgeBaseDocuments` first, then reseed |
| "Build errors" | Run `dotnet clean && dotnet build` |
| "Port 5180 in use" | Change port in launchSettings.json or kill process on port |

---

## ?? Checklist

Complete in order:

- [ ] Database created (`JIFAS_Assistant`)
- [ ] All 6 tables exist (verified with SQL)
- [ ] Connection string updated
- [ ] Gemini API key in appsettings.json
- [ ] knowledge-base/ folder organized
- [ ] Application starts: `dotnet run`
- [ ] Health check responds: `/api/health`
- [ ] KB seeding works: `POST /api/kb/admin/seed`
- [ ] Documents in database: `SELECT COUNT(*) FROM KnowledgeBaseDocuments`
- [ ] Embeddings generated (3072 dimensions)
- [ ] Search works: `/api/kb/search?query=test`

---

## ? DONE!

Your JIFAS Assistant is ready:
- ? Clean database
- ? Gemini embeddings
- ? SQL Server storage
- ? Simplified services
- ? Ready for chat/search

---

## ?? Next Steps

1. **Test Chat API:** `POST /api/chat`
2. **Integrate with Frontend:** Connect to your UI
3. **Customize:** Adjust services as needed
4. **Deploy:** To your server/cloud

---

**Total Setup Time:** ~15 minutes ??  
**Difficulty:** Easy ?  
**Status:** Ready to use ??
