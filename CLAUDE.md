# CLAUDE.md - JIFAS Assistant

Panduan ini dibaca oleh Claude Code saat bekerja di repo `jifas-assistant`.
Tujuan utamanya: hemat request/token, tetap aman, dan tidak merusak project.

## Cara Kerja Hemat

- Jangan scan seluruh repo kecuali user benar-benar meminta.
- Mulai dari file yang relevan dengan task, lalu perluas pencarian hanya jika perlu.
- Gunakan `rg` untuk search cepat, bukan membaca folder besar satu per satu.
- Hindari deep thinking untuk task kecil. Berpikir secukupnya, implement, lalu test.
- Jangan membuat report panjang kecuali user meminta file report.
- Jangan menjalankan stress test atau Docker rebuild kalau task hanya edit kecil.
- Setelah task panjang, sarankan `/compact` dengan ringkasan file, keputusan, test, dan TODO.
- Jika user meminta "enterprise ready" secara luas, pecah pekerjaan menjadi audit, fix prioritas, test gate, lalu report singkat.

## Project Summary

JIFAS Assistant adalah backend chatbot internal untuk JIFAS. Fungsi utama:

- menjawab pertanyaan user tentang JIFAS memakai RAG dan Knowledge Base;
- mencari dokumen KB dengan PostgreSQL pgvector dan hybrid search;
- cache response dan KB memakai Redis dengan fallback memory/no-cache;
- membuat tiket helpdesk melalui flow conversational dan integrasi Jira;
- menyimpan metrik ke monitoring dashboard;
- menyediakan command cepat seperti `/help`, `/status`, `/ticket`, `/kb`, `/context`, `/scope`.

Runtime target saat ini adalah Docker internal:

- API: ASP.NET Core / .NET 10;
- DB: PostgreSQL 16 + pgvector;
- cache: Redis;
- LLM dan embedding: Ollama server internal;
- ticketing: Jira Cloud;
- dashboard: static HTML + monitoring API.

## Scope Repo

Repo utama:

```text
D:\Users\magang.it8\jifas-assistant
```

Jangan mengerjakan `jifas-web` kecuali user eksplisit meminta. Project `jifas-web` hanya UI/proxy; logic AI utama tetap di repo ini.

## Struktur Penting

```text
Jifas.Assistant/                 Web API utama
Jifas.Assistant/Controllers/     Endpoint chat, KB, monitoring, feedback
Jifas.Assistant/Services/        Chat pipeline, RAG, cache, Jira, monitoring
Jifas.Assistant/models/          DTO request/response
Jifas.Assistant/Database/        Bootstrap PostgreSQL pgvector
Jifas.Assistant/KnowledgeBase/   Source dokumen Knowledge Base
Jifas.Assistant/wwwroot/monitoring/index.html
jifas_assistant.DAL/             EF Core DbContext dan entity
Jifas.Assistant.Tests/           Unit tests
KBLoader/                        Loader/reindex KB
scripts/                         Readiness, Docker, smoke, stress
docs/                            Runbook dan readiness report
```

## File Paling Sering Disentuh

| File | Fungsi |
| --- | --- |
| `Jifas.Assistant/Program.cs` | Dependency injection, middleware, startup validation, DB init, health check. |
| `Jifas.Assistant/Controllers/ChatController.cs` | Endpoint chat, health, capabilities. |
| `Jifas.Assistant/Services/ChatService.cs` | Pipeline utama: validation, command, ticket, cache, scope, KB, LLM, history, monitoring. |
| `Jifas.Assistant/Services/AssistantCommandService.cs` | Slash command dan metadata capability tanpa panggil LLM. |
| `Jifas.Assistant/Services/KnowledgeBaseSearchService.cs` | Hybrid keyword + semantic pgvector search. |
| `Jifas.Assistant/Services/OllamaAIService.cs` | Panggilan generate response ke Ollama. |
| `Jifas.Assistant/Services/OllamaEmbeddingService.cs` | Panggilan embedding ke Ollama. |
| `Jifas.Assistant/Services/RedisCacheService.cs` | Distributed cache Redis, harus fallback aman. |
| `Jifas.Assistant/Services/TicketService.cs` | Conversational ticket flow dan Jira API. |
| `Jifas.Assistant/Services/MonitoringService.cs` | AI usage log, stats, dashboard data. |
| `Jifas.Assistant/wwwroot/monitoring/index.html` | Dashboard enterprise monitoring. |

## Kontrak API Utama

Endpoint chat:

```http
POST /api/chat/message
```

Request umum:

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

Response tetap punya field `suggestions` untuk kompatibilitas frontend, tetapi default boleh kosong.
Arahan lanjutan sebaiknya masuk di `message`, bukan memanggil LLM suggestion kedua.

Endpoint publik penting:

```text
/health
/api/chat/health
/api/chat/capabilities
/api/KnowledgeBaseSearch/health
/api/monitoring/all?minutes=60
/monitoring/index.html
```

## Chat Pipeline Expected Order

Urutan aman dan hemat di `ChatService`:

1. Validasi input.
2. Command cepat (`/help`, `/status`, dll) tanpa cache/KB/LLM.
3. Ticket flow jika session sedang membuat tiket.
4. Greeting/gratitude/out-of-scope fast path.
5. Response cache lookup.
6. Knowledge Base search.
7. Prompt engineering.
8. Ollama response.
9. Save history, cache successful response, record monitoring.

Jangan cache:

- ticket flow;
- request invalid;
- response error;
- jawaban dependency failure;
- payload yang tergantung data sensitif user/dokumen tanpa contextual key.

## Cache Policy

- Pertanyaan umum JIFAS boleh shared cache lintas user.
- Pertanyaan yang mengandung user, company, page, document, ticket, atau context khusus harus contextual cache.
- Redis failure tidak boleh mematikan chat utama.
- Jika Redis unavailable, fallback ke memory/no-cache dan log terkontrol.
- Untuk test cache, pertanyaan umum sama dari user berbeda harus bisa menunjukkan cache hit.

## Rate Limit Policy

Keputusan terbaru user: app-level rate limit tidak dipakai dulu.

Acceptance selama rate limit disabled:

- HTTP 429 harus 0.
- Overload test saat ini dipantau lewat latency, health, restart count, dan 5xx.

Jika menemukan sisa `AddRateLimiter`, `UseRateLimiter`, atau policy rate limit:

- jangan langsung hapus membabi buta;
- cek apakah middleware aktif;
- sesuaikan dengan keputusan user terbaru;
- pastikan test menerima 429 = 0.

## Jira Safety

Jangan membuat tiket Jira real saat test kecuali user eksplisit meminta.

Safe/default validation:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Run-FullFeatureSmokeTest.ps1
```

Real ticket hanya dengan flag:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Run-FullFeatureSmokeTest.ps1 -CreateRealJiraTicket
```

Jika real ticket dibuat:

- title wajib prefix `[TEST]`;
- deskripsi harus menyebut tiket otomatis untuk validasi integrasi;
- jangan close/transition otomatis;
- laporkan ticket key, status, URL, dan response ringkas.

## Secrets

Jangan print, commit, atau tulis ulang secret real.

File aman:

- `.env.example`: template placeholder;
- `.env.docker`: placeholder Docker;
- `.env.docker.local`: secret lokal, jangan commit.

Production startup wajib memvalidasi:

- `Admin__ApiKey` ada;
- jika `Jwt__Enabled=true`, `Jwt__SigningKey` minimal 32 karakter;
- secret Jira berada di environment, bukan docs/report/README.

## Docker

Start stack:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Start-DockerStack.ps1 -SkipTests
```

Check compose config:

```powershell
docker compose --env-file .env.docker --env-file .env.docker.local config --quiet
```

Check health:

```powershell
docker inspect --format "{{.Name}} restart={{.RestartCount}} health={{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}" jifas-assistant-api jifas-postgres jifas-redis
```

Jika Docker error pipe `dockerDesktopLinuxEngine`, Docker Desktop belum siap. Jangan lanjut Docker test sampai engine ready.

## Database dan KB

Database utama:

```text
PostgreSQL 16 + pgvector
```

Bootstrap resmi:

```text
Jifas.Assistant/Database/Initialize-PostgresPgvector.sql
```

Jangan membuat alter schema tersembunyi di startup jika bisa dibuat idempotent di script/migration resmi.

Reindex KB:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\ReindexKnowledgeBase.ps1
```

Validasi KB harus mengutamakan semantic/hybrid search. Keyword-only dapat misleading karena banyak dokumen punya judul generic seperti `Invoice`, `Payment`, `PUM`, `Report`, `General`.

## Monitoring Dashboard

Dashboard:

```text
Jifas.Assistant/wwwroot/monitoring/index.html
```

Target format angka:

- durasi: `2 menit 6 detik`, bukan `2,1 mnt`;
- token: `3.300 token`, bukan `3,3 rb token`;
- karakter: `1.420 karakter`, bukan compact yang membingungkan;
- error rate hanya kegagalan utama, bukan validation test yang expected atau suggestion timeout lama.

Metrik penting:

- total request;
- avg/p95 latency;
- token usage;
- cache hit/miss;
- KB hit;
- dependency failure;
- latest request;
- container/runtime health jika tersedia.

## Commands

Command cepat harus diproses setelah input validation dan sebelum ticket/cache/KB/LLM:

```text
/help
/commands
/status
/monitoring
/ticket
/kb
/context
/scope
```

Capability endpoint:

```text
GET /api/chat/capabilities
```

Command tidak boleh memanggil Ollama.

## Test Commands

Gunakan test paling ringan yang cukup untuk task.

Local build/test:

```powershell
dotnet build --no-restore
dotnet test --no-restore
```

Readiness gate:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Test-ProductionReadiness.ps1
```

Functional smoke:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Run-FullFeatureSmokeTest.ps1
```

Stress 50 VU:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Run-ChatStressTest.ps1 -VirtualUsers 50
```

Stress acceptance:

- total success 50/50;
- HTTP 429 = 0;
- HTTP 5xx = 0;
- API/Postgres/Redis healthy;
- restart count 0;
- cache hit visible;
- source stability reported.

## Full Feature Smoke Coverage

Smoke test harus mencakup:

- KB question: `Apa itu JIFAS?`;
- knowledge modul: Invoice, PUM, Payment, Report;
- navigasi: `Tombol approve invoice ada di page mana?`;
- follow-up dalam session sama;
- greeting;
- gratitude;
- out-of-scope;
- SQL-style input validation;
- ticket cancel flow;
- KB hybrid query;
- monitoring endpoint.

## Current Verified Baseline

Baseline terakhir yang diketahui:

- build lulus;
- test lulus 14/14;
- readiness gate lulus;
- Docker stack healthy;
- full feature smoke 22/22;
- stress 50 VU lulus;
- HTTP 429 = 0;
- HTTP 5xx = 0;
- container restart count 0.

Angka runtime bisa berubah. Jika user meminta angka terbaru, jalankan test ulang.

## Coding Rules

- Ikuti pola service/controller existing.
- Jangan refactor besar tanpa alasan langsung.
- Jangan ubah kontrak `POST /api/chat/message` tanpa izin.
- Jaga `suggestions` tetap ada di response untuk kompatibilitas.
- Propagate `CancellationToken` dari controller ke service, DB, Ollama, embedding, Jira.
- Pakai `IDbContextFactory` untuk operasi paralel/long-running agar `DbContext` tidak dibagi antar request.
- Jangan tambahkan komentar berlebihan. Komentar Bahasa Indonesia cukup untuk blok penting.
- Hindari encoding rusak. Gunakan ASCII jika tidak ada alasan memakai karakter khusus.
- Jangan masukkan source citation/performance detail ke UI user biasa kecuali untuk debugging.

## Git Safety

- Jangan `git reset --hard`.
- Jangan revert perubahan user tanpa diminta.
- Sebelum edit, cek `git status --short`.
- Artifact runtime tidak boleh jadi source production:
  - `publish/`
  - `publish-context/`
  - `logs/`
  - `reports/`
  - `*.err`
  - output test sementara
- Jika user minta cleanup, pastikan file penting Docker, pgvector, Redis, monitoring, Jira, scripts, dan docs tidak ikut terhapus.

## Hemat Token Saat Membaca File

Prefer:

```powershell
rg -n "pattern" path
Get-Content path | Select-Object -First 120
Get-Content path | Select-Object -Skip 120 -First 120
```

Hindari:

- membaca seluruh file besar tanpa filter;
- membaca semua KnowledgeBase `.txt` kecuali task memang KB authoring;
- memakai `@folder` atau inject folder besar ke prompt;
- mengulang scan repo penuh setelah konteks sudah cukup.

## Context7

Jika user menulis `use context7`, gunakan Context7 MCP hanya jika tool MCP tersedia di session.

Jika Context7 belum tersedia:

- jelaskan singkat bahwa MCP belum aktif di session saat ini;
- lanjut dari repo lokal jika task tidak butuh docs eksternal;
- jangan pura-pura sudah memakai Context7.

Contoh prompt hemat dengan Context7:

```text
use context7
Cek dokumentasi ASP.NET Core terbaru hanya untuk health checks dan cancellation token.
Jangan scan repo penuh.
```

## Output Style Untuk User

User biasanya memakai Bahasa Indonesia. Jawab ringkas, jelas, dan langsung sebut:

- file yang diubah;
- hasil test;
- endpoint/report yang bisa dicek;
- blocker kalau ada.

Jika task belum benar-benar selesai, jangan klaim production ready.
