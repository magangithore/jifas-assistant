# JIFAS AI Assistant - Deployment & Setup Guide

## ?? Project Overview

JIFAS AI Assistant adalah aplikasi ASP.NET Core 10 yang menyediakan:
- **Chat API** untuk interaksi pengguna dengan AI (Gemini)
- **RAG Search** (Retrieval-Augmented Generation) dengan Knowledge Base
- **Admin API** untuk manajemen Knowledge Base
- **Health Checks** untuk monitoring sistem
- **Analytics & Metrics** untuk tracking penggunaan

---

## ??? Technology Stack

- **.NET 10** dengan C# 14.0
- **ASP.NET Core** (Minimal APIs)
- **Entity Framework Core 10** (ORM)
- **SQL Server** (Data persistence)
- **Google Gemini API** (AI & Embeddings)
- **Memory Cache** (Performance)
- **Swagger/OpenAPI** (Documentation)

---

## ?? Prerequisites

### Development Environment
- .NET 10 SDK or Runtime
- SQL Server 2019+ atau LocalDB
- Visual Studio 2024+ atau VS Code
- PowerShell 7+ (optional, for scripts)

### Production Environment
- .NET 10 Runtime (Hosting Bundle)
- SQL Server 2019+ atau Azure SQL Database
- Windows Server 2019+ atau Linux
- 4GB+ RAM minimum
- 50GB+ disk space

---

## ?? Quick Start

### 1. Clone Repository
```bash
git clone https://github.com/magangithore/jifas-assistant.git
cd jifas-assistant
```

### 2. Setup Database
```bash
# Lokasi: (localdb)\MSSQLLocalDB
# Database: JIFAS_Assistant
# Akan dicreate secara otomatis oleh EF Core migrations
```

### 3. Configure API Key
```bash
# Set environment variable
$env:GEMINI_API_KEY = "your-api-key-here"

# Atau edit appsettings.json (development only)
```

### 4. Build & Run
```bash
cd Jifas.Assistant

# Build
dotnet build

# Run
dotnet run

# Atau dengan specific profile
dotnet run --launch-profile "https"
```

### 5. Access Application
```
API: https://localhost:5001/api
Documentation: https://localhost:5001/api-docs
Health Check: https://localhost:5001/health
```

---

## ?? API Endpoints

### Chat API
```
POST /api/chatbot/conversation
- Process user message through AI
- Request: { "message": "string", "userId": "string", "sessionId": "string" }
- Response: { "message": "string", "suggestions": ["string"], ... }
```

### Knowledge Base Search
```
POST /api/knowledgebasesearch/search
- Hybrid RAG search
- Request: { "query": "string", "embedding": [float] }
- Response: { "results": [...], "totalCount": int }
```

### KB Management
```
GET    /api/kb/documents              - List all documents
GET    /api/kb/documents/{id}         - Get document details
POST   /api/kb/documents              - Create document
PUT    /api/kb/documents/{id}         - Update document
DELETE /api/kb/documents/{id}         - Delete document
```

### Health & Status
```
GET /api/chatbot/health                - System health status
GET /api/chatbot/analytics             - Analytics dashboard
GET /api/chatbot/analytics/popular     - Popular queries
```

---

## ?? Configuration

### appsettings.json Keys

#### Gemini Configuration
```json
"Gemini": {
  "ApiKey": "your-api-key",
  "Model": "gemini-2.0-flash",
  "EmbeddingModel": "gemini-embedding-001",
  "EmbeddingDimensions": 3072
}
```

#### Knowledge Base
```json
"KnowledgeBase": {
  "MinRelevanceScore": 0.3,
  "TopKResults": 3,
  "CacheDurationMinutes": 30
}
```

#### Caching
```json
"Caching": {
  "EnableKBCache": true,
  "EnableResponseCache": true,
  "ResponseCacheDurationHours": 24
}
```

---

## ?? Database Schema

### Tables
1. **KnowledgeBaseDocuments** - KB documents
2. **KnowledgeBaseChunks** - Document chunks with embeddings
3. **Chats** - Chat conversation history
4. **Metrics** - Analytics & usage metrics
5. **UserFeedbacks** - User feedback on responses

---

## ?? Security Considerations

### Before Production Deployment

1. **API Key Management**
   ```bash
   # Use Environment Variables or Azure Key Vault
   $env:GEMINI_API_KEY = "your-key"
   # Don't commit API keys to repository
   ```

2. **HTTPS/TLS**
   ```json
   "Kestrel": {
     "Endpoints": {
       "Https": {
         "Url": "https://0.0.0.0:443",
         "Certificate": {...}
       }
     }
   }
   ```

3. **Authentication**
   - Implement Windows Authentication or OAuth2
   - Add authorization policies per endpoint

4. **CORS**
   - Configure for specific domains only (production)
   - Currently allows all (`AllowAnyOrigin()`)

5. **Rate Limiting**
   - Implement to prevent API abuse
   - Configure timeouts appropriately

---

## ?? Monitoring & Logging

### Logging
- File-based logging to `Logs/` folder
- Configurable log levels per environment
- Log rotation available

### Health Checks
```bash
# Check system health
curl https://localhost:5001/api/chatbot/health

# Response includes:
# - Database connectivity
# - Gemini API status
# - KB availability
# - Cache status
```

### Performance Monitoring
- Built-in performance tracking
- Operation timing metrics
- Slow operation detection

---

## ?? Deployment Steps

### Docker Deployment (Recommended)

1. **Build Docker Image**
```bash
docker build -f Jifas.Assistant/Dockerfile -t jifas-assistant:latest .
```

2. **Run Container**
```bash
docker run -d \
  -e GEMINI_API_KEY="your-key" \
  -e DATABASE_CONNECTION_STRING="your-connection" \
  -p 80:80 \
  --name jifas-assistant \
  jifas-assistant:latest
```

### IIS Deployment

1. **Publish**
```bash
dotnet publish -c Release -o ./publish
```

2. **Create Application Pool** (Integrated)

3. **Create Website**
   - Physical Path: `./publish`
   - Binding: `https://your-domain.com`

4. **Configure HTTPS**
   - Install SSL Certificate
   - Bind to HTTPS port

### Azure App Service

1. **Create Resource Group**
```bash
az group create --name jifas-rg --location eastasia
```

2. **Create App Service Plan**
```bash
az appservice plan create \
  --name jifas-plan \
  --resource-group jifas-rg \
  --sku B2
```

3. **Create App Service**
```bash
az webapp create \
  --resource-group jifas-rg \
  --plan jifas-plan \
  --name jifas-assistant
```

4. **Deploy**
```bash
az webapp deployment source config-zip \
  --resource-group jifas-rg \
  --name jifas-assistant \
  --src publish.zip
```

---

## ?? Testing

### Unit Tests
```bash
dotnet test
```

### Integration Tests
```bash
# In development environment
dotnet test --configuration Release
```

### API Testing
```bash
# Using provided PowerShell scripts
.\test-rag-api.ps1
```

---

## ?? Troubleshooting

### Common Issues

1. **API Key Not Found**
   ```
   Error: GEMINI_API_KEY environment variable not set
   Solution: Set environment variable or update appsettings.json
   ```

2. **Database Connection Failed**
   ```
   Error: Cannot connect to (localdb)\MSSQLLocalDB
   Solution: Ensure SQL Server LocalDB is installed and running
   ```

3. **Port Already in Use**
   ```
   Error: Address already in use on port 5001
   Solution: Change port in launchSettings.json or kill process on port
   ```

4. **Embedding Generation Failed**
   ```
   Error: Failed to generate embeddings
   Solution: Verify Gemini API key and API quota
   ```

---

## ?? Support

- **Documentation**: Check `/api-docs` endpoint
- **Health Status**: Check `/api/chatbot/health`
- **Contact**: it@jababeka.com

---

## ?? License

© 2024 Jababeka Group. All rights reserved.

---

## ? Deployment Checklist

- [ ] Environment variables configured
- [ ] Database created and accessible
- [ ] HTTPS certificate installed
- [ ] API key verified
- [ ] Logging configured
- [ ] Monitoring setup
- [ ] Backup procedure implemented
- [ ] Health check passing
- [ ] Load testing completed
- [ ] Security review passed

---

**Last Updated**: December 2024
**Status**: Ready for Production
