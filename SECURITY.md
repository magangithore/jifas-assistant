# 🔒 JIFAS AI Assistant - Security Guidelines

## ⚠️ Critical Security Alert

**API Keys have been found exposed in repository. IMMEDIATE ACTION REQUIRED:**

1. **Google Gemini API Key**: Found in `.env` and `appsettings.Development.json`
   - **Status**: Replaced with placeholder `YOUR_GOOGLE_GEMINI_API_KEY_HERE`
   - **Action Required**: 
     - If you were using this key, **IMMEDIATELY REVOKE** it from Google Cloud Console
     - Generate a new API key: https://ai.google.dev
     - Never commit actual keys to repository

2. **Connection Strings**: Verify database credentials are not hardcoded in production config

## 🔐 How to Securely Manage Credentials

### Option 1: Environment Variables (Recommended for Development)

Set environment variables before running the application:

**PowerShell (Windows):**
```powershell
$env:Gemini__ApiKey = "your-actual-api-key-here"
$env:ConnectionStrings__DefaultConnection = "your-connection-string"
dotnet run
```

**Bash/Linux:**
```bash
export Gemini__ApiKey="your-actual-api-key-here"
export ConnectionStrings__DefaultConnection="your-connection-string"
dotnet run
```

### Option 2: User Secrets (Recommended for Development)

Store secrets locally without committing to repository:

```bash
cd Jifas.Assistant

# Initialize user secrets (one-time)
dotnet user-secrets init

# Set secrets
dotnet user-secrets set "Gemini:ApiKey" "your-actual-api-key"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "your-connection-string"

# View configured secrets
dotnet user-secrets list
```

User secrets are stored in:
- Windows: `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`
- Linux/Mac: `~/.microsoft/usersecrets/<UserSecretsId>/secrets.json`

The `UserSecretsId` for this project is: `b375d8c9-0269-41b2-bece-00556711b0b1`

### Option 3: Azure Key Vault (Recommended for Production)

```csharp
builder.Configuration
    .AddAzureKeyVault(
        new Uri($"https://{keyVaultName}.vault.azure.net/"),
        new DefaultAzureCredential());
```

### Option 4: .env File (Local Development Only)

1. Create `.env` file in project root with actual credentials
2. Ensure `.env` is in `.gitignore` (it already is)
3. Use DotNet.Env or similar package to load .env file

## 📋 Security Checklist

- [ ] Never commit actual API keys to repository
- [ ] Always use `.env` or environment variables for local development
- [ ] Use `dotnet user-secrets` for development credentials
- [ ] Use Azure Key Vault or similar for production secrets
- [ ] Rotate API keys regularly (at least quarterly)
- [ ] Monitor API key usage in Google Cloud Console
- [ ] Enable rate limiting on APIs to prevent abuse
- [ ] Use different keys for Development, Staging, and Production
- [ ] Add `.env` and `secrets.json` to `.gitignore` (already done)

## 🔍 Checking for Exposed Secrets

To scan repository for accidentally committed secrets:

```bash
# Using git-secrets
git secrets --scan

# Using TruffleHog
pip install truffleHog
truffleHog filesystem . --json
```

Add pre-commit hook to prevent accidental commits:

```bash
cd .git/hooks
# Add a script to run git-secrets before commit
```

## 📝 Configuration Files

| File | Purpose | Contains Secrets? | .gitignore? |
|------|---------|-------------------|-------------|
| `appsettings.json` | Default config | ❌ No | ❌ Committed |
| `appsettings.Development.json` | Dev config | ⚠️ Placeholder only | ⚠️ Committed |
| `appsettings.Production.json` | Prod config | ❌ No | ❌ Committed |
| `.env` | Environment variables | ✅ YES | ✅ Ignored |
| `.env.example` | Template | ❌ No | ❌ Committed |

## 🚨 If You Discover an Exposed Key

1. **Immediately rotate** the key in the service console
2. **Search history** to find when it was added:
   ```bash
   git log -p -S "exposed-key" -- .
   ```
3. **Remove from history** (if critical):
   ```bash
   git filter-branch --force --index-filter 'git rm --cached --ignore-unmatch appsettings.Development.json' HEAD
   ```
4. **Force push** to repository (notify team)
5. **Audit logs** to check if key was used

## 🔗 References

- [Google Gemini API Keys](https://ai.google.dev)
- [Microsoft User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets)
- [Azure Key Vault with .NET](https://learn.microsoft.com/azure/key-vault/general/quick-start-net)
- [OWASP: Secrets Management](https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html)

---

**Last Updated**: February 2026
**Status**: 🟢 Credentials Secured
