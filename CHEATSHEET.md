# JIFAS - Command Cheatsheet

## Start/Stop

```bash
# Start
docker-compose up -d

# Stop
docker-compose down

# Restart
docker-compose restart

# Logs (real-time)
docker-compose logs -f jifas-api

# Status
docker-compose ps
```

## Access

```bash
# API: http://localhost:8888
# Swagger: http://localhost:8888/swagger/index.html
# Health: http://localhost:8888/api/chatbot/health
```

## Test Chat

```bash
curl -X POST http://localhost:8888/api/chatbot/process \
  -H "Content-Type: application/json" \
  -d '{"message":"Hi JIFAS","sessionId":"test","userId":"user1"}'
```

## Debug

```bash
# Full logs
docker-compose logs jifas-api

# Container shell
docker exec -it jifas-assistant-api sh

# Rebuild (if issues)
docker-compose build --no-cache && docker-compose up -d

# Clean all
docker-compose down -v
docker system prune -a
```

## Edit Config

```bash
# Edit .env file for settings
# Then restart:
docker-compose restart
```

---

**All set! Container is running on port 8888** ?
