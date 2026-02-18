# 🛠️ JIFAS AI Assistant - Development Setup Guide

## Prerequisites

- **OS**: Windows 10/11, macOS, or Linux
- **.NET SDK**: Version 10.0 or later
- **SQL Server**: LocalDB (included with Visual Studio) or SQL Server 2019+
- **Git**: For cloning and version control
- **API Key**: Google Gemini API key from https://ai.google.dev

---

## 📥 Installation & Setup

### Step 1: Clone Repository

```bash
git clone https://github.com/your-org/jifas-assistant.git
cd jifas-assistant
```

### Step 2: Restore Dependencies

```bash
dotnet restore
```

### Step 3: Set Up Local Secrets (Recommended)

Choose ONE of the following methods:

#### Option A: User Secrets (Easiest for Local Development)

```bash
cd Jifas.Assistant

# Initialize user secrets (one-time)
dotnet user-secrets init

# Set your actual API key
dotnet user-secrets set "Gemini:ApiKey" "YOUR_ACTUAL_GOOGLE_GEMINI_API_KEY"

# Optional: Set custom connection string
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "your-custom-connection-string"

# Verify secrets are set
dotnet user-secrets list
```

**Note**: User secrets are stored locally and NOT committed to repository. Learn more:
- Windows: `%APPDATA%\Microsoft\UserSecrets\b375d8c9-0269-41b2-bece-00556711b0b1\secrets.json`
- Linux/Mac: `~/.microsoft/usersecrets/b375d8c9-0269-41b2-bece-00556711b0b1/secrets.json`

#### Option B: Environment Variables (Windows PowerShell)

```powershell
cd Jifas.Assistant

$env:Gemini__ApiKey = "YOUR_ACTUAL_GOOGLE_GEMINI_API_KEY"
$env:ASPNETCORE_ENVIRONMENT = "Development"

dotnet run
```

#### Option C: Local .env File (Not Recommended - But Possible)

1. Copy `.env.example` to `.env`:
   ```bash
   cp .env.example .env
   ```

2. Edit `.env` and add your credentials:
   ```
   Gemini__ApiKey=YOUR_ACTUAL_API_KEY
   ConnectionStrings__DefaultConnection=your-connection-string
   ```

3. Install package to load .env:
   ```bash
   cd Jifas.Assistant
   dotnet add package DotNetEnv
   ```

4. Update `Program.cs` to load .env file (if using this method)

**⚠️ Important**: Ensure `.env` is in `.gitignore` (it already is)

### Step 4: Create/Migrate Database

```bash
cd Jifas.Assistant

# Apply EF migrations to LocalDB
dotnet ef database update

# Or, if using a custom connection string:
dotnet ef database update --connection "your-connection-string"
```

**Database** will be created automatically with tables:
- `Chats` - Chat history
- `KnowledgeBaseDocuments` - KB documents
- `KnowledgeBaseChunks` - Document chunks + embeddings
- `Metrics` - Usage analytics
- `UserFeedbacks` - User feedback data

### Step 5: Build Solution

```bash
dotnet build jifas-assistant.slnx --configuration Debug
```

Expected output:
```
Build succeeded with X warning(s) in Y.Zs
```

---

## 🚀 Running the Application

### Local Development

```bash
cd Jifas.Assistant

# Run with development environment
dotnet run --configuration Debug

# Expected output:
# Building...
# info: Microsoft.Hosting.Lifetime[14]
#       Now listening on: http://localhost:5000
#       Now listening on: https://localhost:5001
```

The API will be available at:
- **HTTP**: http://localhost:5000
- **HTTPS**: https://localhost:5001
- **Swagger Docs**: http://localhost:5000/api-docs

### Docker (Optional)

```bash
# Build and run in Docker
docker-compose up --build

# Expected port: http://localhost:8888 (configured in .env)

# Stop containers
docker-compose down
```

---

## 🧪 Testing the API

### Health Check

```bash
curl http://localhost:5000/health
```

**Expected Response**:
```json
{
  "status": "Healthy",
  "database": "Healthy"
}
```

### Chat Endpoint (Simple Test)

```bash
curl -X POST http://localhost:5000/api/chatbot \
  -H "Content-Type: application/json" \
  -d '{
    "message": "Bagaimana cara login ke JIFAS?",
    "userId": "test-user-001",
    "sessionId": "test-session-001"
  }'
```

### Knowledge Base Search

```bash
curl http://localhost:5000/api/kb/documents
```

### Add Knowledge Base Document

```bash
curl -X POST http://localhost:5000/api/kb/documents \
  -H "Content-Type: application/json" \
  -d '{
    "title": "JIFAS Login Guide",
    "content": "To login to JIFAS, go to ...",
    "category": "Getting Started",
    "tags": "login,authentication"
  }'
```

---

## 📊 Configuration Reference

### appsettings.json (Default)

```json
{
  "Gemini": {
    "ApiKey": "${GEMINI_API_KEY}",  // Set via environment variable
    "Model": "gemini-2.0-flash"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=JIFAS_Assistant;..."
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

### appsettings.Development.json (Development Override)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  },
  "Gemini": {
    "ApiKey": "YOUR_DEVELOPMENT_API_KEY"
  }
}
```

### Key Configuration Options

| Setting | Description | Default |
|---------|-------------|---------|
| `Gemini:ApiKey` | Google Gemini API Key | `${GEMINI_API_KEY}` |
| `Gemini:Model` | LLM Model | `gemini-2.0-flash` |
| `ConnectionStrings:DefaultConnection` | Database connection | `(localdb)\MSSQLLocalDB` |
| `Logging:LogLevel:Default` | Default log level | `Information` |
| `Caching:EnableResponseCache` | Enable response caching | `true` |
| `KnowledgeBase:TopKResults` | KB search results count | `3` |
| `Chat:OutOfScopeMessage` | Message for out-of-scope queries | (Indonesian message) |

---

## 🐛 Common Issues & Troubleshooting

### Issue 1: Database Connection Error

**Error**:
```
Microsoft.Data.SqlClient.SqlException: Cannot open database
```

**Solution**:
1. Verify SQL Server LocalDB is installed
2. Check connection string in `appsettings.json`
3. Run `dotnet ef database update` to create DB
4. For custom server, update connection string in user secrets

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=YOUR_SERVER;Database=JIFAS_Assistant;..."
```

### Issue 2: Gemini API Key Error

**Error**:
```
InvalidOperationException: Gemini:ApiKey not configured in appsettings.json
```

**Solution**:
1. Ensure API key is set via user secrets or environment variable
2. Check key format (should start with `AIza...`)
3. Verify key is not expired in Google Cloud Console

```bash
dotnet user-secrets set "Gemini:ApiKey" "YOUR_ACTUAL_KEY"
```

### Issue 3: Port Already in Use

**Error**:
```
System.IO.EndOfStreamException: Unable to read data from the transport connection
```

**Solution**:
1. Kill process using port 5000:
   ```bash
   # Windows
   netstat -ano | findstr :5000
   taskkill /PID <PID> /F
   
   # Linux/Mac
   lsof -i :5000
   kill -9 <PID>
   ```

2. Or change port in `launchSettings.json`:
   ```json
   "profiles": {
     "http": {
       "commandName": "Project",
       "dotnetRunMessages": true,
       "applicationUrl": "http://localhost:6000"
     }
   }
   ```

### Issue 4: High Memory Usage During Startup

**Cause**: Loading large knowledge base into memory

**Solution**:
1. Enable Qdrant vector DB (see next section)
2. Disable in-memory caching temporarily:
   ```bash
   dotnet user-secrets set "Caching:EnableKBCache" "false"
   ```
3. Monitor memory with Task Manager

---

## 🔍 Advanced: Enabling Qdrant Vector Database

For better semantic search performance on large knowledge bases:

### Step 1: Start Qdrant Docker Container

```bash
docker run -p 6333:6333 \
  -e QDRANT_API_KEY=your-api-key \
  qdrant/qdrant:latest
```

### Step 2: Enable Qdrant in Configuration

```bash
cd Jifas.Assistant

dotnet user-secrets set "Qdrant:Enabled" "true"
dotnet user-secrets set "Qdrant:Url" "http://localhost:6333"
dotnet user-secrets set "Qdrant:CollectionName" "jifas_kb"
dotnet user-secrets set "Qdrant:ApiKey" "your-api-key"
```

### Step 3: Restart Application

```bash
dotnet run
```

---

## 📝 Development Workflow

### 1. Create Feature Branch

```bash
git checkout -b feature/your-feature-name
```

### 2. Make Changes

Edit files in `Jifas.Assistant/Services/`, `Controllers/`, etc.

### 3. Test Locally

```bash
dotnet build
dotnet run
curl http://localhost:5000/health
```

### 4. Commit Changes

```bash
git add .
git commit -m "feat: description of changes"
```

### 5. Push & Create PR

```bash
git push origin feature/your-feature-name
```

---

## 🚀 Performance Tips

1. **Enable Response Caching**: Already enabled by default
2. **Use Qdrant for Large KB**: For >10k documents
3. **Batch Embeddings**: Use `GenerateBatchEmbeddingsAsync()` instead of sequential
4. **Monitor Logs**: Check `Logs/` folder for performance insights
5. **Profile with**: Visual Studio Diagnostic Tools or dotTrace

---

## 📚 Useful Commands

```bash
# Build
dotnet build

# Run
dotnet run

# Test (when tests are added)
dotnet test

# Format code
dotnet format

# Apply migrations
dotnet ef database update

# Create migration
dotnet ef migrations add MigrationName

# View NuGet packages
dotnet list package

# Check for vulnerabilities
dotnet list package --vulnerable

# Publish for deployment
dotnet publish -c Release -o ./publish
```

---

## 🔐 Security Checklist for Development

- [ ] Gemini API key set via user secrets (not in code)
- [ ] Database connection string uses trusted connection (Windows Auth)
- [ ] `.env` file is in `.gitignore`
- [ ] No secrets logged to console (check `appsettings.json`)
- [ ] HTTPS enabled in production config
- [ ] CORS configured appropriately
- [ ] Input validation enabled (default on)
- [ ] Rate limiting considered (see ANALYSIS.md)

---

## 📞 Support & Resources

- **Google Gemini API**: https://ai.google.dev
- **.NET Documentation**: https://learn.microsoft.com/dotnet
- **Entity Framework**: https://learn.microsoft.com/ef/core
- **ASP.NET Core**: https://learn.microsoft.com/aspnet/core
- **Qdrant Vector DB**: https://qdrant.tech

---

## ✅ Quick Start Checklist

- [ ] Install .NET 10.0 SDK
- [ ] Clone repository
- [ ] Run `dotnet restore`
- [ ] Set Gemini API key via user secrets
- [ ] Run `dotnet ef database update`
- [ ] Run `dotnet build`
- [ ] Run `dotnet run` and test with curl
- [ ] View API docs at http://localhost:5000/api-docs
- [ ] Read SECURITY.md for credential management
- [ ] Read ANALYSIS.md for architecture overview

---

**Last Updated**: February 18, 2026  
**For Questions**: Check SECURITY.md and ANALYSIS.md for detailed information

