# 🚀 SETUP.md - Complete Installation & Configuration Guide

**JIFAS AI Assistant - Production Ready Setup Instructions**

---

## 📋 Table of Contents

1. [System Requirements](#system-requirements)
2. [Installation Steps](#installation-steps)
3. [Environment Configuration](#environment-configuration)
4. [Database Setup](#database-setup)
5. [Running the Application](#running-the-application)
6. [Verification & Testing](#verification--testing)
7. [Docker Deployment](#docker-deployment)
8. [Production Checklist](#production-checklist)
9. [Troubleshooting](#troubleshooting)

---

## 💻 System Requirements

### Minimum Requirements
| Component | Version | Link |
|-----------|---------|------|
| **.NET SDK** | 10.0+ | https://dotnet.microsoft.com/download |
| **SQL Server** | 2019+ or LocalDB | Included with Visual Studio |
| **Git** | Latest | https://git-scm.com |
| **RAM** | 4GB | Minimum for development |
| **Disk Space** | 2GB | For SDK, runtime, dependencies |

### Recommended Setup
- **OS**: Windows 11, macOS 13+, or Ubuntu 22.04+
- **IDE**: Visual Studio 2024 or VS Code
- **RAM**: 8GB+ for comfortable development
- **Internet**: For NuGet packages & Google API

### API Requirements
- **Google Gemini API Key** (FREE) - Get from https://ai.google.dev
  - No credit card required for free tier
  - 50 requests/min limit on free tier
  - Enough for most use cases

---

## 📥 Installation Steps

### Step 1: Clone Repository

```bash
# Clone the project
git clone https://github.com/your-org/jifas-assistant.git
cd jifas-assistant

# Or if already cloned, update
git pull origin main
```

### Step 2: Verify .NET Installation

```bash
# Check .NET version
dotnet --version
# Should output 10.0.X or higher

# Check installed SDKs
dotnet --list-sdks

# Check runtime versions
dotnet --list-runtimes
```

### Step 3: Restore NuGet Packages

```bash
cd jifas-assistant

# Restore all dependencies
dotnet restore

# Expected: Downloaded 100+ packages
# Time: 2-5 minutes depending on internet
```

### Step 4: Verify Project Structure

```bash
# List main projects
ls -la *.csproj
ls -la */

# Expected folders:
# - Jifas.Assistant/
# - jifas_assistant.DAL/
# - jifas_assistant.Seeding/
```

---

## 🔐 Environment Configuration

### Option A: User Secrets (RECOMMENDED for Development)

**Why?** Secrets stored locally, never committed to git, easy to manage.

```bash
# Navigate to API project
cd Jifas.Assistant

# Initialize user secrets (one-time)
dotnet user-secrets init
# Creates: %APPDATA%\Microsoft\UserSecrets\{id}\secrets.json

# Set your Google Gemini API key
dotnet user-secrets set "Gemini:ApiKey" "YOUR_ACTUAL_GOOGLE_API_KEY"

# Set database connection (optional, uses default LocalDB)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\\MSSQLLocalDB;Database=JIFAS_Assistant;Trusted_Connection=true;Encrypt=false"

# Verify secrets are set
dotnet user-secrets list

# Output should show:
# Gemini:ApiKey = YOUR_ACTUAL_GOOGLE_API_KEY
# ConnectionStrings:DefaultConnection = Server=...
```

**Where are secrets stored?**
- Windows: `%APPDATA%\Microsoft\UserSecrets\b375d8c9-0269-41b2-bece-00556711b0b1\secrets.json`
- macOS: `~/.microsoft/usersecrets/b375d8c9-0269-41b2-bece-00556711b0b1/secrets.json`
- Linux: `~/.microsoft/usersecrets/b375d8c9-0269-41b2-bece-00556711b0b1/secrets.json`

**Why hidden?** Never version controlled, secure, local to developer only.

### Option B: Environment Variables

**Windows PowerShell:**
```powershell
# Set environment variables
$env:Gemini__ApiKey = "YOUR_ACTUAL_API_KEY"
$env:ConnectionStrings__DefaultConnection = "Server=..."
$env:ASPNETCORE_ENVIRONMENT = "Development"

# Verify
$env:Gemini__ApiKey  # Should show your key

# Run app
dotnet run
```

**Linux/macOS:**
```bash
export Gemini__ApiKey="YOUR_ACTUAL_API_KEY"
export ConnectionStrings__DefaultConnection="Server=..."
export ASPNETCORE_ENVIRONMENT="Development"

dotnet run
```

### Option C: appsettings.json (Development Only)

⚠️ **WARNING**: Never commit actual secrets!

```json
{
  "Gemini": {
    "ApiKey": "YOUR_ACTUAL_API_KEY",
    "Model": "gemini-2.0-flash"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=JIFAS_Assistant;Trusted_Connection=true;Encrypt=false"
  }
}
```

Then in .gitignore, ensure secrets are ignored.

### Configuration Reference

| Setting | Default | Purpose |
|---------|---------|---------|
| `Gemini:ApiKey` | (required) | Google Gemini API authentication |
| `Gemini:Model` | `gemini-2.0-flash` | LLM model to use |
| `Gemini:BaseUrl` | `https://generativelanguage.googleapis.com/v1beta/models` | API endpoint |
| `ConnectionStrings:DefaultConnection` | LocalDB | Database connection |
| `Logging:LogLevel:Default` | `Information` | Default log level |
| `Caching:EnableResponseCache` | `true` | Cache responses |
| `Caching:ResponseCacheDurationHours` | `24` | Cache TTL |
| `Qdrant:Enabled` | `false` | Enable vector DB |
| `Qdrant:Url` | `http://localhost:6333` | Vector DB endpoint |

---

## 💾 Database Setup

### Prerequisites
- SQL Server 2019+ OR LocalDB (free, included with Visual Studio)

### Step 1: Create Database (Auto via Migrations)

```bash
cd Jifas.Assistant

# Apply Entity Framework migrations
# This creates tables, indexes, constraints
dotnet ef database update

# Expected output:
# Build started...
# Applying migration 001_Initial
# Applying migration 002_AddEmbeddings
# Done.
# Database updated successfully.
```

### Step 2: Seed Sample Data (Optional)

```bash
# Navigate to seeding project
cd ../jifas_assistant.Seeding

# Run seeding
dotnet run

# This will populate:
# - Sample KB documents
# - Categorized content
# - Test data for development
```

### Step 3: Verify Database

**Option A: SQL Server Management Studio**
1. Connect to `(localdb)\MSSQLLocalDB`
2. Database: `JIFAS_Assistant`
3. Tables: Chats, KnowledgeBaseDocuments, KnowledgeBaseChunks, Metrics, UserFeedbacks

**Option B: Command Line**
```bash
# Using dotnet ef
dotnet ef dbcontext info

# Should show:
# Database name: JIFAS_Assistant
# Provider: Microsoft.EntityFrameworkCore.SqlServer
```

### Step 4: Custom Connection String (If Not Using LocalDB)

For SQL Server on network:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SERVER_IP,1433;Database=JIFAS_Assistant;User Id=sa;Password=YourStrongPassword;TrustServerCertificate=true;"
  }
}
```

Or via user secrets:
```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=192.168.1.100,1433;Database=JIFAS_Assistant;User Id=sa;Password=YourPassword;TrustServerCertificate=true;"
```

---

## ▶️ Running the Application

### Local Development

```bash
# Navigate to API project
cd Jifas.Assistant

# Build solution
dotnet build

# Run with debug info
dotnet run --configuration Debug

# Expected output:
# Build succeeded with X warning(s)
# info: Microsoft.Hosting.Lifetime[14]
#       Now listening on: http://localhost:5000
#       Now listening on: https://localhost:5001
#       Application started. Press Ctrl+C to exit
```

### Access the API

```bash
# In another terminal, test the API

# 1. Health check
curl http://localhost:5000/health

# Expected:
# {"status":"Healthy","database":"Connected"}

# 2. Simple chat
curl -X POST http://localhost:5000/api/chatbot \
  -H "Content-Type: application/json" \
  -d '{
    "message": "Apa itu JIFAS?",
    "userId": "test-user",
    "sessionId": "test-session"
  }'

# Expected response with AI answer
```

### Using Visual Studio

1. Open `jifas-assistant.slnx`
2. Right-click Solution → Set Startup Project → `Jifas.Assistant`
3. Press F5 or Debug → Start Debugging
4. API launches at `http://localhost:5000`

### Using VS Code

```bash
# Install C# extension first
# Then in VS Code:
# 1. Open folder
# 2. Select Jifas.Assistant as default project
# 3. Press F5 to debug
```

---

## ✅ Verification & Testing

### Test 1: API Health Check

```bash
curl http://localhost:5000/health

# Expected:
# {"status":"Healthy","database":"Connected"}
```

### Test 2: List KB Documents

```bash
curl http://localhost:5000/api/kb/documents

# Expected:
# [
#   {
#     "id": 1,
#     "title": "JIFAS User Guide",
#     "category": "Getting Started",
#     "isActive": true
#   }
# ]
```

### Test 3: Chat with KB

```bash
curl -X POST http://localhost:5000/api/chatbot \
  -H "Content-Type: application/json" \
  -d '{
    "message": "How to login?",
    "userId": "dev-user",
    "sessionId": ""
  }'

# Expected:
# {
#   "message": "To login to JIFAS...",
#   "isFromKnowledgeBase": true,
#   "confidenceScore": 0.85,
#   "suggestions": ["Reset password?", "What is 2FA?"],
#   "performanceMetrics": {
#     "totalMs": 245,
#     "kbSearchMs": 45,
#     "llmResponseMs": 180
#   },
#   "sessionId": "unique-session-id"
# }
```

### Test 4: Semantic Search

```bash
curl -X POST http://localhost:5000/api/kb/search \
  -H "Content-Type: application/json" \
  -d '{
    "query": "login",
    "embedding": [0.1, 0.2, 0.3, ...],  # 3072-dimensional vector
    "topK": 5
  }'
```

### Test 5: Performance Check

Check Logs folder for performance metrics:

```bash
# View recent logs
tail -f Logs/application-2026-02-18.log

# Look for performance lines:
# [ChatService] KB Search (45ms): 3 valid results, Confidence: 0.87
# [Performance] Total response time: 245ms
```

---

## 🐳 Docker Deployment

### Build Docker Image

```bash
# Build image
docker build -t jifas-ai-assistant:latest .

# Expected: Image created successfully

# List images
docker images | grep jifas
```

### Run with Docker Compose

```bash
# Start all services (API + Database)
docker-compose up -d

# Expected services:
# - jifas-api (port 8888)
# - sql-server (port 1433)

# Check status
docker-compose ps

# View logs
docker-compose logs -f jifas-api
```

### Configure Environment Variables

**docker-compose.yml:**
```yaml
environment:
  - Gemini__ApiKey=YOUR_ACTUAL_KEY
  - ASPNETCORE_ENVIRONMENT=Production
  - ConnectionStrings__DefaultConnection=Server=sql-server;Database=JIFAS_Assistant;...
```

Or use `.env` file:
```
Gemini__ApiKey=YOUR_KEY
ASPNETCORE_ENVIRONMENT=Production
```

---

## 📋 Production Checklist

Before deploying to production:

### Security
- [ ] API key is NOT in code (use environment variables)
- [ ] Database credentials are secure
- [ ] HTTPS is enabled (not just HTTP)
- [ ] CORS is properly configured
- [ ] Input validation is enabled
- [ ] Rate limiting is configured
- [ ] Secrets are in secure storage (not in git)

### Performance
- [ ] Response caching is enabled
- [ ] Database indexes are created
- [ ] Logging level is set to Warning/Error (not Debug)
- [ ] Performance monitoring is active

### Database
- [ ] Backups are scheduled
- [ ] Database is indexed properly
- [ ] Maintenance jobs are configured
- [ ] Connection pooling is optimized

### Deployment
- [ ] All tests pass
- [ ] Code review completed
- [ ] Staging deployment successful
- [ ] Rollback plan documented
- [ ] Monitoring alerts configured

### Documentation
- [ ] API documentation updated
- [ ] Runbooks created for common issues
- [ ] Team trained on deployment procedures
- [ ] Disaster recovery plan in place

---

## 🐛 Troubleshooting

### Issue: "Gemini API key not configured"

```
Error: Gemini:ApiKey not configured in appsettings.json
```

**Solution:**
```bash
# Check if secret is set
dotnet user-secrets list

# If missing, set it
dotnet user-secrets set "Gemini:ApiKey" "AIza..."

# Or use environment variable
$env:Gemini__ApiKey = "AIza..."

# Verify it's working
curl http://localhost:5000/health
```

### Issue: Database Connection Failed

```
Error: Cannot connect to database
Microsoft.Data.SqlClient.SqlException: Login failed for user
```

**Solution:**
```bash
# 1. Verify LocalDB is installed
sqllocaldb info

# 2. Check connection string
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\\MSSQLLocalDB;Database=JIFAS_Assistant;Trusted_Connection=true;Encrypt=false"

# 3. Create database
dotnet ef database update

# 4. Verify connection
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "SELECT @@VERSION"
```

### Issue: Port 5000 Already in Use

```
Error: Address already in use - 0.0.0.0:5000
```

**Solution:**
```bash
# Option 1: Kill process on port 5000
netstat -ano | findstr :5000
taskkill /PID <PID> /F

# Option 2: Use different port
dotnet run -- --urls "http://localhost:5001"

# Option 3: Change in launchSettings.json
{
  "applicationUrl": "http://localhost:5002"
}
```

### Issue: Semantic Search Not Working

```
No embeddings found for chunks
Semantic search returned 0 results
```

**Solution:**
```bash
# 1. Check if embeddings exist in DB
SELECT COUNT(*) FROM KnowledgeBaseChunks WHERE Embedding IS NOT NULL

# 2. Run seeding to generate embeddings
cd jifas_assistant.Seeding
dotnet run

# 3. Verify Gemini API key
dotnet user-secrets list

# 4. Test embedding API
POST /api/kb/search with embedding vector
```

### Issue: Slow Response Time (>500ms)

**Check these:**
1. Is caching enabled?
   ```bash
   # In appsettings
   "Caching": { "EnableResponseCache": true }
   ```

2. Are database indexes present?
   ```sql
   SELECT * FROM sys.indexes WHERE OBJECT_NAME(object_id) = 'KnowledgeBaseChunks'
   ```

3. Is KB too large?
   ```bash
   # Check KB size
   SELECT COUNT(*) FROM KnowledgeBaseChunks
   # If >100k, consider filtering by category
   ```

4. Check logs for bottleneck:
   ```bash
   tail -f Logs/application-*.log | grep "ms"
   ```

### Issue: Out of Memory

```
System.OutOfMemoryException: Insufficient memory
```

**Solution:**
```bash
# 1. Disable in-memory KB cache
"KnowledgeBase": { "EnableKBCache": false }

# 2. Enable Qdrant vector DB (offloads to separate service)
"Qdrant": { "Enabled": true, "Url": "http://localhost:6333" }

# 3. Increase available memory
# Increase container limits or machine RAM
```

---

## 🔗 Quick Links

| Topic | File |
|-------|------|
| Project Overview | README.md |
| Technical Deep-Dive | ANALYSIS.md |
| Improvements Applied | CODE_IMPROVEMENTS_IMPLEMENTED.md |
| Future Roadmap | ROADMAP.md |
| Security Guidelines | SECURITY.md |

---

## 📞 Getting Help

1. **Check Documentation**: README.md, ANALYSIS.md, SECURITY.md
2. **Review Logs**: `Logs/application-*.log` for error details
3. **Google Gemini Docs**: https://ai.google.dev
4. **ASP.NET Core**: https://learn.microsoft.com/aspnet/core
5. **Entity Framework**: https://learn.microsoft.com/ef/core

---

## ✅ Setup Complete!

If you got here without errors:
✅ Environment configured  
✅ Database initialized  
✅ API running  
✅ Tests passing  

**Next Steps:**
1. Read README.md for features overview
2. Run local tests from test endpoints
3. Review SECURITY.md for production deployment
4. Check ROADMAP.md for future improvements

---

**Last Updated**: 18 February 2026  
**Version**: 2.0  
**Status**: 🟢 Production Ready

