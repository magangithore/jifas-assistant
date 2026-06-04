# JIFAS AI Architecture Guide

Tanggal update: 04 Juni 2026

Dokumen ini menjelaskan struktur teknis JIFAS AI Assistant agar mudah dibaca saat maintenance, dokumentasi magang, atau handover ke tim lain.

## 1. Tujuan Sistem

JIFAS AI Assistant berfungsi sebagai asisten internal untuk:

- Menjawab pertanyaan user tentang JIFAS berdasarkan Knowledge Base.
- Membantu navigasi menu dan halaman JIFAS.
- Menjelaskan proses finance/accounting seperti Invoice, PUM, Payment, Receiving, Cashbank, Accounting, Budget, dan Report.
- Membantu flow pembuatan tiket Jira saat user butuh bantuan IT.
- Mencatat metrik request ke dashboard monitoring.

AI tidak dipakai untuk menjawab topik umum di luar JIFAS. Jika pertanyaan tidak relevan, sistem harus memberi jawaban aman dan mengarahkan user kembali ke konteks JIFAS.

## 2. Runtime Architecture

```text
JIFAS Web / Postman / Client
    |
    | POST /api/chat/message
    v
ChatController
    |
    v
ChatService
    |-- InputValidator
    |-- TicketService
    |-- RedisCacheService / MemoryCacheService
    |-- OutOfScopeDetector
    |-- KnowledgeBaseSearchService
    |-- PromptEngineeringService
    |-- OllamaAIService
    |-- ChatHistoryService
    `-- MonitoringService
```

Supporting services:

```text
Ollama server
    |-- generate chat response
    `-- generate embeddings

PostgreSQL + pgvector
    |-- KnowledgeBaseDocuments
    |-- KnowledgeBaseChunks
    |-- ChatHistory
    |-- UserMemory
    `-- AiUsageLog

Redis
    |-- response cache
    |-- KB/search cache
    `-- short-lived fallback coordination

Jira
    `-- create real support ticket only after user confirmation
```

## 3. Chat Request Flow

1. `ChatController` menerima request dan meneruskan `CancellationToken`.
2. `ChatService` membuat `CorrelationId` untuk tracing.
3. `InputValidator` memvalidasi payload sebelum cache, DB, atau LLM dipanggil.
4. Jika session sedang berada di ticket flow, request langsung diproses oleh `TicketService`.
5. Jika bukan ticket flow, service mengecek response cache.
6. `OutOfScopeDetector` menahan pertanyaan di luar konteks JIFAS.
7. `KnowledgeBaseSearchService` mencari dokumen via keyword + semantic pgvector.
8. `PromptEngineeringService` membangun prompt berbasis KB dan context halaman.
9. `OllamaAIService` memanggil Ollama untuk jawaban utama.
10. Response disimpan ke cache jika aman.
11. `ChatHistoryService` menyimpan percakapan.
12. `MonitoringService` mencatat latency, token, cache, status, dan dependency.

## 4. Cache Policy

Response cache memakai strategi hybrid:

- `shared`: untuk pertanyaan umum seperti "Apa itu JIFAS?" atau definisi modul.
- `contextual`: untuk pertanyaan yang bergantung pada user, company, halaman, dokumen, atau status.
- `no-cache`: untuk ticket flow, request invalid, dan response error.

Redis adalah cache utama. Jika Redis gagal, request chat utama tidak boleh ikut gagal; aplikasi akan fallback ke memory/no-cache.

## 5. Suggestion Policy

Pipeline suggestion LLM terpisah sudah dimatikan.

Alasannya:

- Mengurangi beban Ollama saat banyak user request bersamaan.
- Menghindari error palsu di monitoring akibat timeout suggestion.
- Menurunkan latency p95 karena hanya ada satu call LLM untuk jawaban utama.

Field response `suggestions` tetap ada untuk kompatibilitas frontend lama, tetapi default chat normal adalah list kosong. Lanjutan percakapan harus ditulis natural di dalam `message`.

## 6. Ticket Flow

`TicketService` menangani percakapan pembuatan tiket:

1. User menyatakan ingin membuat tiket.
2. AI meminta detail masalah.
3. AI menampilkan ringkasan tiket.
4. User konfirmasi.
5. Tiket dibuat ke Jira.
6. Response mengembalikan `ticket.ticketNumber` dan `ticket.url`.

Automated test tidak membuat tiket real kecuali flag eksplisit dipakai:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Run-FullFeatureSmokeTest.ps1 -CreateRealJiraTicket
```

## 7. Database Initialization

Bootstrap PostgreSQL/pgvector berada di:

```text
Jifas.Assistant/Database/Initialize-PostgresPgvector.sql
```

Startup production harus:

- Menjalankan `CREATE EXTENSION IF NOT EXISTS vector`.
- Menjaga tabel runtime tersedia secara idempotent.
- Tidak bergantung pada file publish lama.
- Tidak memakai `EnsureCreated()` sebagai flow utama PostgreSQL.

## 8. Operational Scripts

| Script | Fungsi |
|--------|--------|
| `scripts/Start-DockerStack.ps1` | Build dan start stack Docker internal. |
| `scripts/Test-ProductionReadiness.ps1` | Gate static + build + test + config check. |
| `scripts/Run-FullFeatureSmokeTest.ps1` | Smoke test fitur utama chat. |
| `scripts/Run-ChatStressTest.ps1` | Stress test default 50 virtual users. |
| `scripts/ReindexKnowledgeBase.ps1` | Reindex dokumen KB ke database/vector store. |

## 9. File yang Tidak Boleh Masuk Source

File berikut adalah artifact runtime dan harus di-ignore:

- `publish/`
- `publish-context/`
- `logs/`
- `reports/`
- `*.err`
- `scripts/*_log.txt`
- `scripts/*_raw.txt`
- `scripts/*_preview.txt`
- `.env.docker.local`

## 10. Validation Standard

Sebelum dianggap siap:

```powershell
dotnet build --no-restore
dotnet test --no-restore
powershell -ExecutionPolicy Bypass -File scripts\Test-ProductionReadiness.ps1
docker compose --env-file .env.docker --env-file .env.docker.local config --quiet
```

Stress baseline:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Run-ChatStressTest.ps1 -VirtualUsers 50
```

Acceptance saat rate limit disabled:

- HTTP 429 = 0.
- HTTP 5xx = 0.
- Container API/Postgres/Redis tetap healthy.
- Restart count tetap 0.
- Monitoring error tidak naik karena suggestion timeout.
- Cache hit tercatat untuk pertanyaan umum berulang.

## 11. Cara Membaca Monitoring

Dashboard monitoring dipakai untuk melihat:

- Total request.
- Error rate.
- Avg dan p95 latency.
- Token input/output.
- Cache hit/miss.
- KB hit.
- Dependency failure.
- Latest request.
- Module paling aktif.

Error monitoring harus merepresentasikan kegagalan utama request atau dependency utama. Test input validation boleh terlihat sebagai `success=false`, tetapi bukan bug server.

