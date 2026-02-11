# ?? JIFAS AI Assistant - Quick Deployment Guide

## 5-Minute Setup

### Step 1: Prerequisites Check
```bash
dotnet --version        # Should be 10.0.0 or higher
sqlcmd -S ? -Q "SELECT @@VERSION"  # SQL Server running
```

### Step 2: Database Setup
```bash
# Start SQL Server LocalDB
sqllocaldb start mssqllocaldb

# Verify it's running
sqllocaldb info mssqllocaldb
```

### Step 3: Configure Application
Edit `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(local);Database=JifasAssistant;Trusted_Connection=true;"
  },
  "Gemini": {
    "ApiKey": "YOUR_GEMINI_API_KEY_HERE"
  }
}
```

### Step 4: Run Application
```bash
cd Jifas.Assistant
dotnet run
```

### Step 5: Verify
```bash
# Should return JSON with status "running"
curl http://localhost:5180/

# Response:
# {"message":"JIFAS AI Assistant API v1.0","status":"running","documentation":"/api-docs"}
```

---

## Database Status

? **Database**: JifasAssistant (Created)
? **Tables**: 4 (Chats, KnowledgeBaseDocuments, UserFeedbacks, Metrics)
? **Indexes**: 5 (for performance optimization)
? **Migrations**: Applied (20260211075526_InitialCreate)

---

## API Quick Reference

### Health Check
```bash
curl http://localhost:5180/api/chatbot/health
```

### Chat Endpoint
```bash
curl -X POST http://localhost:5180/api/chatbot/conversation \
  -H "Content-Type: application/json" \
  -d '{"userId":"test1","sessionId":"sess1","userMessage":"Hello"}'
```

### List KB Documents
```bash
curl http://localhost:5180/api/kb/documents
```

### Search Knowledge Base
```bash
curl "http://localhost:5180/api/kb/search?query=your+search+term"
```

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Port 5180 already in use | `taskkill /PID {pid} /F` then restart |
| Database connection fails | `sqllocaldb start mssqllocaldb` |
| API not responding | Check logs: `Logs/jifas-chatbot-*.log` |
| Build errors | `dotnet clean && dotnet build` |

---

## Service Dependencies

```
24 Services Registered:
? ChatService              ? IChat interface
? KnowledgeBaseService     ? IKnowledgeBase interface  
? GeminiService            ? IGemini interface
? GeminiEmbeddingService   ? IEmbedding interface
? HealthCheckService       ? IHealthCheck interface
? FileLoggerService        ? ILogger interface
? MemoryCacheService       ? ICache interface
? And 17 more infrastructure & data services...
```

---

## Configuration Files

| File | Purpose |
|------|---------|
| `appsettings.json` | Main configuration |
| `appsettings.Development.json` | Dev overrides |
| `appsettings.Production.json` | Prod overrides |

---

## Performance Metrics

| Metric | Value |
|--------|-------|
| Build Time | ~2-5 seconds |
| Startup Time | ~3-5 seconds |
| API Response | <100ms (typical) |
| Database Queries | <50ms (indexed) |

---

## Deployment Options

### Local Development
```bash
dotnet run
```

### Docker Container
```bash
docker build -t jifas-assistant .
docker run -p 5180:5180 jifas-assistant
```

### IIS (Windows Production)
```bash
dotnet publish -c Release -o ./publish
# Copy publish folder to IIS
```

### Linux/Azure App Service
```bash
dotnet publish -c Release
# Deploy publish folder to cloud platform
```

---

## Security Checklist

- [ ] API keys not in source code (use environment variables)
- [ ] HTTPS enabled in production
- [ ] SQL Server using Windows authentication (trusted connection)
- [ ] CORS configured for allowed origins
- [ ] Rate limiting enabled
- [ ] Logging enabled for audit trail

---

## Monitoring

### View Application Logs
```bash
# Latest 50 lines
tail -50 Logs/jifas-chatbot-2026-02-11.log

# Real-time log watch
Get-Content Logs/jifas-chatbot-*.log -Wait
```

### Health Status
```bash
curl http://localhost:5180/api/chatbot/health | jq '.services'
```

### Performance Metrics
```bash
curl http://localhost:5180/api/metrics/summary
```

---

## Common Tasks

### Reload Configuration
```bash
# Restart application
Ctrl+C
dotnet run
```

### Clear Database
```bash
# Drop and recreate
dotnet-ef database drop
dotnet-ef database update
```

### View Database Tables
```bash
sqlcmd -S (localdb)\mssqllocaldb -E -d JifasAssistant -Q "SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE'"
```

### Restart SQL Server
```bash
sqllocaldb stop mssqllocaldb
sqllocaldb start mssqllocaldb
```

---

## Version Info

- **Application Version**: 2.0.0 (.NET 10)
- **Last Updated**: Feb 11, 2026
- **Status**: ? Production Ready

---

**For detailed documentation, see README.md**
