# JIFAS AI Assistant - Docker Setup Guide

## Prerequisites

- Docker Desktop (v20.10+)
- Docker Compose (v2.0+)
- Git

Untuk Windows dan Mac, download Docker Desktop dari https://www.docker.com/products/docker-desktop

## Quick Start

### 1. Clone Repository
```bash
git clone https://github.com/magangithore/jifas-assistant.git
cd jifas-assistant
```

### 2. Configure Environment
```bash
# Copy template dan edit dengan credentials Anda
cp .env.docker .env.local

# Edit .env.local dengan values yang sesuai
# Minimal: SQL_SA_PASSWORD, GEMINI_API_KEY, QDRANT_API_KEY
```

### 3. Run Setup Script

**Linux/Mac:**
```bash
chmod +x docker-setup.sh
./docker-setup.sh
```

**Windows (PowerShell):**
```powershell
.\docker-setup.bat
```

**Manual (jika script tidak bekerja):**
```bash
docker-compose build
docker-compose up -d
```

### 4. Verify Services

```bash
# Check all services
docker-compose ps

# Check API health
curl http://localhost:5000/health

# Check Qdrant
curl http://localhost:6333/health

# View logs
docker-compose logs -f jifas-api
```

## Service URLs

- **API**: http://localhost:5000
- **API Documentation**: http://localhost:5000/api-docs
- **Health Check**: http://localhost:5000/health
- **Qdrant**: http://localhost:6333
- **Qdrant Dashboard**: http://localhost:6333/dashboard
- **SQL Server**: localhost:1433
- **pgAdmin**: http://localhost:5050

## Default Credentials

| Service | Username | Password |
|---------|----------|----------|
| SQL Server | sa | (from .env.docker) |
| pgAdmin | admin@jababeka.com | (from .env.docker) |
| Qdrant | - | (from .env.docker) |

## Common Docker Commands

```bash
# Start services
docker-compose up -d

# Stop services
docker-compose down

# View logs
docker-compose logs -f jifas-api

# View specific service logs
docker-compose logs -f sqlserver

# Restart a service
docker-compose restart jifas-api

# Execute command in container
docker-compose exec jifas-api dotnet ef database update

# Remove volumes (WARNING: deletes data)
docker-compose down -v

# Rebuild images
docker-compose build --no-cache
```

## Database Access

### Using SQL Server Management Studio (SSMS)
- Server: localhost,1433
- Username: sa
- Password: (from SQL_SA_PASSWORD in .env.docker)
- Database: JifasAssistant

### Using Docker CLI
```bash
docker-compose exec sqlserver sqlcmd -S localhost -U sa -P YOUR_PASSWORD
```

## Qdrant Vector Database

### Initialize Collections
```bash
# Create collection for JIFAS KB
curl -X PUT "http://localhost:6333/collections/jifas_kb" \
  -H "Content-Type: application/json" \
  -d '{
    "vectors": {
      "size": 384,
      "distance": "Cosine"
    }
  }'
```

### Seed Data
```bash
# The application provides endpoints to seed KB data
curl -X POST http://localhost:5000/api/knowledge-base/seed
```

## Troubleshooting

### Services won't start
```bash
# Check Docker daemon
docker ps

# View detailed logs
docker-compose logs

# Check resources
docker stats
```

### Database connection failed
```bash
# Check SQL Server is ready
docker-compose exec sqlserver sqlcmd -S localhost -U sa

# Wait 30 seconds and retry (SQL Server needs time to initialize)
```

### Port already in use
Edit `docker-compose.yml` dan ubah port mappings:
```yaml
ports:
  - "5000:80"      # Change 5000 to available port
  - "5001:443"
```

### Memory/Resource issues
Edit `docker-compose.yml` dan add resource limits:
```yaml
jifas-api:
  deploy:
    resources:
      limits:
        cpus: '1.5'
        memory: 2G
```

## Production Deployment

Untuk production, ikuti security best practices:

1. **Use environment variables** untuk sensitive data
2. **Set strong passwords** untuk SQL Server dan Qdrant
3. **Enable HTTPS** dengan valid certificates
4. **Configure CORS** untuk domain Anda
5. **Use Docker secrets** untuk credentials
6. **Monitor logs** dengan logging service
7. **Setup backups** untuk databases
8. **Use reverse proxy** (Nginx/Traefik)
9. **Enable health checks** dan auto-restart
10. **Use volume backups** untuk persistent data

### Production docker-compose.yml updates
```yaml
services:
  jifas-api:
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=https://+:443
    ports:
      - "443:443"
    restart: always
    healthcheck:
      test: ["CMD", "curl", "-f", "https://localhost/health"]
```

## Monitoring

### Docker Logs
```bash
# All containers
docker-compose logs -f

# Specific service
docker-compose logs -f jifas-api --tail 100

# With timestamps
docker-compose logs -f --timestamps
```

### Health Metrics
```bash
# API Health
curl http://localhost:5000/health | jq

# Database Health
docker-compose exec sqlserver sqlcmd -S localhost -U sa -P PASSWORD \
  -Q "SELECT 'Database is healthy' AS Status"

# Qdrant Status
curl http://localhost:6333/health | jq
```

## Backup & Recovery

### Backup Database
```bash
docker-compose exec sqlserver \
  /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P PASSWORD \
  -Q "BACKUP DATABASE JifasAssistant TO DISK = '/var/opt/mssql/jifas_backup.bak'"
```

### Backup Qdrant
```bash
# Qdrant maintains snapshots automatically
# Located in: qdrant-snapshots volume
docker cp jifas-qdrant:/qdrant/snapshots ./qdrant_backups
```

### Restore Database
```bash
docker-compose exec sqlserver \
  /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P PASSWORD \
  -Q "RESTORE DATABASE JifasAssistant FROM DISK = '/var/opt/mssql/jifas_backup.bak'"
```

## Performance Tuning

### SQL Server Configuration
Edit `docker-compose.yml`:
```yaml
sqlserver:
  environment:
    MSSQL_MEMORY_LIMIT_MB: 2048
    MSSQL_COLLATION: SQL_Latin1_General_CP1_CI_AS
```

### API Configuration
Edit `appsettings.Docker.json`:
```json
{
  "Performance": {
    "MaxCacheSize": 10000,
    "EnableCompressionResponse": true,
    "SlowOperationThresholdMs": 1000
  }
}
```

## Support

Untuk bantuan lebih lanjut:
- ?? Email: it@jababeka.com
- ?? Phone: +62-21-5241-8000
- ?? Documentation: https://github.com/magangithore/jifas-assistant/wiki

## License

Internal Use Only - Jababeka Company
