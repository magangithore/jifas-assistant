# Quick Start Guide - JIFAS AI Assistant .NET 10

## ?? Get Started in 5 Minutes

### Option 1: Local Development (Fastest)

```bash
# 1. Restore dependencies
dotnet restore

# 2. Build
dotnet build

# 3. Run
dotnet run

# Done! API running at http://localhost:5000
# Docs: http://localhost:5000/api-docs
```

### Option 2: Docker (Recommended)

**Windows:**
```powershell
.\docker-setup.bat
```

**Linux/Mac:**
```bash
chmod +x docker-setup.sh
./docker-setup.sh
```

**Done!** All services running at:
- API: http://localhost:5000
- Docs: http://localhost:5000/api-docs
- SQL Server: localhost:1433
- Qdrant: http://localhost:6333

### Environment Setup

```bash
# Copy and edit environment file
cp .env.docker .env

# Edit with your values:
# - SQL_SA_PASSWORD=YourPassword
# - GEMINI_API_KEY=your_api_key
# - QDRANT_API_KEY=your_key
```

## ?? Project Structure

| Folder | Purpose |
|--------|---------|
| `Data/` | Database context, models, repositories |
| `Configuration/` | Settings and configuration classes |
| `Controllers/` | API endpoints |
| `Services/` | Business logic (in transition) |
| `Middleware/` | Custom middleware |
| `Models/` | DTOs and request/response models |
| `Utilities/` | Helper functions |

## ?? Configuration

All settings in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.;Database=JifasAssistant;..."
  },
  "Gemini": {
    "ApiKey": "your_key",
    "Model": "gemini-2.0-flash"
  }
}
```

Environment-specific:
- `appsettings.Development.json` - Local
- `appsettings.Docker.json` - Docker
- `appsettings.Production.json` - Production

## ?? API Endpoints

```
GET  /                   - Status
GET  /health             - Health check
POST /api/chatbot/...    - Chat endpoints (in development)
```

Full docs at: `/api-docs` (Swagger UI)

## ?? Database

Migrations auto-apply on startup. To manually apply:

```bash
# Generate migration
dotnet ef migrations add MigrationName

# Apply migration
dotnet ef database update

# On Docker
docker-compose exec jifas-api dotnet ef database update
```

## ?? Docker Commands

```bash
# Start services
docker-compose up -d

# Stop services
docker-compose down

# View logs
docker-compose logs -f jifas-api

# Restart API
docker-compose restart jifas-api

# Remove all (including data)
docker-compose down -v
```

## ?? Troubleshooting

### Build fails
```bash
dotnet clean
dotnet restore
dotnet build
```

### Database connection error
- Check `appsettings.json` connection string
- Verify SQL Server is running
- Wait 30 seconds (SQL Server takes time to start)

### API won't start
```bash
# Check logs
docker-compose logs jifas-api

# Check health
curl http://localhost:5000/health
```

### Port already in use
Edit `docker-compose.yml`:
```yaml
ports:
  - "5000:80"   # Change 5000 to another port
```

## ?? Documentation

- **Setup**: See `SETUP_COMPLETE.md`
- **Migration**: See `MIGRATION_GUIDE.md`
- **Docker**: See `DOCKER_SETUP.md`
- **Config**: See `Configuration/AppSettings.cs`

## ?? Next Steps

1. ? Get it running (done!)
2. ?? Run migrations: `dotnet ef database update`
3. ?? Test API: Open `http://localhost:5000/api-docs`
4. ?? Update services (Phase 2)
5. ?? Deploy to production

## ?? Useful Commands

```bash
# Check what's running
docker ps

# Access database
docker-compose exec sqlserver sqlcmd -S localhost -U sa

# View API logs real-time
docker-compose logs -f --tail=50 jifas-api

# Health check
curl http://localhost:5000/health | jq

# Swagger UI
open http://localhost:5000/api-docs
```

## ?? Security Notes

1. **Never commit** `.env` or `appsettings.*.json` with secrets
2. Use `.gitignore` to prevent accidental commits
3. Store keys in user secrets locally: `dotnet user-secrets set "key" "value"`
4. Use environment variables in production

## ?? Support

- ?? Email: it@jababeka.com
- ?? Phone: +62-21-5241-8000
- ?? Docs: /api-docs

---

**Status**: ? Ready for development
**Last updated**: 2024
