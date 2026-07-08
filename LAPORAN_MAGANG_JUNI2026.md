# Laporan Magang — Juni 2026
## JIFAS AI Assistant (jifas-assistant)

**Nama:** Magang Team
**Email:** magang@jifas.dev
**Repo:** https://github.com/magangithore/jifas-assistant
**Periode:** 1 — 30 Juni 2026
**Komiten:** 18 commit oleh Magang Team | Rentang: 2026-06-02 s/d 2026-06-29

---

## a. Ringkasan Periode

Juni 2026 adalah bulan pembangunan fondasi dan iterasi fitur utama chatbot JIFAS. Fokus utama adalah:
(1) integrasi database PostgreSQL + pgvector untuk semantic search Knowledge Base,(2) implementasi pipeline AI Learning end-to-end mulai dari model DB, service, controller, UI admin, hingga quality policy,(3) pembangunan monitoring dashboard dan command system, serta(4) hardening keamanan (IDOR session isolation) dan performa (cache policy, Redis, rate limiting). Seluruh fitur dikembangkan dalam container Docker dengan Ollama sebagai inference engine dan divalidasi melalui smoke test, stress test, dan regression test otomatis.

---

## b. Rincian Pekerjaan per Tema

### 1. Fondasi PostgreSQL / pgvector & Docker Setup
**Komit:** `5f3d779` (2026-06-02)
**Tanggal:** 2 Juni 2026

Membangun fondasi data dengan mengintegrasikan PostgreSQL 16 + pgvector untuk vector similarity search. Hasil:
- Script `Initialize-PostgresPgvector.sql` untuk bootstrap schema dan pgvector extension
- Docker Compose dengan 3 service: API (.NET 10), PostgreSQL 16+pgvector, Redis
- Konfigurasi environment untuk local development dan Docker (`docker-compose.yml`, `.env.docker`, `.env.example`)
- KBLoader untuk load/reindex dokumen Knowledge Base ke Postgres
- Test script pertama: `Run-GoldenEvaluation.ps1`, `ReindexKnowledgeBase.ps1`

---

### 2. Konfigurasi, Arsitektur & Startup Pipeline
**Komit:** `dfa7324` (2026-06-04)
**Tanggal:** 4 Juni 2026

Refactor konfigurasi besar-besaran dan pembangunan arsitektur service layer. Hasil:
- `AppSettings.cs` untuk strongly-typed configuration binding
- 14+ interface baru: `ICacheService`, `IOllamaService`, `IInputValidator`, `ILoggerService`, dsb.
- Implementasi baru: `RedisCacheService` (dengan memory fallback), `ChatHistoryService`, `ChatService` (pipeline utama), `KnowledgeBaseSearchService` (hybrid keyword + semantic), `TicketService` (Jira integration)
- Middleware: `RequestLoggingMiddleware`, `AdminAccessAuthorizationHandler`
- Script produksi: `Start-DockerStack.ps1`, `Test-ProductionReadiness.ps1`, `Run-FullFeatureSmokeTest.ps1`
- GitHub Actions CI: `.github/workflows/dotnet-ci.yml`
- Dockerfile untuk build image .NET 10

---

### 3. Command System & Capabilities
**Komit:** `1569d52` (2026-06-05)
**Tanggal:** 5 Juni 2026

Menambahkan slash command untuk interaksi cepat tanpa AI call:
- `AssistantCommandService` — menangani `/help`, `/status`, `/monitoring`, `/ticket`, `/kb`, `/context`, `/scope`
- Capability endpoint `GET /api/chat/capabilities` — mengembalikan metadata chatbot
- MonitoringService dengan embedding warmup, quality metrics, AI usage tracking
- Stress test: `Run-ChatStressTest.ps1` — 50 VU load test
- Unit test: `AssistantCommandServiceTests.cs`

---

### 4. AI Learning Pipeline (FULL STACK)
**Komit:** `adaa8f5` (2026-06-09) + `bf927ff` (2026-06-18) + B1-B6 (2026-06-29)
**Tanggal:** 9 Juni — 29 Juni 2026

Fitur terbesar periode ini — pipeline AI Learning end-to-end untuk improve Knowledge Base secara otomatis dari feedback user. Hasil:

**Model & DB (komit `adaa8f5`):**
- `LearningCandidate` model — menyimpan pasangan Q&A candidate
- `LearningCandidateAuditLog` — audit trail perubahan
- `UserMemory` — cross-session memory per user
- Schema migration + DbContext

**Service layer:**
- `AiLearningService` — deduplication (hash-based), quality scoring, candidate review workflow
- `ResponseQualityService` — confidence scoring
- Unit tests: `AiLearningPolicyTests.cs`, `QueryUnderstandingServiceTests.cs`

**Bug fixes (komit `bf927ff`, 18 Juni):**
- AI Learning edge case: hash collision prevention, graceful degradation saat learning pipeline fail
- KB search edge case: empty result handling
- Ticket flow edge case: conversation state machine consistency
- Jira SSL bypass toggle untuk environment dengan certificate interception

**Feature iteration B1-B6 (komit 29 Juni):**
- `bcd9ac3` (B1): `TotalFrequency` column — tracking frekuensi kumulatif per candidate hash
- `9678413` (B3): Audit log retention cleanup otomatis >30 hari
- `e4bed66` (B2): QualityScore threshold-based update — tidak lagi monotonic (bisa turun)
- `7e673a4` (B1 fix): `TotalFrequency` per-hash checkpoint, filter `[Session Greeting]`, `LastSeenAt` consistency
- `133749b` (B5): Tampilan frekuensi "terakhir X lalu" di learning admin UI
- `1a592d5` (B4): Rate limit per-user per-day — max 20 candidate creation/user/hari
- `d2f0310` (B6): Sensitive-data warning box di learning admin UI + `SkippedDueToRateLimit` result flag

---

### 5. Monitoring Dashboard
**Komit:** `7131a9b` (2026-06-08), `48d921f` (2026-06-08), `4de446a` (2026-06-22)
**Tanggal:** 8 Juni — 22 Juni 2026

Perbaikan monitoring dashboard enterprise untuk visibility AI usage:
- `Jifas.Assistant/wwwroot/monitoring/index.html` — dashboard interaktif
- Perbaikan label metrik: cache hit/miss, KB hit, LLM latency, error rate
- Terminologi diklarifikasi: "durasi" (bukan "waktu"), format angka konsisten (ribuan, bukan rb)
- Initial `PRODUCT.md` dokumentasi dashboard

---

### 6. Cross-Session Memory
**Komit:** `051d44a` (2026-06-26)
**Tanggal:** 26 Juni 2026

Fitur untuk melacak user across sessions:
- `UserMemory` model: `LastSessionId`, `LastSessionAt` — user bisa lanjut dari session sebelumnya
- `CrossSessionContextService` — agregasi konteks antar session
- Migration SQL: `AddLastSessionIdColumn.sql`
- `ConversationIntelligenceService` update untuk include cross-session context
- Regression test: `LongSessionRecallTest.ps1` (7 phase, 27 pertanyaan)

---

### 7. Keamanan — IDOR Session Isolation
**Komit:** `a61e94b` (2026-07-01, di luar periode Juni) + `55d2ec2`
**Tanggal:** 1 Juli 2026 (di luar periode)

Fix keamanan kritis — ditemukan via red team analysis:
- `ChatHistoryService.GetSessionHistoryAsync`: filter `WHERE SessionId=X AND UserId=Y` — userId WAJIB, tidak boleh kosong
- `ConversationIntelligenceService.BuildContextAsync`: empty/null userId → return empty context (tidak fallback ke "anon")
- Cache key menggunakan `ConversationContext_{userId}_{sessionId}` — tanpa anonymous bucket
- Scope rule teknis IT di `OllamaAIService` — konsep seperti websocket, REST API, OAuth, graphQL di luar konteks JIFAS ditolak

**Re-test:** 3/3 PASS — `userId=""`, `null`, dan `"anon"` tidak bisa leak data session lain

---

### 8. Cache & Performa
**Komit:** `a61e94b` (terintegrasi)
**Tanggal:** konteks session isolation

Perbaikan cache policy:
- OOS (out-of-scope) responses TIDAK di-cache — hanya KB-grounded responses yang masuk cache
- Mencegah cache pollution dari jawaban yang tidak terikat Knowledge Base
- Cache key per-user per-session — tidak ada shared cache cross-user

---

### 9. Environment Refactor & Docs
**Komit:** `582d1e1` (2026-06-25), `1e3dde9` (2026-06-19)
**Tanggal:** 19 Juni — 25 Juni 2026

- Refactor environment configuration untuk Docker setup — env var binding ke configuration
- `DbContext` registration untuk parallel service operations (`IDbContextFactory`)
- Dokumentasi: `AI_LEARNING_RUNBOOK.md`, `AI_QUALITY_RUNBOOK.md`, `JIFAS_AI_ARCHITECTURE.md`, `PRD_JIFAS_AI_ASSISTANT.md`, `PRODUCTION_READINESS_REPORT_20260603.md`

---

### 10. Cleanup & Dead Code (2026-07-02, di luar periode)
**Komit:** `55d2ec2`, `565e919`
**Tanggal:** 2 Juli 2026 (di luar periode)

Aktifitas di awal Juli — dicatat sebagai lampiran:
- 17 file artefak dev dihapus dari repo (red-team scripts, fix scripts, CLAUDE_INSTRUCTION, vt.dll, dll.)
- `.gitignore` diperbarui untuk mencegah commit artefak lagi
- 570 baris dead code dihapus: `_conversationTurns` AsyncLocal, 9 orphan methods
- Regression test resmi: `Jifas.Assistant.Tests/Regression/idortest.js`
- Typo fix: `conversaional` → `conversational`

---

## c. Teknologi & Tools

| Kategori | Teknologi |
|----------|-----------|
| Runtime | ASP.NET Core / .NET 10 |
| Database | PostgreSQL 16 + pgvector (vector similarity search) |
| Cache | Redis 7 (distributed cache, memory fallback) |
| AI/Embedding | Ollama server internal (model: qwen3:8b, embedding: qwen3-embedding:4b) |
| Ticketing | Jira Cloud (REST API) |
| Container | Docker Compose (API, Postgres, Redis) |
| CI/CD | GitHub Actions (.github/workflows/dotnet-ci.yml) |
| Testing | dotnet test, PowerShell smoke/stress test, ConvTest |
| UI | Static HTML dashboard (monitoring, learning admin) |
| Config | appsettings.json, environment variables, .env files |

---

## d. Masalah yang Ditemui & Penyelesaian

| Masalah | Penyelesaian |
|---------|--------------|
| **IDOR session hijacking** — filter `WHERE SessionId=X` tanpa userId check | Wajib gunakan `WHERE SessionId=X AND UserId=Y` di `ChatHistoryService`; empty/null userId → return empty context |
| **Cache pollution** — OOS responses di-cache dan answered dari cache | Gate cache: hanya `response.IsFromKnowledgeBase==true` yang di-cache |
| **Ollama T1 90 detik** — timeout saat Ollama unavailable | Retry exponential backoff (3-8-16 detik) + jitter, graceful degradation |
| **Jira SSL validation failure** di environment dengan certificate interception | Toggle `Jira__BypassSslValidation` di environment |
| **Docker image build cache pollution** — restart container tidak reload code |Perlu rebuild image (`docker compose build`) untuk deploy code change |
| **Red team: OOS cache bypass** — "websocket?" dijawab dari cache (17-21ms) | Fix OOS cache gate + technical IT scope rule di Ollama prompt |
| **HISTORY_DEPTH double query** — sesi >15 turn hit DB 2x per request | Computed RunningSummary untuk older turns, hanya last 15 verbatim |
| **SaveHistory silent failure** — gagal tanpa sinyal | Catch-and-continue, audit trail hilang tapi app tetap jalan |

---

## e. Tabel Metrik

| Metrik | Nilai |
|--------|-------|
| **Total commit** | 18 (author: Magang Team) |
| **Tanggal pertama** | 2026-06-02 |
| **Tanggal terakhir** | 2026-06-29 |
| **Total insertions** | +13.476 baris |
| **Total deletions** | -6.268 baris |
| **Net perubahan** | +7.208 baris |
| **File tersering disentuh** | `AiLearningService.cs` (7x), `Program.cs` (7x), `ChatService.cs` (7x), `monitoring/index.html` (7x) |
| **Test files (.cs)** | 13 file |
| **Script automasi** | 11 file (.ps1) |
| **Dokumentasi** | 9 file (.md) |
| **Docker image** | jifas-assistant:latest (rebuilt tiap code change) |

**File per area (top touch counts):**

| Area | File | Touch |
|------|------|-------|
| Services (AI) | `AiLearningService.cs` | 7x |
| Services (Core) | `ChatService.cs` | 7x |
| Services (KB) | `KnowledgeBaseSearchService.cs` | 5x |
| Services (Ticket) | `TicketService.cs` | 5x |
| Config | `appsettings.json`, `appsettings.Docker.json` | 5x, 6x |
| UI (Monitoring) | `monitoring/index.html` | 7x |
| UI (Learning) | `admin/learning/index.html` | 5x |
| Database | `Initialize-PostgresPgvector.sql` | 3x |
| Tests | `AiLearningPolicyTests.cs` | 4x |
| Scripts | `Run-FullFeatureSmokeTest.ps1`, `Run-ChatStressTest.ps1` | created |
| Config (Docker) | `docker-compose.yml`, `.env.example` | 4x, 4x |
| DAL | `LearningCandidate.cs`, `UserMemory.cs` | created |

---

## Lampiran: Commit List

| Tanggal | Hash | Subjek |
|---------|------|--------|
| 2026-06-02 10:37 | `5f3d779` | feat: Integrate PostgreSQL with pgvector support and update Docker configuration |
| 2026-06-04 15:00 | `dfa7324` | Add scripts for full feature smoke testing, Docker stack startup, and production readiness validation |
| 2026-06-05 17:10 | `1569d52` | feat: Enhance JIFAS Assistant with command handling and capabilities |
| 2026-06-08 11:40 | `7131a9b` | feat: Enhance response regeneration with self-correction and detailed prompt engineering style: Update monitoring dashboard terminology for clarity and consistency |
| 2026-06-08 15:29 | `48d921f` | refactor: Update metric labels and descriptions for clarity in monitoring dashboard |
| 2026-06-09 15:03 | `adaa8f5` | Add LearningCandidate and LearningCandidateAuditLog models with DbSet in context |
| 2026-06-18 09:49 | `bf927ff` | fix: AI Learning, KB search, Ticket flow edge cases, Jira SSL bypass |
| 2026-06-19 10:37 | `1e3dde9` | refactor: Update DbContext registration for parallel service operations |
| 2026-06-22 10:50 | `4de446a` | Add initial PRODUCT.md documentation for JIFAS AI Assistant dashboard |
| 2026-06-25 08:37 | `582d1e1` | Refactor environment configuration for Docker setup |
| 2026-06-26 10:14 | `051d44a` | feat: Implement cross-session memory tracking with LastSessionId and LastSessionAt |
| 2026-06-29 11:46 | `bcd9ac3` | B1: Add TotalFrequency column for cumulative historical frequency |
| 2026-06-29 14:05 | `9678413` | B3: Add audit log retention cleanup (>30 days) |
| 2026-06-29 14:09 | `e4bed66` | B2: QualityScore threshold-based update (no longer monotonic) |
| 2026-06-29 15:01 | `7e673a4` | B1 fix: TotalFrequency per-hash checkpoint + [Session Greeting] filter + LastSeenAt consistency |
| 2026-06-29 15:03 | `133749b` | B5: Show TotalFrequency and 'terakhir X lalu' in learning admin list |
| 2026-06-29 15:15 | `1a592d5` | B4: per-user per-day rate limit on candidate creation (20/day/user) |
| 2026-06-29 15:15 | `d2f0310` | B6: add SkippedDueToRateLimit to LearningCollectionResult; add sensitive-data warning box in learning admin UI |

---

*Aktifitas di luar periode (2 Juli 2026): komit `a61e94b` (IDOR bypass fix), `55d2ec2` (cleanup), `565e919` (dead code removal). Total 3 komit tambahan — tidak termasuk dalam metrik Juni.*
