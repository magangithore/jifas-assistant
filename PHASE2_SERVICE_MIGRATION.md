# Phase 2: How to Re-enable & Update Services

## ?? Overview

Services are currently excluded from the .csproj to keep the build clean. This guide shows how to re-enable and update them one by one.

---

## ?? Service Update Process

### Step 1: Pick a Service

Start with a priority service from FINAL_CHECKLIST.md:
- Example: `GeminiService`

### Step 2: Remove from Exclusion

In `Jifas.Assistant.csproj`, remove the line:
```xml
<Compile Remove="Services\GeminiService.cs" />
```

### Step 3: Update the Service

Open the service and update it to use new patterns:

#### Pattern 1: Configuration (OLD ? NEW)

**BEFORE:**
```csharp
private readonly string _apiKey = System.Configuration.ConfigurationManager.AppSettings["Gemini:ApiKey"];
```

**AFTER:**
```csharp
private readonly IOptions<GeminiSettings> _settings;
private readonly string _apiKey;

public GeminiService(IOptions<GeminiSettings> settings)
{
    _settings = settings;
    _apiKey = settings.Value.ApiKey;
}
```

#### Pattern 2: Logging (OLD ? NEW)

**BEFORE:**
```csharp
using Jifas.Chatbot.Services;
// ...
_logger = LoggerFactory.GetLogger();
_logger.LogInformation("Message");
```

**AFTER:**
```csharp
using Microsoft.Extensions.Logging;
// ...
private readonly ILogger<GeminiService> _logger;

public GeminiService(ILogger<GeminiService> logger)
{
    _logger = logger;
}
// ...
_logger.LogInformation("Message");
```

#### Pattern 3: Database Access (OLD ? NEW)

**BEFORE:**
```csharp
private readonly JIFAS_AssistantEntities _db;

public GeminiService(JIFAS_AssistantEntities db)
{
    _db = db;
}
```

**AFTER:**
```csharp
using Jifas.Assistant.Data;
// ...
private readonly JifasAssistantDbContext _context;
private readonly IUnitOfWork _unitOfWork;

public GeminiService(JifasAssistantDbContext context, IUnitOfWork unitOfWork)
{
    _context = context;
    _unitOfWork = unitOfWork;
}
```

#### Pattern 4: Async/Await

**BEFORE:**
```csharp
public string GenerateResponse(string query)
{
    // Synchronous code
    return result;
}
```

**AFTER:**
```csharp
public async Task<string> GenerateResponseAsync(string query)
{
    // Asynchronous code
    return await result;
}
```

### Step 4: Register in DI

Add service registration to `Program.cs`:

```csharp
// In Program.cs, after repository registration

// Services
builder.Services.AddScoped<IGeminiService, GeminiService>();
builder.Services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();
// ... other services
```

### Step 5: Build & Test

```bash
# Build to check for errors
dotnet build

# Run tests for this service
dotnet test --filter "GeminiService"

# Run the app
dotnet run
```

### Step 6: Commit

```bash
git add .
git commit -m "Phase 2: Migrate GeminiService to .NET 10"
```

---

## ?? Common Replacements Reference

### Namespaces

| Old | New |
|-----|-----|
| `Jifas.Chatbot.DAL` | `Jifas.Assistant.Data` |
| `Jifas.Chatbot.Services` | `Jifas.Assistant.Services` |
| `Jifas.Chatbot.Models` | `Jifas.Assistant.Models` |
| `System.Configuration` | `Microsoft.Extensions.Configuration` |
| `System.Data.Entity` | `Microsoft.EntityFrameworkCore` |

### Classes

| Old | New |
|-----|-----|
| `JIFAS_AssistantEntities` | `JifasAssistantDbContext` |
| `ConfigurationManager.AppSettings` | `IOptions<T>` |
| `LoggerFactory.GetLogger()` | `ILogger<T>` |
| `HttpRuntime.AppDomainAppPath` | `IWebHostEnvironment.ContentRootPath` |

### Interfaces

| Old | New |
|-----|-----|
| Custom ILoggerService | `ILogger<T>` |
| Custom ICacheService | `IMemoryCache` |

---

## ?? Service Update Guide

### Priority 1: Core Services

#### ChatService
- [ ] Add DI for IOptions<T>
- [ ] Replace custom logging
- [ ] Add async/await
- [ ] Update DB references

#### GeminiService
- [ ] Get API key from IOptions<GeminiSettings>
- [ ] Replace HTTP client usage if needed
- [ ] Add proper error handling
- [ ] Make async

#### KnowledgeBaseService
- [ ] Replace JIFAS_AssistantEntities with DbContext
- [ ] Use repositories instead of direct DB access
- [ ] Update configuration access
- [ ] Make async

### Priority 2: Supporting Services

#### QdrantVectorService
- [ ] Get Qdrant settings from IOptions<QdrantSettings>
- [ ] Use DbContext for data
- [ ] Make async
- [ ] Add error handling

#### MetricsService
- [ ] Get metrics config from IOptions<MetricsSettings>
- [ ] Use DbContext for storage
- [ ] Make async

#### AnalyticsService
- [ ] Replace old DB references
- [ ] Use ILogger
- [ ] Make async

---

## ?? Testing Template

Create unit tests for each updated service:

```csharp
using Xunit;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Moq;

public class GeminiServiceTests
{
    private readonly Mock<IOptions<GeminiSettings>> _optionsMock;
    private readonly Mock<ILogger<GeminiService>> _loggerMock;

    public GeminiServiceTests()
    {
        _optionsMock = new Mock<IOptions<GeminiSettings>>();
        _loggerMock = new Mock<ILogger<GeminiService>>();
    }

    [Fact]
    public async Task GenerateResponseAsync_ShouldReturnValidResponse()
    {
        // Arrange
        var service = new GeminiService(_optionsMock.Object, _loggerMock.Object);

        // Act
        var result = await service.GenerateResponseAsync("test query");

        // Assert
        Assert.NotNull(result);
    }
}
```

---

## ?? Common Pitfalls

### 1. Forgetting to make methods async
```csharp
// ? WRONG
public Task<string> GetDataAsync()
{
    return GetDataSync();  // Not actually async!
}

// ? RIGHT
public async Task<string> GetDataAsync()
{
    return await _service.GetDataAsync();
}
```

### 2. Mixing old and new patterns
```csharp
// ? WRONG - Mixing ConfigurationManager and IOptions
var oldKey = ConfigurationManager.AppSettings["key"];
var newValue = _options.Value.Key;

// ? RIGHT - Use only IOptions
var value = _options.Value.Key;
```

### 3. Creating services with `new`
```csharp
// ? WRONG
var service = new GeminiService();

// ? RIGHT - Use DI
public ChatService(IGeminiService geminiService) { }
```

### 4. Forgetting namespaces
```csharp
// ? WRONG - Missing using
var options = IOptions<GeminiSettings>;

// ? RIGHT
using Microsoft.Extensions.Options;
var options = IOptions<GeminiSettings>;
```

---

## ?? Debugging Tips

### Build Errors
```bash
# Clean build
dotnet clean
dotnet build

# Check error details
dotnet build --verbosity detailed
```

### Runtime Errors
```bash
# Enable detailed logging
# In appsettings.json:
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Information"
    }
  }
}
```

### Testing
```bash
# Run all tests
dotnet test

# Run specific test
dotnet test --filter "GeminiService"

# Run with output
dotnet test --verbosity detailed
```

---

## ?? Progress Tracking

Create a spreadsheet tracking:

| Service | Status | Updated By | Date | Notes |
|---------|--------|-----------|------|-------|
| ChatService | In Progress | - | - | Using new DbContext |
| GeminiService | Not Started | - | - | - |
| KnowledgeBaseService | Not Started | - | - | - |

---

## ?? Batch Processing

To speed up Phase 2, can process services by category:

### Batch 1: AI Services
- GeminiService
- EmbeddingService
- GeminiEmbeddingService

### Batch 2: KB Services
- KnowledgeBaseService
- KnowledgeBaseEmbeddingService
- SuggestionService

### Batch 3: Vector DB
- QdrantVectorService
- QdrantSeedingService
- QdrantInitializer

### Batch 4: Analytics
- MetricsService
- AnalyticsService
- PerformanceMonitorService

### Batch 5: Supporting
- ChatService
- TicketService
- ConversationService
- OutOfScopeDetector

---

## ?? Checklist for Each Service

When updating a service:
- [ ] Remove from .csproj exclusions
- [ ] Replace all `ConfigurationManager` with `IOptions<T>`
- [ ] Replace `LoggerFactory` with `ILogger<T>`
- [ ] Replace DB access with DbContext/Repositories
- [ ] Make methods async where appropriate
- [ ] Remove `new` statements for services (use DI)
- [ ] Update namespaces
- [ ] Add to Program.cs DI
- [ ] Build successfully
- [ ] Write/update tests
- [ ] Test manually
- [ ] Code review
- [ ] Commit with clear message

---

## ?? Pro Tips

1. **Start with smallest services** - Easier to refactor first
2. **Test incrementally** - Don't update too many at once
3. **Keep original logic** - Don't change business logic
4. **Use find-replace** - Many patterns are identical across services
5. **Pair programming** - Two people review changes
6. **Create feature branches** - One branch per service
7. **Keep commits small** - Easier to track changes

---

## ? Success Criteria

Each service update is complete when:
- ? Builds without errors
- ? Tests pass
- ? No warnings
- ? Code reviewed
- ? Follows new patterns
- ? Properly injected
- ? Async where needed
- ? Committed to git

---

## ?? Next: Create New Controllers

Once services are updated:

```csharp
[ApiController]
[Route("api/[controller]")]
public class ChatbotController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly ILogger<ChatbotController> _logger;

    public ChatbotController(
        IChatService chatService,
        ILogger<ChatbotController> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    [HttpPost("conversation")]
    public async Task<IActionResult> Conversation([FromBody] ChatRequest request)
    {
        var response = await _chatService.ProcessMessageAsync(request);
        return Ok(response);
    }
}
```

---

**Ready to start Phase 2? Pick a service and begin! ??**
