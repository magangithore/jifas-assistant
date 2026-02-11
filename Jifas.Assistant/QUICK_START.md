# ?? QUICK START - SETUP & FIRST RUN (10 minutes)

## ? Sebelum mulai, siapkan:

```
? Visual Studio or VS Code
? .NET 10 SDK installed
? Terminal/PowerShell open
? Ready to copy-paste commands
```

---

## ?? STEP 1: Initialize User Secrets (2 minutes)

```bash
# Navigate to project directory
cd Jifas.Assistant

# Initialize user secrets (one-time setup)
dotnet user-secrets init

# Expected output:
# Successfully initialized...
```

---

## ?? STEP 2: Store Your Secrets (3 minutes)

```bash
# Get your API keys ready, then run these commands:

# Store Gemini API Key
dotnet user-secrets set "Gemini:ApiKey" "AIzaSyDdTDJEIjXTPI4IoJLTTP4giavhvkR8z0k"

# Store Qdrant API Key
dotnet user-secrets set "Qdrant:ApiKey" "your-qdrant-api-key-here"

# Store Database Connection String (optional - if different from default)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=.;Database=JifasAssistant;Trusted_Connection=true;Encrypt=false"

# Verify all secrets are stored
dotnet user-secrets list

# Expected output:
# Gemini:ApiKey = ***
# Qdrant:ApiKey = ***
# ConnectionStrings:DefaultConnection = ***
```

---

## ?? STEP 3: Run Application (2 minutes)

```bash
# Run in development mode (loads user secrets + appsettings.Development.json)
dotnet run

# Or with more verbosity:
dotnet run --verbose

# Expected output:
# info: Microsoft.Hosting.Lifetime[14]
#       Now listening on: https://localhost:5001
#       Now listening on: http://localhost:5000
```

---

## ? STEP 4: Verify It's Working (2 minutes)

Open browser and check:

```
http://localhost:5000/swagger/index.html
```

You should see:
- ? Swagger UI loading
- ? API endpoints listed
- ? No configuration errors

Or check terminal:
```
info: Jifas.Assistant.Controllers.ChatbotController[0]
      API loaded successfully with configuration...
```

---

## ?? DONE! Configuration is Working

Your application is now running with:
- ? Configuration loaded from appsettings.json
- ? Secrets loaded from user-secrets
- ? Services using AppSettings
- ? Everything secured

---

## ?? QUICK TEST: Verify Configuration Loading

### Test 1: Check Configuration via API
```bash
curl http://localhost:5000/api/chatbot/test

# Expected response:
# {
#   "sender": "JIFAS AI Assistant",
#   "message": "JIFAS AI Assistant is running successfully!",
#   "version": "1.0.0",
#   "features": [...]
# }
```

### Test 2: Check Health Status
```bash
curl http://localhost:5000/health

# Expected response:
# {
#   "status": "healthy",
#   "timestamp": "2024-01-15 10:30:00"
# }
```

### Test 3: Verify Secrets Are Loaded
Add this temporary test in ChatbotController:
```csharp
[HttpGet("test-config")]
public ActionResult TestConfig()
{
    return Ok(new
    {
        geminiLoaded = !string.IsNullOrEmpty(_appSettings.Gemini.ApiKey),
        qdrantEnabled = _appSettings.Qdrant.Enabled,
        geminiModel = _appSettings.Gemini.Model
    });
}
```

Then call:
```bash
curl http://localhost:5000/api/chatbot/test-config

# Expected:
# {
#   "geminiLoaded": true,
#   "qdrantEnabled": true,
#   "geminiModel": "gemini-2.0-flash"
# }
```

---

## ?? TROUBLESHOOTING (If Something Goes Wrong)

### ? Issue: "Configuration key 'Gemini:ApiKey' not found"
**Solution:**
```bash
# Check secrets are stored
dotnet user-secrets list

# If empty, set them again
dotnet user-secrets set "Gemini:ApiKey" "your-key-here"

# Clear cache if needed
dotnet user-secrets clear
dotnet user-secrets init
```

### ? Issue: "appsettings.json not found"
**Solution:**
```bash
# Make sure you're in the right directory
cd Jifas.Assistant

# Check file exists
dir appsettings.json
# Should show: appsettings.json file
```

### ? Issue: "Port 5000 already in use"
**Solution:**
```bash
# Use different port
dotnet run --urls "http://localhost:5002"

# Or kill process using port
# Windows:
netstat -ano | findstr :5000
taskkill /PID <PID> /F

# Mac/Linux:
lsof -ti:5000 | xargs kill -9
```

### ? Issue: "Database connection failed"
**Solution:**
```bash
# Check connection string
dotnet user-secrets list | findstr ConnectionStrings

# Verify database exists
# SQL Server: Open SSMS and check database

# Update connection string if needed
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "your-correct-connection-string"
```

### ? Issue: "Still seeing configuration errors"
**Solution:**
```bash
# Clean and rebuild
dotnet clean
dotnet build

# Then run again
dotnet run
```

---

## ?? Next Steps After First Run

After verifying everything works:

1. **Read Documentation**
   - [ ] Read: CONFIGURATION_INDEX.md (5 min)
   - [ ] Read: README_CONFIGURATION.md (15 min)
   - [ ] Browse: CONFIGURATION_QUICK_REFERENCE.md

2. **Update Services**
   - [ ] Update GeminiService to use AppSettings
   - [ ] Update QdrantService to use AppSettings
   - [ ] Update other services...

3. **Run Tests**
   - [ ] dotnet test

4. **Deploy**
   - [ ] Follow IMPLEMENTATION_CHECKLIST.md

---

## ?? USEFUL COMMANDS FOR LATER

```bash
# List all user secrets
dotnet user-secrets list

# Set a new secret
dotnet user-secrets set "Key:SubKey" "value"

# Remove a secret
dotnet user-secrets remove "Key:SubKey"

# Clear all secrets
dotnet user-secrets clear

# View .env file location
dotnet user-secrets path

# Run in specific environment
dotnet run --environment Production

# Run with specific configuration
dotnet run --configuration Release
```

---

## ?? Configuration File Locations

```
Jifas.Assistant/
??? appsettings.json              ? Base config
??? appsettings.Development.json  ? Dev overrides
??? appsettings.Production.json   ? Prod config
??? .env.example                  ? Template
??? Configuration/
    ??? AppSettings.cs            ? Helper class
    ??? ConfigurationUsageExamples.cs  ? Code examples
```

---

## ? Security Reminder

```
?? NEVER commit these to Git:
? appsettings.json (dengan hardcoded secrets)
? .env files
? user-secrets files
? API keys
? Passwords

? DO commit these:
? appsettings.json (tanpa secrets)
? appsettings.Development.json
? Configuration/AppSettings.cs
? .env.example (template only)
```

---

## ?? What You Just Did

1. ? Initialized user secrets
2. ? Stored API keys securely
3. ? Ran application with configuration
4. ? Verified configuration loading
5. ? Tested endpoints

**Congratulations!** Configuration setup complete! ??

---

## ?? Need Help?

| Issue | Solution |
|-------|----------|
| Can't find a command? | Check: CONFIGURATION_QUICK_REFERENCE.md |
| Don't understand setup? | Read: README_CONFIGURATION.md |
| Something broke? | Check: VISUAL_ANALYSIS_GUIDE.md (Section 14) |
| Need code examples? | See: ConfigurationUsageExamples.cs |
| Want to learn more? | Read: CONFIGURATION_MIGRATION_ANALYSIS.md |

---

## ?? YOU'RE READY!

```
? Configuration is setup
? Secrets are secure
? Application is running
? Endpoints are working

Next: Read documentation and update your services!
```

---

**Duration:** ~10 minutes  
**Framework:** .NET 10  
**Status:** Ready to Go! ??
