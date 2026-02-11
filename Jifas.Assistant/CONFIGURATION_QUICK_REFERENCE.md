# ?? QUICK REFERENCE - CONFIGURATION IN .NET CORE

## ?? 1. SETTING UP CONFIGURATION

### Di Program.cs
```csharp
// Register configuration models
services.Configure<GeminiSettings>(configuration.GetSection("Gemini"));
services.Configure<QdrantSettings>(configuration.GetSection("Qdrant"));

// OR register AppSettings helper (recommended)
services.AddSingleton(sp => new AppSettings(configuration));
```

---

## ?? 2. CARA MENGAKSES CONFIGURATION

### ? OPTION A: IOptions<T> (Best untuk single section)
```csharp
public class MyService
{
    private readonly IOptions<GeminiSettings> _settings;
    
    public MyService(IOptions<GeminiSettings> settings)
    {
        _settings = settings;
    }
    
    public void DoSomething()
    {
        var apiKey = _settings.Value.ApiKey;
    }
}
```

### ? OPTION B: IConfiguration (Direct access)
```csharp
public class MyService
{
    private readonly IConfiguration _config;
    
    public MyService(IConfiguration config)
    {
        _config = config;
    }
    
    public void DoSomething()
    {
        var apiKey = _config["Gemini:ApiKey"];
        // atau
        var apiKey = _config.GetValue<string>("Gemini:ApiKey");
    }
}
```

### ? OPTION C: AppSettings Helper (Best untuk multiple sections)
```csharp
public class MyService
{
    private readonly AppSettings _appSettings;
    
    public MyService(AppSettings appSettings)
    {
        _appSettings = appSettings;
    }
    
    public void DoSomething()
    {
        var geminiKey = _appSettings.Gemini.ApiKey;
        var qdrantUrl = _appSettings.Qdrant.Url;
        var supportEmail = _appSettings.Support.HelpDeskEmail;
    }
}
```

---

## ?? 3. SECURING SENSITIVE DATA

### ?? WRONG ?
```json
{
  "Gemini": {
    "ApiKey": "AIzaSyDdTDJEIjXTPI4IoJLTTP4giavhvkR8z0k"
  }
}
```

### ? RIGHT (Development) ?
```bash
# Using User Secrets
dotnet user-secrets init
dotnet user-secrets set "Gemini:ApiKey" "AIzaSyDdTDJEIjXTPI4IoJLTTP4giavhvkR8z0k"
```

### ? RIGHT (Production) ?
```bash
# Using Environment Variables
$env:Gemini__ApiKey = "your-api-key"

# Or setup in appsettings.json
{
  "Gemini": {
    "ApiKey": "${GEMINI__APIKEY}"
  }
}
```

---

## ?? 4. CONFIGURATION FILES STRUCTURE

```
Jifas.Assistant/
??? appsettings.json              # Default settings
??? appsettings.Development.json  # Development overrides
??? appsettings.Production.json   # Production overrides
??? appsettings.Staging.json      # Staging overrides
??? .env.example                  # Template untuk environment variables
??? Configuration/
    ??? AppSettings.cs            # Helper class untuk access config
    ??? ConfigurationUsageExamples.cs
```

---

## ??? 5. ACCESSING DIFFERENT SETTINGS

### Gemini Configuration
```csharp
var geminiKey = _appSettings.Gemini.ApiKey;
var geminiModel = _appSettings.Gemini.Model;
var geminiBaseUrl = _appSettings.Gemini.BaseUrl;
```

### Qdrant Configuration
```csharp
if (_appSettings.Qdrant.Enabled)
{
    var url = _appSettings.Qdrant.Url;
    var apiKey = _appSettings.Qdrant.ApiKey;
    var collectionName = _appSettings.Qdrant.CollectionName;
}
```

### Knowledge Base Configuration
```csharp
var maxDocs = _appSettings.KnowledgeBase.MaxDocumentsPerSearch;
var minScore = _appSettings.KnowledgeBase.MinRelevanceScore;
var useQdrant = _appSettings.KnowledgeBase.UseQdrant;
```

### Caching Configuration
```csharp
var cacheDuration = _appSettings.Caching.DefaultDurationMinutes;
var enableKBCache = _appSettings.Caching.EnableKBCache;
var cacheExpiry = TimeSpan.FromMinutes(cacheDuration);
```

### Support Configuration
```csharp
var supportEmail = _appSettings.Support.HelpDeskEmail;
var supportPhone = _appSettings.Support.HelpDeskPhone;
var department = _appSettings.Support.Department;
```

---

## ?? 6. ENVIRONMENT-SPECIFIC SETTINGS

### Development (appsettings.Development.json)
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  },
  "Performance": {
    "SlowOperationThresholdMs": 2000
  }
}
```

### Production (appsettings.Production.json)
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning"
    }
  },
  "Performance": {
    "SlowOperationThresholdMs": 500
  }
}
```

### How it works
```csharp
// ASP.NET Core automatically loads:
// 1. appsettings.json (base)
// 2. appsettings.{Environment}.json (overrides)
// 3. Environment variables (highest priority)

// Environment dipilih dari ASPNETCORE_ENVIRONMENT
// Development: dotnet run
// Production: dotnet run --environment Production
```

---

## ?? 7. RELOADING CONFIGURATION TANPA RESTART

### Using IOptionsSnapshot<T>
```csharp
public class MyService
{
    private readonly IOptionsSnapshot<GeminiSettings> _settings;
    
    public MyService(IOptionsSnapshot<GeminiSettings> settings)
    {
        _settings = settings; // Bisa berubah tanpa restart
    }
    
    public void DoSomething()
    {
        var currentValue = _settings.Value.ApiKey; // Always latest
    }
}
```

---

## ?? 8. CONFIGURATION PRIORITY (Highest to Lowest)

1. ?? **Environment Variables** (Highest priority)
   ```bash
   $env:Gemini__ApiKey = "value"
   ```

2. ?? **User Secrets** (Development only)
   ```bash
   dotnet user-secrets set "Gemini:ApiKey" "value"
   ```

3. ?? **appsettings.{Environment}.json**
   ```json
   { "Gemini": { "ApiKey": "value" } }
   ```

4. ?? **appsettings.json** (Lowest priority)
   ```json
   { "Gemini": { "ApiKey": "default_value" } }
   ```

---

## ?? 9. TESTING WITH CONFIGURATION

### Mock Configuration
```csharp
[Test]
public void TestWithConfiguration()
{
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string>
        {
            {"Gemini:ApiKey", "test-key"},
            {"Gemini:Model", "test-model"}
        })
        .Build();
    
    var appSettings = new AppSettings(config);
    Assert.AreEqual("test-key", appSettings.Gemini.ApiKey);
}
```

---

## ? 10. CHECKLIST - READY FOR PRODUCTION

- [ ] API Keys moved to environment variables
- [ ] Database passwords moved to environment variables
- [ ] Sensitive data not in appsettings.json
- [ ] appsettings.Production.json configured correctly
- [ ] User Secrets setup for development
- [ ] Azure Key Vault setup for production (if applicable)
- [ ] Configuration models created (GeminiSettings, QdrantSettings, etc.)
- [ ] AppSettings helper registered in DI container
- [ ] All services using IOptions<T> or AppSettings
- [ ] Health checks configured
- [ ] CORS configured
- [ ] Logging configured with proper log levels

---

## ?? 11. RUNNING WITH DIFFERENT ENVIRONMENTS

### Development
```bash
dotnet run
# Uses: appsettings.json + appsettings.Development.json
```

### Production
```bash
dotnet run --environment Production
# Uses: appsettings.json + appsettings.Production.json
# Plus: Environment variables
```

### With Custom Environment Variables
```bash
# Windows PowerShell
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:Gemini__ApiKey = "your-api-key"
dotnet run

# Linux/Mac
export ASPNETCORE_ENVIRONMENT=Production
export Gemini__ApiKey="your-api-key"
dotnet run
```

---

## ?? USEFUL REFERENCES

- [Microsoft Docs: Configuration](https://docs.microsoft.com/aspnet/core/fundamentals/configuration)
- [IOptions Pattern](https://docs.microsoft.com/aspnet/core/fundamentals/configuration/options)
- [User Secrets](https://docs.microsoft.com/aspnet/core/security/app-secrets)
- [Azure Key Vault Provider](https://docs.microsoft.com/azure/azure-app-configuration/setup-azure-app-configuration-aspnet)

---

**Last Updated:** 2024  
**Framework:** .NET 10
