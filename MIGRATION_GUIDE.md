# JIFAS AI Assistant - Migration Guide (.NET Framework ? .NET 10)

## Overview

Migrasi dari .NET Framework ke .NET 10 melibatkan:
- ? Database Schema Mapping
- ? Configuration Management (Web.config ? appsettings.json)
- ? Dependency Injection Setup
- ? Entity Framework Core Integration
- ? Docker Containerization
- ? Qdrant Vector Database Integration

## What's Been Done

### 1. Project Structure
```
Jifas.Assistant/
??? Program.cs                          # Main entry point
??? Configuration/
?   ??? AppSettings.cs                 # Strongly typed settings
??? Data/
?   ??? JifasAssistantDbContext.cs     # EF Core DbContext
?   ??? Models/
?   ?   ??? Chat.cs
?   ?   ??? KnowledgeBaseDocument.cs
?   ?   ??? UserFeedback.cs
?   ?   ??? Metric.cs
?   ??? Repositories/
?   ?   ??? IRepository.cs
?   ?   ??? Repository.cs
?   ?   ??? ChatRepository.cs
?   ?   ??? KnowledgeBaseRepository.cs
?   ??? UnitOfWork/
?       ??? IUnitOfWork.cs
?       ??? UnitOfWork.cs
??? Middleware/
?   ??? RequestLoggingMiddleware.cs
??? appsettings.json                   # Development config
??? appsettings.Docker.json            # Docker config
??? Dockerfile                         # Docker image
```

### 2. Web.config ? appsettings.json Mapping

#### Original Web.config sections:
```xml
<configuration>
  <appSettings>
    <add key="Gemini:ApiKey" value="..." />
    <add key="Gemini:Model" value="gemini-2.0-flash" />
    <!-- ... -->
  </appSettings>
  <connectionStrings>
    <add name="DefaultConnection" value="..." />
  </connectionStrings>
</configuration>
```

#### Migrated to appsettings.json:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "..."
  },
  "Gemini": {
    "ApiKey": "...",
    "Model": "gemini-2.0-flash"
  }
  // ... other sections
}
```

### 3. Configuration Classes

Semua settings telah dikonversi ke strongly-typed classes di `AppSettings.cs`:
- `GeminiSettings` - Gemini API configuration
- `QdrantSettings` - Vector database configuration
- `KnowledgeBaseSettings` - KB-specific settings
- `ChatSettings` - Chat system messages
- Dan 10+ konfigurasi lainnya

Access configuration:
```csharp
// Via DI
public class MyService
{
    private readonly IOptions<GeminiSettings> _settings;
    
    public MyService(IOptions<GeminiSettings> settings)
    {
        _settings = settings;
    }
}

// Or via AppSettings helper
var apiKey = appSettings.Gemini.ApiKey;
```

### 4. Database Context

**Old approach (Web.config):** Configuration di App.config
**New approach:** DbContext di code

```csharp
// Using EF Core with SQL Server
builder.Services.AddDbContext<JifasAssistantDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseSqlServer(connectionString, sqlServerOptions =>
    {
        sqlServerOptions.MigrationsAssembly("Jifas.Assistant");
        sqlServerOptions.EnableRetryOnFailure(maxRetryCount: 5);
    });
});
```

### 5. Entities & Database Schema

Empat main entities telah dibuat:

**Chat** - Menyimpan chat history
```csharp
public class Chat
{
    public int Id { get; set; }
    public string UserId { get; set; }
    public string UserMessage { get; set; }
    public string AssistantResponse { get; set; }
    public string SessionId { get; set; }
    public double? ConfidenceScore { get; set; }
    public bool IsFromKnowledgeBase { get; set; }
    // ... more fields
}
```

**KnowledgeBaseDocument** - Menyimpan KB documents dengan embedding
```csharp
public class KnowledgeBaseDocument
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }
    public string Category { get; set; }
    public string Embedding { get; set; }
    public int EmbeddingDimensions { get; set; }
    // ... more fields
}
```

**UserFeedback** - Menyimpan feedback dari users
```csharp
public class UserFeedback
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public string UserId { get; set; }
    public string Rating { get; set; }
    public bool IsHelpful { get; set; }
    // ... more fields
}
```

**Metric** - Menyimpan analytics metrics
```csharp
public class Metric
{
    public int Id { get; set; }
    public string MetricType { get; set; }
    public string MetricName { get; set; }
    public int Count { get; set; }
    public double Value { get; set; }
    // ... more fields
}
```

### 6. Repository Pattern

Generic repository untuk data access:

```csharp
// Get from repository
var chats = await _chatRepository.GetByUserIdAsync(userId);

// Or via Unit of Work
var chats = await _unitOfWork.Chats.GetByUserIdAsync(userId);
await _unitOfWork.SaveChangesAsync();
```

### 7. Dependency Injection

Semua services diregistrasi di `Program.cs`:

```csharp
// Configuration
builder.Services.Configure<GeminiSettings>(...)

// Data Access Layer
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Database Context
builder.Services.AddDbContext<JifasAssistantDbContext>(...)

// Middleware
builder.Services.AddTransient<RequestLoggingMiddleware>();
```

### 8. Docker Support

Dockerfile dan docker-compose.yml untuk containerization:

```bash
# Build and run
docker-compose up -d

# Services akan available di:
# - API: http://localhost:5000
# - SQL Server: localhost:1433
# - Qdrant: http://localhost:6333
```

## Migration Checklist

- [x] Create .NET 10 project structure
- [x] Configure appsettings.json
- [x] Setup DbContext dengan SQL Server
- [x] Create database models (Chat, KB, Feedback, Metrics)
- [x] Implement Repository pattern
- [x] Implement Unit of Work pattern
- [x] Setup dependency injection
- [x] Create EF Core migrations
- [x] Add Docker support (Dockerfile, docker-compose)
- [x] Setup environment-specific configs (Development, Docker)
- [x] Add request logging middleware
- [x] Configure Swagger/OpenAPI
- [x] Setup health checks
- [ ] Update all Services (ChatService, GeminiService, etc.)
- [ ] Update all Controllers (ChatbotController, etc.)
- [ ] Update Models (ChatRequest, ChatResponse, etc.)
- [ ] Test database connectivity
- [ ] Test API endpoints
- [ ] Test Docker deployment

## Remaining Tasks

### 1. Update Services

Services yang sudah ada perlu di-update untuk menggunakan:
- Dependency Injection (bukan `new` statements)
- ILogger instead of custom logging
- Configuration via IOptions<T>
- Async/await patterns
- Entity Framework DbContext

Contoh:
```csharp
// Before (Old)
public class ChatService
{
    public ChatService()
    {
        _geminiService = new GeminiService();
        _logger = LoggerFactory.GetLogger();
    }
}

// After (New)
public class ChatService : IChatService
{
    public ChatService(
        IGeminiService geminiService,
        ILogger<ChatService> logger,
        IUnitOfWork unitOfWork)
    {
        _geminiService = geminiService;
        _logger = logger;
        _unitOfWork = unitOfWork;
    }
}
```

### 2. Update Controllers

Controllers perlu di-update untuk:
- Inject services via constructor
- Use async methods
- Return proper HTTP status codes
- Use DTO/Response models

### 3. Database Migrations

Run migrations untuk setup database:

```bash
# Apply migrations
dotnet ef database update

# Or in Docker
docker-compose exec jifas-api dotnet ef database update
```

### 4. Testing

- [x] Build the solution
- [ ] Run integration tests
- [ ] Test API endpoints
- [ ] Test Docker deployment
- [ ] Load test Qdrant integration
- [ ] Test Gemini API integration

## Package Updates

Semua packages telah diupdate ke .NET 10 compatible versions:

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.EntityFrameworkCore | 10.0.3 | ORM |
| Microsoft.EntityFrameworkCore.SqlServer | 10.0.3 | SQL Server Provider |
| Microsoft.EntityFrameworkCore.Tools | 10.0.3 | Migrations |
| Swashbuckle.AspNetCore | 10.1.2 | API Documentation |
| Google.Generative.AI | 1.1.1 | Gemini API |
| Qdrant.Client | 1.11.0 | Vector DB Client |
| Newtonsoft.Json | 13.0.3 | JSON Serialization |
| Serilog | 4.0.1 | Structured Logging |

## Environment-Specific Configs

### Development (appsettings.json)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.;Database=JifasAssistant;Trusted_Connection=true;"
  }
}
```

### Docker (appsettings.Docker.json)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=sqlserver;Database=JifasAssistant;..."
  }
}
```

### Production
Gunakan user secrets atau environment variables:
```bash
# Set secrets
dotnet user-secrets set "Gemini:ApiKey" "xxx"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "xxx"
```

## Running the Application

### Development
```bash
# Build
dotnet build

# Run
dotnet run

# With specific environment
ASPNETCORE_ENVIRONMENT=Development dotnet run
```

### Docker
```bash
# Setup
./docker-setup.sh  # or docker-setup.bat on Windows

# Or manual
docker-compose build
docker-compose up -d
```

## API Documentation

Swagger documentation tersedia di:
- Development: http://localhost:5000/swagger/ui.html
- Docker: http://localhost:5000/api-docs

## Database Access

### SQL Server
```bash
# Connection string
Server=localhost,1433;Database=JifasAssistant;User Id=sa;Password=...

# Using docker
docker-compose exec sqlserver sqlcmd -S localhost -U sa
```

### Qdrant
```bash
# API endpoint
http://localhost:6333

# Dashboard
http://localhost:6333/dashboard
```

## Troubleshooting

### Build Errors
```bash
# Clean build
dotnet clean
dotnet build

# Restore packages
dotnet restore
```

### Database Connection
```bash
# Check connection string in appsettings.json
# Verify SQL Server is running
# Check firewall rules
```

### EF Core Migrations
```bash
# List migrations
dotnet ef migrations list

# Add migration
dotnet ef migrations add "MigrationName"

# Update database
dotnet ef database update

# Revert last migration
dotnet ef database update "PreviousMigration"
```

## Next Steps

1. **Update Services** - Refactor existing services to use DI and async patterns
2. **Update Controllers** - Refactor controllers and create proper DTOs
3. **Run Tests** - Execute integration and unit tests
4. **Deploy** - Test Docker deployment and production setup
5. **Optimize** - Performance tuning and caching optimization
6. **Monitor** - Setup logging and monitoring

## Support

- ?? it@jababeka.com
- ?? +62-21-5241-8000
- ?? Wiki: https://github.com/magangithore/jifas-assistant/wiki
