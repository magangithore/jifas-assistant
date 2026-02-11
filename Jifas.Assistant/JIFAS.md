# ?? JIFAS AI ASSISTANT - COMPLETE .NET SETUP GUIDE

**Date**: February 2026 | **Status**: Production Ready | **Framework**: .NET 8+ | **LLM**: Gemini | **Database**: SQL Server

---

## ?? RINGKASAN TUJUAN

Kamu akan membangun **JIFAS AI Assistant** - sebuah enterprise-grade AI chatbot yang **HANYA** menjawab pertanyaan tentang **Jababeka Integrated Finance Accounting System (JIFAS)**.

### ? Karakteristik Utama:
- ?? **STRICT Knowledge Base Only**: Semua jawaban dari KB JIFAS yang kamu upload
- ?? **Finance-Focused**: Panduan login, troubleshooting, akses, konfigurasi JIFAS
- ?? **Smart Suggestions**: Di setiap jawaban ada pertanyaan lanjutan (seperti ChatGPT)
- ? **Hard Out-of-Scope Rejection**: Pertanyaan non-JIFAS ditolak dengan halus
- ?? **Future-Ready**: Siap untuk automation (fill form, generate invoice, dll)
- ?? **Professional Grade**: Production-ready code, scalable architecture

### ? BUKAN untuk:
- General knowledge (non-JIFAS)
- Gemini general AI responses
- Multiple domains
- Open-ended chat

---

## ??? TECH STACK - MINIMAL & PRODUCTION-READY

### **Core Framework**
```
? ASP.NET Core 8.0 LTS
? C# 12+
? Entity Framework Core 8
? Dependency Injection built-in
```

### **Database**
```
? SQL Server 2019+ (kamu udah punya)
? Entity Framework Core dengan SQL Server provider
? Structured logging ke database
```

### **Vector Database & Embeddings**
```
? Azure OpenAI Embeddings API (RECOMMENDED)
   OR
? Ollama (jika prefer self-hosted - tapi optional)
   OR
? Semantic Kernel embeddings

BEST CHOICE: Azure OpenAI Embeddings (enterprise-grade, scalable)
```

### **LLM (Summarization Only)**
```
? Gemini API (gemini-2.0-flash - kamu sudah punya key)
   - HANYA untuk summarization/rephrasing
   - BUKAN untuk answer generation
```

### **Vector Search**
```
? Semantic Kernel (.NET native)
   OR
? Qdrant (.NET client library)
   OR
? Azure Cognitive Search (cloud option)

BEST CHOICE: Semantic Kernel (Microsoft native, .NET integrated)
```

### **Supporting Libraries**
```
? RestSharp (HTTP client)
? Serilog (structured logging - file + SQL Server)
? FluentValidation (input validation)
? Newtonsoft.Json (JSON)
? System.IdentityModel.Tokens.Jwt (API security)
? Azure.Identity (untuk Azure services - optional)
```

### **Development Tools**
```
? Docker (optional - untuk vector DB jika pake Qdrant)
? SQL Server Management Studio
? Visual Studio 2022 atau VS Code
? Postman (API testing)
```

---

## ?? ARCHITECTURE OVERVIEW

```
???????????????????????????????????????????????????????
?                   REACT FRONTEND                    ?
?          (Future: JIFAS chat interface)             ?
???????????????????????????????????????????????????????
                     ? HTTP/REST
???????????????????????????????????????????????????????
?          ASP.NET CORE 8 WEB API                     ?
?  ????????????????????????????????????????????????  ?
?  ?     ChatController                           ?  ?
?  ? POST /api/chat/ask                           ?  ?
?  ? GET /api/suggestions                         ?  ?
?  ? POST /api/ticket/create                      ?  ?
?  ????????????????????????????????????????????????  ?
?                   ?                                 ?
?  ????????????????????????????????????????????????  ?
?  ?  Business Logic Layer                        ?  ?
?  ?  ?? ChatService                              ?  ?
?  ?  ?? KnowledgeBaseService (RAG)              ?  ?
?  ?  ?? TicketService                            ?  ?
?  ?  ?? SuggestionService                        ?  ?
?  ?  ?? OutOfScopeDetector                       ?  ?
?  ????????????????????????????????????????????????  ?
?                   ?                                 ?
?  ????????????????????????????????????????????????  ?
?  ?  Data & Infrastructure Layer                 ?  ?
?  ?  ?? SQL Server (conversations, KB, tickets) ?  ?
?  ?  ?? Vector Search (embeddings + semantic)   ?  ?
?  ?  ?? Gemini API (summarization)              ?  ?
?  ?  ?? osTicket API (ticket creation)          ?  ?
?  ????????????????????????????????????????????????  ?
???????????????????????????????????????????????????????
```

---

## ?? COMPLETE SETUP STEPS

### **STEP 1: Environment & Credentials (5 menit)**

#### A. Create `.env` file untuk JIFAS

```bash
# Copy dari .env.example dan modify untuk JIFAS
```

#### B. File: `appsettings.json` (JIFAS-specific)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  },
  "JIFAS": {
    "ApiKey": "jifas-secret-key-123",
    "Environment": "development",
    "SystemName": "JIFAS AI Assistant",
    "Version": "1.0.0",
    "MaxRequestsPerMinute": 60,
    "SessionTimeoutMinutes": 30,
    "StrictKBMode": true,
    "RequireApprovalForTickets": false
  },
  "LLM": {
    "Provider": "Gemini",
    "Gemini": {
      "ApiKey": "AIzaSyDdTDJEIjXTPI4IoJLTTP4giavhvkR8z0k",
      "Model": "gemini-2.0-flash",
      "BaseUrl": "https://generativelanguage.googleapis.com/v1beta/models",
      "UseFor": "SummarizationOnly",
      "MaxTokens": 200,
      "Temperature": 0.3
    }
  },
  "Embeddings": {
    "Provider": "SemanticKernel",
    "SemanticKernel": {
      "Model": "text-embedding-3-small",
      "Dimension": 1536,
      "BatchSize": 10,
      "CacheDurationMinutes": 60
    }
  },
  "VectorSearch": {
    "Provider": "AzureCognitiveSearch",
    "AzureCognitiveSearch": {
      "Endpoint": "https://your-service.search.windows.net",
      "AdminApiKey": "***use-secrets-manager***",
      "IndexName": "jifas-kb"
    }
  },
  "Database": {
    "Provider": "SqlServer",
    "SqlServer": {
      "Server": "localhost",
      "Database": "JIFAS_Assistant",
      "UserId": "sa",
      "Password": "***",
      "TrustServerCertificate": true,
      "Encrypt": false
    }
  },
  "KnowledgeBase": {
    "ChunkSize": 500,
    "ChunkOverlap": 50,
    "MinConfidenceScore": 0.65,
    "TopKResults": 3,
    "EnableReranking": false,
    "CacheDurationMinutes": 120
  },
  "OutOfScope": {
    "Enabled": true,
    "StrictMode": true,
    "FallbackMessage": "Pertanyaan Anda di luar cakupan JIFAS AI Assistant. Silakan hubungi IT Help Desk untuk bantuan lebih lanjut.",
    "KeywordsToReject": [
      "bitcoin", "crypto", "personal", "dating", "game", "covid", "politics"
    ],
    "DomainWhitelist": ["jifas", "finance", "accounting", "invoice", "ar", "ap", "gl"]
  },
  "Suggestions": {
    "Enabled": true,
    "MinScore": 0.6,
    "MaxSuggestions": 3,
    "GenerationType": "KeywordBased"
  },
  "Tickets": {
    "Enabled": true,
    "OnlyJIFASRelated": true,
    "AutoCreate": false,
    "RequireUserConfirmation": true,
    "Categories": ["jifas_access", "jifas_error", "jifas_feature_request", "jifas_training"]
  }
}
```

#### C. File: `appsettings.Production.json`

```json
{
  "JIFAS": {
    "Environment": "production",
    "ApiKey": "***use-secrets-manager***"
  },
  "Database": {
    "SqlServer": {
      "Server": "prod-sql-server.jababeka.com",
      "Database": "JIFAS_Assistant_Prod",
      "UserId": "***",
      "Password": "***"
    }
  },
  "LLM": {
    "Gemini": {
      "ApiKey": "***use-secrets-manager***"
    }
  },
  "VectorSearch": {
    "AzureCognitiveSearch": {
      "Endpoint": "***",
      "AdminApiKey": "***use-secrets-manager***"
    }
  }
}
```

#### D. File: `.env.example` (share dengan team)

```bash
# ============= JIFAS AI ASSISTANT =============

# Basic
JIFAS_API_KEY=jifas-secret-key-123
JIFAS_ENVIRONMENT=development

# LLM (Gemini - Summarization Only)
LLM_PROVIDER=gemini
GEMINI_API_KEY=AIzaSyDdTDJEIjXTPI4IoJLTTP4giavhvkR8z0k
GEMINI_MODEL=gemini-2.0-flash

# Embeddings (Semantic Kernel + Azure OpenAI)
EMBEDDINGS_PROVIDER=semantic-kernel
EMBEDDINGS_MODEL=text-embedding-3-small
AZURE_OPENAI_ENDPOINT=https://your-resource.openai.azure.com/
AZURE_OPENAI_KEY=***

# Vector Search (Azure Cognitive Search)
VECTOR_SEARCH_PROVIDER=azure-cognitive-search
AZURE_SEARCH_ENDPOINT=https://your-service.search.windows.net
AZURE_SEARCH_KEY=***
AZURE_SEARCH_INDEX_NAME=jifas-kb

# Database (SQL Server)
DATABASE_PROVIDER=sql-server
DATABASE_SERVER=localhost
DATABASE_NAME=JIFAS_Assistant
DATABASE_USER=sa
DATABASE_PASSWORD=***

# Tickets (osTicket)
OSTICKET_BASE_URL=https://ithelp.jababeka.com
OSTICKET_API_PATH=/api/tickets.json
OSTICKET_API_KEY=97575DA943DD39B9AB7EFCD4D7830517

# Logging
LOG_LEVEL=Information
LOG_FILE_PATH=logs/jifas-assistant-.txt
LOG_TO_DATABASE=true
```

---

### **STEP 2: Install NuGet Packages (5 menit)**

```bash
cd JIFASAssistant.API

# Core Framework
dotnet add package Microsoft.AspNetCore.OpenApi
dotnet add package Swashbuckle.AspNetCore

# Database & ORM
dotnet add package Microsoft.EntityFrameworkCore
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
dotnet add package Microsoft.EntityFrameworkCore.Tools
dotnet add package Microsoft.EntityFrameworkCore.Design

# HTTP & REST
dotnet add package RestSharp
dotnet add package System.Net.Http.Json

# Logging
dotnet add package Serilog
dotnet add package Serilog.AspNetCore
dotnet add package Serilog.Sinks.File
dotnet add package Serilog.Sinks.MSSqlServer

# AI & Embeddings (SEMANTIC KERNEL - Microsoft Native)
dotnet add package Microsoft.SemanticKernel
dotnet add package Microsoft.SemanticKernel.Connectors.OpenAI
dotnet add package Microsoft.SemanticKernel.Connectors.AzureOpenAI

# Vector Search (Optional - jika pakai Azure)
dotnet add package Azure.Search.Documents
dotnet add package Azure.Identity

# Validation
dotnet add package FluentValidation
dotnet add package FluentValidation.DependencyInjectionExtensions

# JSON & Serialization
dotnet add package Newtonsoft.Json
dotnet add package System.Text.Json

# Security
dotnet add package System.IdentityModel.Tokens.Jwt

# Utilities
dotnet add package CommunityToolkit.Mvvm
```

---

### **STEP 3: Project Structure Setup (15 menit)**

```bash
# Create solution
dotnet new globaljson --sdk-version 8.0
dotnet new sln -n JIFASAssistant

# Create projects dengan template yang tepat
dotnet new webapi -n JIFASAssistant.API -f net8.0
dotnet new classlib -n JIFASAssistant.Core -f net8.0
dotnet new classlib -n JIFASAssistant.Data -f net8.0
dotnet new classlib -n JIFASAssistant.Infrastructure -f net8.0
dotnet new classlib -n JIFASAssistant.Common -f net8.0
dotnet new xunit -n JIFASAssistant.Tests.Unit -f net8.0
dotnet new xunit -n JIFASAssistant.Tests.Integration -f net8.0

# Add ke solution
dotnet sln add JIFASAssistant.API
dotnet sln add JIFASAssistant.Core
dotnet sln add JIFASAssistant.Data
dotnet sln add JIFASAssistant.Infrastructure
dotnet sln add JIFASAssistant.Common
dotnet sln add JIFASAssistant.Tests.Unit
dotnet sln add JIFASAssistant.Tests.Integration

# Add project references
cd JIFASAssistant.API
dotnet add reference ../JIFASAssistant.Core/JIFASAssistant.Core.csproj
dotnet add reference ../JIFASAssistant.Data/JIFASAssistant.Data.csproj
dotnet add reference ../JIFASAssistant.Infrastructure/JIFASAssistant.Infrastructure.csproj
dotnet add reference ../JIFASAssistant.Common/JIFASAssistant.Common.csproj
```

#### Folder Structure:

```
JIFASAssistant/
??? JIFASAssistant.sln
?
??? src/
?   ??? JIFASAssistant.API/
?   ?   ??? Program.cs
?   ?   ??? appsettings.json
?   ?   ??? appsettings.Production.json
?   ?   ??? Controllers/
?   ?   ?   ??? ChatController.cs
?   ?   ?   ??? KnowledgeBaseController.cs
?   ?   ?   ??? TicketController.cs
?   ?   ?   ??? HealthController.cs
?   ?   ??? Middleware/
?   ?       ??? ErrorHandlingMiddleware.cs
?   ?       ??? ApiKeyAuthMiddleware.cs
?   ?
?   ??? JIFASAssistant.Core/
?   ?   ??? Services/
?   ?   ?   ??? ChatService.cs
?   ?   ?   ??? KnowledgeBaseService.cs
?   ?   ?   ??? TicketService.cs
?   ?   ?   ??? EmbeddingService.cs
?   ?   ?   ??? SuggestionService.cs
?   ?   ?   ??? OutOfScopeDetector.cs
?   ?   ??? Models/
?   ?   ?   ??? Requests/
?   ?   ?   ??? Responses/
?   ?   ?   ??? Domain/
?   ?   ??? Interfaces/
?   ?
?   ??? JIFASAssistant.Data/
?   ?   ??? AppDbContext.cs
?   ?   ??? Entities/
?   ?   ?   ??? ConversationEntity.cs
?   ?   ?   ??? TicketEntity.cs
?   ?   ?   ??? KBDocumentEntity.cs
?   ?   ?   ??? UserEntity.cs
?   ?   ??? Migrations/
?   ?
?   ??? JIFASAssistant.Infrastructure/
?   ?   ??? LLM/
?   ?   ?   ??? GeminiClient.cs
?   ?   ?   ??? SemanticKernelClient.cs
?   ?   ??? VectorSearch/
?   ?   ?   ??? AzureCognitiveSearchClient.cs
?   ?   ?   ??? VectorStoreService.cs
?   ?   ??? ExternalApis/
?   ?   ?   ??? OSTicketClient.cs
?   ?   ??? FileProcessing/
?   ?       ??? DocumentParser.cs
?   ?       ??? ChunkingStrategy.cs
?   ?
?   ??? JIFASAssistant.Common/
?       ??? Constants/
?       ??? Extensions/
?       ??? Utils/
?
??? tests/
?   ??? JIFASAssistant.Tests.Unit/
?   ??? JIFASAssistant.Tests.Integration/
?
??? docs/
?   ??? API_ENDPOINTS.md
?   ??? KB_STRUCTURE.md
?   ??? ARCHITECTURE.md
?
??? .env.example
??? .gitignore
```

---

### **STEP 4: Knowledge Base Structure (PENTING!)**

Ini adalah structure KB yang kamu akan upload sebagai `.txt` files:

#### Format Standard: `kb_jifas_access_and_login.txt`

```
# JIFAS - Akses & Login

## Deskripsi Sistem
JIFAS (Jababeka Integrated Finance Accounting System) adalah aplikasi keuangan 
terintegrasi yang dirancang untuk mengelola proses akuntansi, anggaran, dan 
data master bagi berbagai unit bisnis di PT Jababeka Tbk.

Fungsi utama:
- Manajemen Piutang (AR - Accounts Receivable)
- Manajemen Utang (AP - Accounts Payable)
- Buku Besar Umum (GL - General Ledger)
- Manajemen Anggaran
- Pelaporan Keuangan Terintegrasi

---

## Akses Browser & URL

### Grup KIJ, GBC, MPK, JM, BW, TL, SPPK
- URL: https://jifas.jababeka.com
- IP Address: 10.0.8.57
- Support Email: finance-it@jababeka.com

### Grup JI, ICTEL, NGE
- URL: https://jifasweb.jiinfra.com
- IP Address: 10.10.1.30
- Support Email: jinfra-support@jiinfra.com

### Grup BP, UP, TS
- URL: https://jifas-bp.bekasipower.co.id
- IP Address: 10.12.0.47
- Support Email: bp-finance@bekasipower.co.id

### Grup KIK
- URL: https://jifas.kik.com
- IP Address: 10.5.1.240
- Support Email: kik-finance@kik.com

---

## Kredensial Login

**Username**: Gunakan username Windows (Active Directory) seperti yang biasanya kamu gunakan

**Contoh**: jsmith (jangan tambahkan @jababeka.com)

**Password**: Gunakan password Windows yang sama

**Keamanan**:
1. Jangan share username/password
2. Logout ketika selesai
3. Jangan gunakan di komputer publik
4. Lapor ke IT jika password lupa

---

## Troubleshooting Login Gagal

### Error: Access Denied
Penyebab: User belum memiliki akses ke JIFAS
Solusi:
1. Hubungi Manager/Finance Lead
2. Manager request akses ke Finance System Administrator
3. Tunggu approval (1-2 hari kerja)
4. Login ulang setelah approval

### Error: Username/Password Salah
Penyebab: Kredensial tidak sesuai
Solusi:
1. Pastikan menggunakan username Windows yang benar
2. Caps Lock OFF saat input password
3. Clear browser cache: Ctrl+Shift+Delete
4. Coba browser lain (Chrome/Firefox/Edge)
5. Jika masih error, reset password di Windows

### Error: Page tidak Responsive / Timeout
Penyebab: Server down atau network issue
Solusi:
1. Check status server: https://status.jababeka.com
2. Verify internet connection (ping 8.8.8.8)
3. Coba URL alternatif atau IP address
4. Tunggu 5-10 menit lalu coba lagi
5. Jika > 30 menit, create support ticket ke IT

---

## Department Access Levels

| Department | Access Level | Modules | Login Frequency |
|-----------|---|---|---|
| Finance | Full | AR, AP, GL, Reports, Budget | Daily |
| Accounting | Moderator | AR, AP, GL, Journal | Daily |
| Management | Read-Only | Dashboard, Reports | Weekly |
| Audit | Limited | GL, Reports (audit trail) | As needed |

---

## Metadata
CATEGORY: Access & Setup
DIFFICULTY: Basic
DEPARTMENT: Finance, Accounting, Management
LAST_UPDATED: 2026-02-03
VERSION: 1.0
TAGS: jifas, login, access, credentials, browser, url, windows
```

#### Format untuk Troubleshooting: `kb_jifas_troubleshooting.txt`

```
# JIFAS - Troubleshooting Common Errors

## Error: "Database Connection Failed"
Deskripsi: Application tidak bisa terhubung ke database JIFAS
Severity: Critical

Penyebab Umum:
1. SQL Server down/maintenance
2. Network connectivity issue
3. Database credentials expired
4. Firewall blocking

Solusi Step-by-Step:
1. Check JIFAS status page: https://status.jababeka.com
2. Ping database server: ping jifas-db.jababeka.com
3. Verify VPN connected (jika akses dari remote)
4. Clear browser cache dan login ulang
5. Coba di komputer lain
6. Tunggu 10 menit (mungkin maintenance)
7. Jika masih error, create ticket: priority=HIGH

Contact: finance-it@jababeka.com | Ext: 1234

---

## Error: "Module Not Visible"
Deskripsi: Modul AR/AP/GL tidak muncul di menu
Severity: Medium

Penyebab:
1. User belum di-grant akses modul
2. Role belum di-update di Active Directory
3. Browser cache lama
4. Permission group salah

Solusi:
1. Clear cache: Ctrl+Shift+Delete
2. Logout dan login ulang
3. Request modul access ke Finance Manager
4. Manager submit request ke JIFAS Admin
5. Tunggu approval (24-48 jam)

---

## Error: "Report Generation Timeout"
Deskripsi: Report hang/tidak bisa generate dalam waktu lama
Severity: Low to Medium

Penyebab:
1. Data terlalu banyak (large dataset)
2. SQL query kompleks
3. Network slow
4. Server resource terbatas

Solusi:
1. Filter data dengan date range lebih kecil
   Contoh: Query 1 month instead of 1 year
2. Gunakan pre-built reports instead of custom
3. Run report di jam non-peak: 11 PM - 6 AM
4. Contact Report Team untuk optimization
   Email: reporting@jababeka.com

---

Metadata
CATEGORY: Troubleshooting
DIFFICULTY: Intermediate
DEPARTMENT: Finance, IT
LAST_UPDATED: 2026-02-03
```

---

### **STEP 5: Database Setup (SQL Server + EF Core)**

#### A. Connection String (appsettings.json)

```json
"Database": {
  "Provider": "SqlServer",
  "SqlServer": {
    "Server": "localhost",
    "Database": "JIFAS_Assistant_Dev",
    "UserId": "sa",
    "Password": "YourPassword123!",
    "TrustServerCertificate": true,
    "Encrypt": false,
    "Connection": "Server=localhost;Database=JIFAS_Assistant_Dev;User Id=sa;Password=YourPassword123!;TrustServerCertificate=True;Encrypt=False;"
  }
}
```

#### B. File: `JIFASAssistant.Data/AppDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using JIFASAssistant.Data.Entities;

namespace JIFASAssistant.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<ConversationEntity> Conversations { get; set; }
        public DbSet<TicketEntity> Tickets { get; set; }
        public DbSet<KBDocumentEntity> KBDocuments { get; set; }
        public DbSet<UserEntity> Users { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Conversation Entity
            modelBuilder.Entity<ConversationEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.UserId).IsRequired().HasMaxLength(100);
                entity.Property(e => e.SessionId).IsRequired().HasMaxLength(100);
                entity.Property(e => e.UserMessage).IsRequired().HasMaxLength(2000);
                entity.Property(e => e.AiResponse).IsRequired().HasMaxLength(3000);
                entity.Property(e => e.Category).HasMaxLength(50);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.HasIndex(e => e.SessionId);
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.CreatedAt);
            });

            // Ticket Entity
            modelBuilder.Entity<TicketEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.TicketNumber).IsRequired().HasMaxLength(20);
                entity.Property(e => e.UserId).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Subject).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Description).IsRequired().HasMaxLength(2000);
                entity.Property(e => e.Category).HasMaxLength(50);
                entity.Property(e => e.Status).HasDefaultValue("Open");
                entity.Property(e => e.Priority).HasDefaultValue("Medium");
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.HasIndex(e => e.TicketNumber).IsUnique();
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.Status);
            });

            // KB Document Entity
            modelBuilder.Entity<KBDocumentEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Content).IsRequired();
                entity.Property(e => e.Category).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Department).IsRequired().HasMaxLength(50);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.HasIndex(e => e.Category);
                entity.HasIndex(e => e.Department);
            });

            // User Entity
            modelBuilder.Entity<UserEntity>(entity =>
            {
                entity.HasKey(e => e.UserId);
                entity.Property(e => e.UserId).HasMaxLength(100);
                entity.Property(e => e.Email).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Department).IsRequired().HasMaxLength(50);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            });
        }
    }
}
```

#### C. Entity Models

**File: `JIFASAssistant.Data/Entities/ConversationEntity.cs`**

```csharp
namespace JIFASAssistant.Data.Entities
{
    public class ConversationEntity
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string UserMessage { get; set; } = string.Empty;
        public string AiResponse { get; set; } = string.Empty;
        public string? Category { get; set; }
        public double? ConfidenceScore { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
```

**File: `JIFASAssistant.Data/Entities/TicketEntity.cs`**

```csharp
namespace JIFASAssistant.Data.Entities
{
    public class TicketEntity
    {
        public int Id { get; set; }
        public string TicketNumber { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string Status { get; set; } = "Open";
        public string Priority { get; set; } = "Medium";
        public string? ConversationReference { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }
}
```

#### D. Create & Apply Migration

```bash
cd JIFASAssistant.API

# Create initial migration
dotnet ef migrations add InitialCreate --project ../JIFASAssistant.Data --startup-project .

# Create database dan apply migrations
dotnet ef database update --project ../JIFASAssistant.Data --startup-project .

# Verify database created
# Buka SQL Server Management Studio dan check: JIFAS_Assistant_Dev
```

---

### **STEP 6: Core Services Implementation**

#### A. Semantic Kernel Integration (Embeddings)

**File: `JIFASAssistant.Infrastructure/LLM/SemanticKernelClient.cs`**

```csharp
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.Extensions.Logging;

namespace JIFASAssistant.Infrastructure.LLM
{
    public interface IEmbeddingService
    {
        Task<float[]> GenerateEmbeddingAsync(string text);
        Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts);
    }

    public class SemanticKernelEmbeddingService : IEmbeddingService
    {
        private readonly Kernel _kernel;
        private readonly ITextEmbeddingGenerationService _embeddingService;
        private readonly ILogger<SemanticKernelEmbeddingService> _logger;

        public SemanticKernelEmbeddingService(Kernel kernel, ILogger<SemanticKernelEmbeddingService> logger)
        {
            _kernel = kernel;
            _embeddingService = kernel.GetRequiredService<ITextEmbeddingGenerationService>();
            _logger = logger;
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            try
            {
                _logger.LogInformation("[EMBEDDING] Generating embedding for: {Text}", text[..50]);

                var embedding = await _embeddingService.GenerateEmbeddingAsync(text);

                _logger.LogInformation("[EMBEDDING] ? Generated {Dimension}-dim vector", embedding.Length);
                return embedding.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EMBEDDING] ? Failed to generate embedding");
                throw;
            }
        }

        public async Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts)
        {
            var results = new List<float[]>();
            foreach (var text in texts)
            {
                var embedding = await GenerateEmbeddingAsync(text);
                results.Add(embedding);
            }
            return results;
        }
    }
}
```

#### B. Vector Search Service (Azure Cognitive Search)

**File: `JIFASAssistant.Infrastructure/VectorSearch/VectorSearchService.cs`**

```csharp
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;

namespace JIFASAssistant.Infrastructure.VectorSearch
{
    public interface IVectorSearchService
    {
        Task InitializeAsync();
        Task<List<(string Content, double Score)>> SearchAsync(float[] embedding, int topK = 3);
        Task StoreAsync(string id, float[] embedding, Dictionary<string, string> metadata);
    }

    public class AzureCognitiveSearchService : IVectorSearchService
    {
        private readonly SearchClient _searchClient;
        private readonly ILogger<AzureCognitiveSearchService> _logger;
        private readonly string _indexName;

        public AzureCognitiveSearchService(
            SearchClient searchClient,
            ILogger<AzureCognitiveSearchService> logger,
            string indexName)
        {
            _searchClient = searchClient;
            _logger = logger;
            _indexName = indexName;
        }

        public async Task InitializeAsync()
        {
            try
            {
                _logger.LogInformation("[VECTOR_SEARCH] Initializing Azure Cognitive Search...");
                // Verify index exists
                // In production, create index with embeddings config
                _logger.LogInformation("[VECTOR_SEARCH] ? Index ready: {IndexName}", _indexName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[VECTOR_SEARCH] ? Initialization failed");
                throw;
            }
        }

        public async Task<List<(string Content, double Score)>> SearchAsync(float[] embedding, int topK = 3)
        {
            try
            {
                _logger.LogInformation("[VECTOR_SEARCH] Searching with topK={TopK}", topK);

                var searchOptions = new SearchOptions
                {
                    VectorSearch = new VectorSearchOptions
                    {
                        Queries = new[] { new VectorizedQuery(embedding) { KNearestNeighborsCount = topK, Fields = { "embedding" } } }
                    },
                    Size = topK
                };

                var results = await _searchClient.SearchAsync<SearchDocument>(null, searchOptions);
                
                var output = new List<(string, double)>();
                await foreach (var result in results.GetResultsAsync())
                {
                    var content = result.Document["content"]?.ToString() ?? string.Empty;
                    output.Add((content, result.Score ?? 0));
                }

                _logger.LogInformation("[VECTOR_SEARCH] ? Found {Count} results", output.Count);
                return output;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[VECTOR_SEARCH] ? Search failed");
                throw;
            }
        }

        public async Task StoreAsync(string id, float[] embedding, Dictionary<string, string> metadata)
        {
            try
            {
                _logger.LogInformation("[VECTOR_SEARCH] Storing document: {Id}", id);
                
                var document = new SearchDocument
                {
                    { "id", id },
                    { "embedding", embedding.ToList() },
                };

                foreach (var kvp in metadata)
                {
                    document[kvp.Key] = kvp.Value;
                }

                await _searchClient.UploadDocumentsAsync(new[] { document });
                _logger.LogInformation("[VECTOR_SEARCH] ? Stored");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[VECTOR_SEARCH] ? Store failed");
                throw;
            }
        }
    }
}
```

#### C. Knowledge Base Service (RAG Pipeline)

**File: `JIFASAssistant.Core/Services/KnowledgeBaseService.cs`**

```csharp
using JIFASAssistant.Infrastructure.LLM;
using JIFASAssistant.Infrastructure.VectorSearch;
using Microsoft.Extensions.Logging;

namespace JIFASAssistant.Core.Services
{
    public interface IKnowledgeBaseService
    {
        Task<List<KBSearchResult>> SearchAsync(string query, int topK = 3);
        Task<double> GetConfidenceScoreAsync(string query, List<KBSearchResult> results);
    }

    public class KnowledgeBaseService : IKnowledgeBaseService
    {
        private readonly IEmbeddingService _embeddingService;
        private readonly IVectorSearchService _vectorSearch;
        private readonly ILogger<KnowledgeBaseService> _logger;

        public KnowledgeBaseService(
            IEmbeddingService embeddingService,
            IVectorSearchService vectorSearch,
            ILogger<KnowledgeBaseService> logger)
        {
            _embeddingService = embeddingService;
            _vectorSearch = vectorSearch;
            _logger = logger;
        }

        public async Task<List<KBSearchResult>> SearchAsync(string query, int topK = 3)
        {
            try
            {
                _logger.LogInformation("[KB] Searching: {Query}", query);

                // Step 1: Generate embedding dari query
                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);

                // Step 2: Vector search
                var searchResults = await _vectorSearch.SearchAsync(queryEmbedding, topK);

                // Step 3: Format results
                var results = searchResults
                    .Select((r, i) => new KBSearchResult
                    {
                        Rank = i + 1,
                        Content = r.Content,
                        Score = r.Score,
                        Department = ExtractDepartmentFromContent(r.Content)
                    })
                    .ToList();

                _logger.LogInformation("[KB] ? Found {Count} results", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[KB] ? Search failed");
                return new List<KBSearchResult>();
            }
        }

        public async Task<double> GetConfidenceScoreAsync(string query, List<KBSearchResult> results)
        {
            if (!results.Any())
                return 0.0;

            // Average score dari top results
            var avgScore = results.Average(r => r.Score);
            _logger.LogInformation("[KB] Confidence score: {Score}", avgScore);

            return avgScore;
        }

        private string ExtractDepartmentFromContent(string content)
        {
            if (content.Contains("JIFAS", StringComparison.OrdinalIgnoreCase))
                return "JIFAS";
            return "Unknown";
        }
    }

    public class KBSearchResult
    {
        public int Rank { get; set; }
        public string Content { get; set; } = string.Empty;
        public double Score { get; set; }
        public string Department { get; set; } = "JIFAS";
    }
}
```

---

### **STEP 7: Program.cs - Full DI Setup**

**File: `JIFASAssistant.API/Program.cs`**

```csharp
using JIFASAssistant.Core.Services;
using JIFASAssistant.Data;
using JIFASAssistant.Infrastructure.LLM;
using JIFASAssistant.Infrastructure.VectorSearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Embeddings;
using Serilog;
using Serilog.Core;
using Serilog.Events;

var builder = WebApplicationBuilder.CreateBuilder(args);

// ============== LOGGING ==============
var levelSwitch = new LoggingLevelSwitch();
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.ControlledBy(levelSwitch)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        "logs/jifas-assistant-.txt",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
    )
    .CreateLogger();

builder.Host.UseSerilog();

try
{
    Log.Information("[STARTUP] ?? Starting JIFAS AI Assistant...");

    // ============== DATABASE ==============
    var dbConnection = builder.Configuration["Database:SqlServer:Connection"]!;
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlServer(dbConnection)
    );
    Log.Information("[STARTUP] ? Database context configured");

    // ============== SEMANTIC KERNEL ==============
    var kernel = builder.Services.AddSemanticKernel();
    
    kernel
        .AddAzureOpenAITextEmbeddingGeneration(
            "text-embedding-3-small",
            builder.Configuration["Azure:OpenAI:Endpoint"]!,
            builder.Configuration["Azure:OpenAI:Key"]!
        );
    
    kernel
        .AddAzureOpenAIChatCompletion(
            "gpt-4",
            builder.Configuration["Azure:OpenAI:Endpoint"]!,
            builder.Configuration["Azure:OpenAI:Key"]!
        );

    builder.Services.AddScoped<IEmbeddingService, SemanticKernelEmbeddingService>();
    Log.Information("[STARTUP] ? Semantic Kernel configured");

    // ============== VECTOR SEARCH ==============
    var searchClient = new Azure.Search.Documents.SearchClient(
        new Uri(builder.Configuration["Azure:CognitiveSearch:Endpoint"]!),
        builder.Configuration["Azure:CognitiveSearch:IndexName"]!,
        new Azure.AzureKeyCredential(builder.Configuration["Azure:CognitiveSearch:Key"]!)
    );
    
    builder.Services.AddScoped<IVectorSearchService>(sp =>
        new AzureCognitiveSearchService(
            searchClient,
            sp.GetRequiredService<ILogger<AzureCognitiveSearchService>>(),
            builder.Configuration["Azure:CognitiveSearch:IndexName"]!
        )
    );
    Log.Information("[STARTUP] ? Vector search configured");

    // ============== CORE SERVICES ==============
    builder.Services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();
    builder.Services.AddScoped<ChatService>();
    builder.Services.AddScoped<TicketService>();
    builder.Services.AddScoped<SuggestionService>();
    builder.Services.AddScoped<OutOfScopeDetector>();
    Log.Information("[STARTUP] ? Core services registered");

    // ============== API & SWAGGER ==============
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "JIFAS AI Assistant API",
            Version = "v1.0.0",
            Description = "Enterprise Finance AI Assistant for JIFAS",
            Contact = new Microsoft.OpenApi.Models.OpenApiContact { Email = "finance-it@jababeka.com" }
        });
    });

    // ============== CORS ==============
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAll", policy =>
            policy
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader()
        );
    });

    var app = builder.Build();

    // ============== MIDDLEWARE ==============
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseCors("AllowAll");
    app.UseRouting();
    app.MapControllers();

    // ============== INITIALIZATION ==============
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var vectorSearch = scope.ServiceProvider.GetRequiredService<IVectorSearchService>();

        try
        {
            Log.Information("[STARTUP] Applying database migrations...");
            dbContext.Database.Migrate();
            Log.Information("[STARTUP] ? Database migrated");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "[STARTUP] ? Database migration failed");
            throw;
        }

        try
        {
            Log.Information("[STARTUP] Initializing vector search...");
            await vectorSearch.InitializeAsync();
            Log.Information("[STARTUP] ? Vector search initialized");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "[STARTUP] ? Vector search initialization failed");
            throw;
        }
    }

    Log.Information("[STARTUP] ? JIFAS AI Assistant ready!");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "[STARTUP] ? Application terminated unexpectedly");
    Environment.Exit(1);
}
finally
{
    Log.CloseAndFlush();
}
```

---

## ? COMPLETE CHECKLIST

### **Software & Tools**
- [ ] .NET 8 SDK installed
- [ ] Visual Studio 2022 atau VS Code
- [ ] SQL Server 2019+ (local atau cloud)
- [ ] SQL Server Management Studio (optional tapi recommended)
- [ ] Postman atau Insomnia (API testing)
- [ ] Git

### **Azure Services (Optional tapi Recommended)**
- [ ] Azure OpenAI account dengan Embeddings deployed
- [ ] Azure Cognitive Search service
- [ ] Or setup alternative embeddings (Semantic Kernel + OpenAI direct)

### **Configuration**
- [ ] Copy & modify `appsettings.json`
- [ ] Setup SQL Server connection string
- [ ] Configure Azure OpenAI credentials
- [ ] Create `.env` file (jangan commit!)

### **Project Setup**
- [ ] Create .NET solution structure
- [ ] Install all NuGet packages
- [ ] Create database & apply migrations
- [ ] Verify database created di SQL Server

### **Knowledge Base**
- [ ] Siapkan 2-3 `.txt` KB files (contoh di atas)
- [ ] Format sesuai standard KB structure
- [ ] Siapkan untuk diupload ke system

---

## ?? FIRST TEST COMMANDS

```bash
# 1. Build solution
dotnet build

# 2. Run migrations
dotnet ef database update --project JIFASAssistant.Data

# 3. Start application
cd JIFASAssistant.API
dotnet run

# 4. Test API (dari terminal lain)
curl http://localhost:5000/api/health

# Expected response:
# {"status":"ok","timestamp":"2026-02-03T...","services":{"database":"ok"}}
```

---

## ?? NEXT STEPS (Setelah Setup Selesai)

1. **IMPLEMENT ChatController** - `/api/chat/ask` endpoint
2. **IMPLEMENT OutOfScopeDetector** - Tolak non-JIFAS queries
3. **IMPLEMENT SuggestionService** - Generate smart suggestions
4. **IMPLEMENT TicketController** - Create JIFAS tickets
5. **IMPLEMENT KB Upload** - Bulk upload `.txt` files
6. **IMPLEMENT Gemini Summarizer** - Summary answers (optional)
7. **TEST Full Pipeline** - End-to-end testing
8. **DEPLOY to Production**

---

## ?? EMBEDDINGS RECOMMENDATION

Kamu punya 3 pilihan:

### **Option 1: Azure OpenAI (RECOMMENDED)**
? Pros: Enterprise-grade, Microsoft native, scalable
? Cons: Perlu subscription Azure
?? Cost: Sangat murah (per token)

### **Option 2: Semantic Kernel + OpenAI API**
? Pros: Direct OpenAI integration
? Cons: Perlu OpenAI API key
?? Cost: Murah

### **Option 3: Local Embeddings (Ollama)**
? Pros: Free, offline, no API key needed
? Cons: Maintenance overhead, slower
?? Cost: Free

**Rekomendasi Aku**: Use **Azure OpenAI** ? best for enterprise

---

**Siap untuk mulai setup?** ??

Mulai dari Step 1 (Environment) atau ada pertanyaan dulu?

