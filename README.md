# JIFAS AI Assistant

JIFAS AI Assistant adalah backend chatbot internal untuk membantu user JIFAS memahami proses finance, accounting, menu, troubleshooting, dan pembuatan tiket bantuan. Aplikasi ini memakai pola RAG: pertanyaan user dicari dulu ke Knowledge Base, lalu Ollama membuat jawaban berdasarkan konteks yang ditemukan.

Target runtime saat ini adalah Docker internal dengan PostgreSQL pgvector, Redis cache, Ollama server internal, Jira integration, dan dashboard monitoring.

## Status Produksi

- Chat API utama: `POST /api/chat/message`.
- Health check: `/health`, `/api/chat/health`, dan `/api/KnowledgeBaseSearch/health`.
- Monitoring dashboard: `/monitoring/index.html`.
- Database utama: PostgreSQL 16 + pgvector.
- Cache utama: Redis, dengan fallback memory/no-cache jika Redis bermasalah.
- Vector store: kolom pgvector di `KnowledgeBaseChunks`.
- Ticketing: Jira, memakai secret dari environment.
- App-level rate limit: tidak aktif sampai diminta lagi.
- Field `suggestions`: tetap ada untuk kompatibilitas frontend lama, tetapi normalnya kosong. Arahan lanjutan harus masuk ke isi `message`.

## Arsitektur Singkat

```text
Client / JIFAS Web
    |
    v
ChatController
    |
    v
ChatService
    |-- InputValidator
    |-- TicketService
    |-- CommonQueryCache / RedisCacheService
    |-- OutOfScopeDetector
    |-- KnowledgeBaseSearchService
    |-- PromptEngineeringService
    |-- OllamaAIService
    |-- ChatHistoryService
    `-- MonitoringService
```

Prinsip utama: AI hanya menjawab dalam konteks JIFAS dan Knowledge Base. Jika pertanyaan di luar konteks, sistem memberi jawaban aman dan mengarahkan user kembali ke JIFAS.

## Struktur Folder

```text
jifas-assistant/
|-- Jifas.Assistant/                 Web API utama
|   |-- Controllers/                 Endpoint chat, KB, monitoring
|   |-- Services/                    Orkestrasi chat, RAG, cache, Jira, monitoring
|   |-- Models/                      DTO request/response API
|   |-- Database/                    Bootstrap PostgreSQL pgvector
|   |-- KnowledgeBase/               Source dokumen KB lokal
|   |-- wwwroot/monitoring/          Dashboard monitoring
|   `-- Program.cs                   DI, middleware, startup validation
|-- jifas_assistant.DAL/             EF Core DbContext dan entity database
|-- Jifas.Assistant.Tests/           Unit test service inti
|-- KBLoader/                        Tool load/reindex Knowledge Base
|-- scripts/                         Operational scripts
|-- docs/                            Runbook dan readiness document
|-- docker-compose.yml               Stack API + Postgres + Redis
|-- Dockerfile                       Image API
|-- .env.example                     Template non-secret
|-- .env.docker                      Placeholder Docker
`-- .env.docker.local                Secret lokal, tidak boleh commit
```

## Service Penting

| File | Fungsi |
|------|--------|
| `ChatService.cs` | Orchestrator utama: validasi, ticket flow, cache, scope, KB, LLM, history, metrics. |
| `KnowledgeBaseSearchService.cs` | Hybrid search keyword + semantic pgvector. |
| `OllamaAIService.cs` | HTTP client ke Ollama untuk generate jawaban. |
| `OllamaEmbeddingService.cs` | HTTP client ke Ollama untuk embedding KB/query. |
| `RedisCacheService.cs` | Distributed cache berbasis Redis dengan fallback terkontrol. |
| `MemoryCacheService.cs` | Cache lokal saat Redis tidak dipakai. |
| `TicketService.cs` | Flow tiket conversational dan integrasi Jira. |
| `MonitoringService.cs` | Simpan metrik request AI dan data dashboard. |
| `InputValidator.cs` | Sanitasi dan validasi payload chat. |
| `PromptEngineeringService.cs` | Instruksi sistem agar jawaban tetap sesuai KB/JIFAS. |

## Kontrak Chat API

Endpoint:

```http
POST /api/chat/message
Content-Type: application/json
```

Request minimal:

```json
{
  "message": "Apa itu JIFAS?",
  "userId": "user-001",
  "sessionId": "session-001",
  "userRole": "FINA:KI",
  "userCompCode": "KI",
  "currentModule": "Home",
  "companyId": "KI",
  "language": "id",
  "isFirstMessage": true,
  "context": {
    "activeModule": "Home",
    "pageTitle": "Home",
    "currentPage": "/Home"
  }
}
```

Response utama:

```json
{
  "sender": "JIFAS AI Assistant",
  "message": "Jawaban AI...",
  "success": true,
  "source": "JIFAS (5 hasil)",
  "sessionId": "session-001",
  "isFromKnowledgeBase": true,
  "confidenceScore": 0.85,
  "suggestions": [],
  "ticket": null,
  "performanceMetrics": {
    "totalMs": 120,
    "kbSearchMs": 20,
    "llmResponseMs": 80,
    "wasCacheLit": false,
    "cacheScope": "shared"
  }
}
```

## Cache Policy

- Pertanyaan umum JIFAS memakai shared cache lintas user.
- Pertanyaan yang bergantung pada user, company, page, dokumen, atau ticket memakai contextual cache.
- Ticket flow tidak masuk response cache.
- Request invalid dan response error tidak masuk response cache.
- Jika Redis gagal, chat utama tetap berjalan dengan fallback memory/no-cache.

## Docker Run

Siapkan secret lokal di `.env.docker.local` dari template `.env.example`, lalu jalankan:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Start-DockerStack.ps1 -SkipTests
```

Cek container:

```powershell
docker compose ps
```

Cek endpoint:

```powershell
Invoke-WebRequest http://localhost:8888/health
Invoke-WebRequest http://localhost:8888/api/chat/health
Invoke-WebRequest http://localhost:8888/api/monitoring/all?minutes=60
```

## Development Run

```powershell
dotnet build --no-restore
dotnet test --no-restore
dotnet run --project Jifas.Assistant
```

Default development biasanya listen di `http://localhost:5000`.

## Database dan Knowledge Base

PostgreSQL bootstrap resmi berada di:

```text
Jifas.Assistant/Database/Initialize-PostgresPgvector.sql
```

Reindex Knowledge Base:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\ReindexKnowledgeBase.ps1
```

Dokumen KB source berada di:

```text
Jifas.Assistant/KnowledgeBase/
```

## Validation Checklist

Local gate:

```powershell
dotnet build --no-restore
dotnet test --no-restore
powershell -ExecutionPolicy Bypass -File scripts\Test-ProductionReadiness.ps1
docker compose --env-file .env.docker --env-file .env.docker.local config --quiet
```

Functional smoke:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Run-FullFeatureSmokeTest.ps1
```

Stress baseline:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Run-ChatStressTest.ps1 -VirtualUsers 50
```

Acceptance saat rate limit disabled:

- HTTP 429 = 0.
- HTTP 5xx = 0.
- API/Postgres/Redis healthy.
- Restart count container = 0.
- Monitoring error tidak berisi timeout suggestion.
- Cache hit tercatat untuk pertanyaan umum berulang.

## Jira Safety

Automated smoke normal tidak boleh membuat tiket Jira real. Tiket real hanya dibuat jika script dijalankan dengan flag eksplisit:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Run-FullFeatureSmokeTest.ps1 -CreateRealJiraTicket
```

Tiket validasi wajib memakai prefix `[TEST]` dan tidak ditutup otomatis.

## Dokumentasi Tambahan

- `docs/PRODUCTION_READINESS_REPORT_20260603.md`
- `docs/JIFAS_AI_ARCHITECTURE.md`
- `docs/POSTGRES_PGVECTOR_RUNBOOK.md`
- `docs/DOCKER_REDIS_CACHE.md`
- `docs/AI_QUALITY_RUNBOOK.md`

## Troubleshooting

Jika chat lambat:

- Cek apakah Redis hidup.
- Cek apakah query sudah cache hit.
- Cek apakah Ollama sedang antre request.
- Cek dashboard monitoring untuk p95 latency dan dependency failure.

Jika KB tidak hit:

- Jalankan reindex KB.
- Cek `/api/KnowledgeBaseSearch/health`.
- Cek apakah embedding dimensions sama dengan konfigurasi.

Jika Jira gagal:

- Pastikan `Jira__Email`, `Jira__ApiToken`, dan URL project benar.
- Jangan aktifkan offline fallback kecuali untuk demo lokal.

Jika dashboard kosong:

- Buka API lebih dulu agar ada data monitoring.
- Cek `/api/monitoring/all?minutes=60`.
- Cek log container `jifas-assistant-api`.
