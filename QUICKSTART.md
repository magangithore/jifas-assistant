# JIFAS AI Assistant - Quick Start

## ? Status
- Code: Ready
- Build: ? Success
- Config: ? .env created
- API: Running on port 8888

## ?? Running Locally (Recommended)

### Build & Run
```bash
cd D:\Users\magang.it8\jifas-assistant
dotnet build
dotnet run --project Jifas.Assistant/Jifas.Assistant.csproj
```

### Access API
```
http://localhost:8888
Swagger: http://localhost:8888/swagger/index.html
Health: http://localhost:8888/api/chatbot/health
```

## ?? Docker Setup (When Internet Available)

### Build Docker Image
```bash
docker-compose -f docker-compose.production.yml build
```

### Run Container
```bash
docker-compose -f docker-compose.production.yml up -d
```

### View Logs
```bash
docker-compose -f docker-compose.production.yml logs -f jifas-api
```

### Stop Container
```bash
docker-compose -f docker-compose.production.yml down
```

## ?? Configuration (.env)

**Current Settings:**
- Port: 8888
- DB: Local SQL Server (Windows Auth)
- Gemini API: Configured
- Environment: Production

**Security Note:**
?? API Key is now active in .env - keep it secure!

## ?? Key Files

| File | Purpose |
|------|---------|
| `.env` | Environment variables (API Key, DB Connection) |
| `docker-compose.production.yml` | Docker orchestration |
| `Dockerfile` | Container image definition |
| `.dockerignore` | Build optimization |

## ?? Test Chat Endpoint

```bash
curl -X POST http://localhost:8888/api/chatbot/process \
  -H "Content-Type: application/json" \
  -d '{
    "message": "Bagaimana cara membuat invoice di AR?",
    "sessionId": "test-session"
  }'
```

## ? Troubleshooting

### Port 8888 Already in Use
```bash
netstat -ano | findstr :8888
taskkill /PID <PID> /F
```

### Database Connection Failed
- Verify SQL Server running: `(localdb)\MSSQLLocalDB`
- Check connection string in `.env`

### Docker Build Issues
- Ensure Docker Desktop running
- Check internet connection for image pull
- Try: `docker system prune` to free space

---

**Status:** ? Ready for Development/Testing  
**Last Updated:** 2024
