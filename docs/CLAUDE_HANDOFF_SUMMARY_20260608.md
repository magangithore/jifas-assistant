# Claude Handoff Summary - JIFAS Assistant

Dokumen ini dibuat untuk membuka chat Claude Code baru tanpa perlu membaca ulang seluruh percakapan lama. Fokus repo saat ini adalah:

```text
D:\Users\magang.it8\jifas-assistant
```

Jangan pindah ke `jifas-web` kecuali user eksplisit meminta. Logic AI utama ada di repo ini.

## Ringkasan Percakapan Terakhir

User sedang mengembangkan backend chatbot internal JIFAS agar siap dipakai produksi internal. Targetnya:

- chatbot menjawab pertanyaan JIFAS dari Knowledge Base;
- Knowledge Base memakai PostgreSQL + pgvector;
- response cache memakai Redis;
- Jira ticket dibuat lewat flow conversational;
- monitoring dashboard mudah dibaca oleh user non-teknis;
- Docker internal menjadi target deployment;
- app-level rate limit tidak dipakai dulu;
- baseline stress test awal adalah 50 virtual users;
- dashboard harus jelas membedakan request `AI generate` dan `Cache`.

User sering meminta implementasi langsung, bukan hanya saran. Jika ada task coding, kerjakan, test, lalu laporkan singkat.

## Yang Terjadi di Claude Code

Claude Code sebelumnya sempat mengedit:

```text
Jifas.Assistant/Services/ResponseQualityService.cs
Jifas.Assistant/Services/QueryUnderstandingService.cs
```

Claude berhenti karena error provider:

```text
API Error: 400 Model provider temporarily unavailable
```

Itu bukan error project C#. Itu error layanan/model Claude Code. Screenshot menunjukkan Claude sedang memperbaiki field case-sensitive:

```csharp
uncertaintyMarkers -> UncertaintyMarkers
```

Masalah casing itu sudah selesai.

## Yang Sudah Dilanjutkan Setelah Claude Error

### ResponseQualityService.cs

Perubahan Claude sudah dirapikan agar tidak hanya compile, tetapi lebih production-ready.

Yang sudah diterapkan:

- menambah scoring baru:
  - `FactualityScore`;
  - `CoherenceScore`;
  - `FactorScores`;
  - `ConfidenceLevel`;
  - `UncertaintyFactors`;
- memperluas validasi kualitas response menjadi beberapa faktor:
  - grounding terhadap KB;
  - factuality terhadap KB;
  - relevance terhadap query user;
  - completeness;
  - clarity;
  - coherence;
- memperbaiki bug casing `UncertaintyMarkers`;
- menghapus teks aneh/non-ASCII rusak `mungkin，预计`;
- menghapus komentar sementara seperti `ENHANCED` yang terlalu noisy;
- membuat `ConfidenceLevel` dan `UncertaintyFactors` benar-benar diisi saat confidence dihitung;
- menjaga fallback agar error quality validation tidak mematikan chat utama.

Catatan penting: quality service ini adalah heuristik, bukan kebenaran absolut. Jangan membuatnya terlalu agresif sampai jawaban valid malah sering dianggap tidak valid.

### QueryUnderstandingService.cs

Claude sempat menambah metadata intent. Setelah itu dilanjutkan supaya field baru benar-benar dipakai.

Yang sudah diterapkan:

- menambah metadata di `IntentResult`:
  - `PrimaryModule`;
  - `SecondaryModule`;
  - `DetectedEntities`;
  - `KeyActions`;
  - `ComplexityLevel`;
  - `ConversationIntent`;
  - `UrgencyLevel`;
- menambah entity extraction untuk:
  - nomor Invoice;
  - PUM;
  - RV;
  - Payment;
  - SPK;
  - company code;
  - tanggal;
  - modul;
- regex entity extraction diberi timeout agar tidak menggantung;
- hasil intent sekarang dienrich lewat `EnrichIntentResult(...)`;
- log yang sebelumnya memakai karakter `?` diganti jadi `->`;
- helper baru ditambah untuk deteksi:
  - action;
  - primary module;
  - secondary module;
  - query complexity;
  - follow-up conversation;
  - urgency.

### Monitoring Dashboard

File:

```text
Jifas.Assistant/wwwroot/monitoring/index.html
```

Perubahan terakhir berfokus pada tabel `Riwayat Request Terbaru`.

Masalah sebelumnya:

- dashboard terlalu ramai;
- baris cache menampilkan `Tidak generate / jawaban dari Redis` di kolom `Kecepatan AI`;
- user bingung karena kolom token cache terlihat seperti token AI yang benar-benar dipakai;
- label `Jawaban cepat` terlalu panjang untuk kolom tipe;
- banner cache terlalu panjang.

Yang sudah dirapikan:

- tipe cache sekarang tampil sebagai `Cache`;
- kolom `Kecepatan AI` untuk cache tampil `-`;
- cache menampilkan `0 token` karena model tidak dipanggil;
- detail estimasi token teks cache dipindah ke tooltip;
- banner cache dipendekkan;
- tinggi row tabel dikurangi;
- label `Beban tanya/Beban jawab` diganti menjadi `Input token/Output token`;
- dashboard sudah rebuild ke Docker dan container health hijau.

## Status Validasi Terakhir

Perintah yang sudah dijalankan setelah perubahan Claude dilanjutkan:

```powershell
dotnet build --no-restore
dotnet test --no-restore
git diff --check
```

Hasil terakhir:

- build sukses;
- test sukses `14/14`;
- `git diff --check` tidak menunjukkan whitespace error, hanya warning normal CRLF;
- Docker stack sempat direbuild untuk dashboard monitoring;
- container yang dicek terakhir:
  - `jifas-assistant-api`: healthy;
  - `jifas-postgres`: healthy;
  - `jifas-redis`: healthy.

Status git saat dokumen ini dibuat masih ada modified files:

```text
M  Jifas.Assistant/Services/QueryUnderstandingService.cs
M  Jifas.Assistant/Services/ResponseQualityService.cs
MM Jifas.Assistant/wwwroot/monitoring/index.html
```

`MM` pada dashboard berarti file punya perubahan staged dan unstaged, atau ada index state campuran. Jangan asal reset. Cek dengan:

```powershell
git status --short
git diff -- Jifas.Assistant/wwwroot/monitoring/index.html
git diff --cached -- Jifas.Assistant/wwwroot/monitoring/index.html
```

## Penjelasan Project Secara Keseluruhan

JIFAS Assistant adalah backend chatbot internal untuk JIFAS. Ia membantu user bertanya tentang finance, accounting, workflow approval, menu, troubleshooting, dan pembuatan tiket bantuan.

Arsitektur sederhananya:

```text
JIFAS Web / Postman / Client
    |
    v
ChatController
    |
    v
ChatService
    |-- InputValidator
    |-- AssistantCommandService
    |-- TicketService
    |-- Redis/Common Query Cache
    |-- OutOfScopeDetector
    |-- QueryUnderstandingService
    |-- KnowledgeBaseSearchService
    |-- PromptEngineeringService
    |-- OllamaAIService
    |-- ChatHistoryService
    |-- ResponseQualityService
    `-- MonitoringService
```

### Runtime Stack

- API: ASP.NET Core / .NET 10.
- Database: PostgreSQL 16 + pgvector.
- Cache: Redis, dengan fallback memory/no-cache.
- LLM: Ollama server internal.
- Embedding: Ollama embedding model.
- Ticketing: Jira Cloud.
- Monitoring: static dashboard HTML + monitoring API + SignalR.
- Deployment target: Docker internal.

### Folder Penting

```text
Jifas.Assistant/                 Web API utama
Jifas.Assistant/Controllers/     Endpoint chat, KB, monitoring, feedback
Jifas.Assistant/Services/        Chat pipeline, RAG, cache, Jira, monitoring
Jifas.Assistant/Models/          DTO request/response
Jifas.Assistant/Database/        Bootstrap PostgreSQL pgvector
Jifas.Assistant/KnowledgeBase/   Source dokumen KB
Jifas.Assistant/wwwroot/monitoring/index.html
jifas_assistant.DAL/             EF Core DbContext dan entity
Jifas.Assistant.Tests/           Unit tests
KBLoader/                        Loader/reindex Knowledge Base
scripts/                         Docker, readiness, smoke, stress scripts
docs/                            Runbook dan readiness docs
```

### Endpoint Penting

```text
POST /api/chat/message
GET  /health
GET  /api/chat/health
GET  /api/chat/capabilities
GET  /api/KnowledgeBaseSearch/health
GET  /api/monitoring/all?minutes=60
GET  /monitoring/index.html
```

### Chat Pipeline yang Diharapkan

Urutan aman di `ChatService`:

1. input validation;
2. slash command cepat seperti `/help`, `/status`, `/kb`;
3. ticket flow jika session sedang membuat tiket;
4. greeting/gratitude/out-of-scope fast path;
5. response cache lookup;
6. query understanding dan KB search;
7. prompt engineering;
8. call Ollama AI;
9. response quality validation;
10. save history;
11. cache response sukses;
12. record monitoring.

Jangan menambah LLM call kedua untuk suggestions. Field `suggestions` tetap ada untuk kompatibilitas frontend, tetapi normalnya boleh kosong. Arahan lanjutan sebaiknya masuk ke isi `message`.

### Cache Policy

Cache memakai Redis.

Pertanyaan umum/non-personal seperti:

```text
Apa itu JIFAS?
Apa itu Invoice?
Bagaimana alur approval?
```

boleh memakai shared cache lintas user.

Pertanyaan yang mengandung konteks user/page/company/document/ticket harus pakai contextual cache.

Jangan cache:

- ticket flow;
- request invalid;
- response error;
- dependency failure;
- jawaban yang bergantung pada data sensitif user;
- response yang tidak sukses.

Jika Redis down, chat utama harus tetap berjalan dengan fallback memory/no-cache.

### Monitoring Dashboard

Dashboard ada di:

```text
Jifas.Assistant/wwwroot/monitoring/index.html
```

URL runtime Docker:

```text
http://localhost:8888/monitoring/index.html
```

Prinsip dashboard:

- harus mudah dibaca, bukan terlalu teknis;
- angka durasi harus jelas;
- token input/output harus terlihat;
- baris cache harus jelas bahwa token AI = 0;
- `Durasi` berarti total waktu request;
- `Kecepatan AI` berarti token output per detik saat model generate;
- untuk cache, `Kecepatan AI` harus `-` karena model tidak dipanggil.

### Jira

Ticket dibuat lewat conversational flow, bukan endpoint tiket terpisah.

Jangan membuat tiket Jira real untuk test kecuali user eksplisit meminta. Smoke test default harus non-destruktif.

Real Jira test hanya jika user minta secara eksplisit atau script diberi flag real-ticket.

### Rate Limit

Keputusan user terbaru: app-level rate limit tidak dipakai dulu.

Acceptance saat rate limit disabled:

```text
HTTP 429 = 0
HTTP 5xx = 0
container restart count = 0
```

Jangan mengaktifkan kembali rate limit tanpa diminta user.

## Cara Run Cepat

Local build/test:

```powershell
dotnet build --no-restore
dotnet test --no-restore
```

Docker internal:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Start-DockerStack.ps1 -SkipTests
```

Cek container:

```powershell
docker ps --filter "name=jifas-" --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"
```

Cek dashboard:

```text
http://localhost:8888/monitoring/index.html
```

Jika browser masih menampilkan UI lama, tekan:

```text
Ctrl + F5
```

## Test Penting

Smoke:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Run-FullFeatureSmokeTest.ps1
```

Stress 50 user:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Run-ChatStressTest.ps1 -VirtualUsers 50
```

Jika ingin test token AI tanpa semuanya cache, gunakan random dan skip warmup:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Run-ChatStressTest.ps1 -VirtualUsers 50 -RandomQuestions -SkipWarmup
```

## Hal yang Perlu Diperhatikan Claude

- Jangan scan seluruh repo kalau tidak perlu.
- Jangan revert perubahan user/Codex tanpa izin.
- Jangan hapus Docker/Postgres/Redis/Jira/monitoring feature yang sudah ada.
- Jangan membuat real Jira ticket kecuali diminta.
- Jangan menyalakan rate limit.
- Gunakan `rg` untuk search.
- Setelah edit, minimal jalankan `dotnet build --no-restore`.
- Untuk perubahan service inti, jalankan `dotnet test --no-restore`.
- Untuk dashboard JS, validasi syntax dengan Node:

```powershell
@'
const fs = require('fs');
const html = fs.readFileSync('Jifas.Assistant/wwwroot/monitoring/index.html', 'utf8');
const scripts = [...html.matchAll(/<script[^>]*>([\s\S]*?)<\/script>/gi)].map(m => m[1]).join('\n');
new Function(scripts);
console.log('dashboard-js-ok');
'@ | node -
```

## Suggested Next Steps

Jika melanjutkan dari state ini:

1. Cek `git status --short`.
2. Review diff tiga file modified:
   - `QueryUnderstandingService.cs`;
   - `ResponseQualityService.cs`;
   - `wwwroot/monitoring/index.html`.
3. Jalankan:

```powershell
dotnet build --no-restore
dotnet test --no-restore
```

4. Jika ingin memastikan Docker UI terbaru:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Start-DockerStack.ps1 -SkipTests
```

5. Buka dashboard dan hard refresh.

## Short Prompt untuk Claude Chat Baru

Copy-paste bagian ini ke Claude:

```text
Kamu sedang bekerja di D:\Users\magang.it8\jifas-assistant.
Fokus repo ini, jangan jifas-web kecuali diminta.

Project adalah backend chatbot JIFAS: ASP.NET Core/.NET 10, PostgreSQL pgvector, Redis cache, Ollama LLM/embedding, Jira ticket flow, monitoring dashboard.

Keputusan penting:
- rate limit aplikasi tidak dipakai dulu;
- HTTP 429 harus 0;
- jangan buat Jira ticket real kecuali diminta;
- suggestions LLM kedua tidak dipakai;
- cache Redis boleh shared untuk pertanyaan umum, contextual untuk pertanyaan user/page/company/document/ticket;
- cache/ticket/error/invalid request tidak boleh dicache;
- dashboard harus mudah dibaca, token input/output terlihat, cache = 0 token AI.

Perubahan terakhir:
- ResponseQualityService ditambah FactualityScore, CoherenceScore, FactorScores, ConfidenceLevel, UncertaintyFactors;
- QueryUnderstandingService ditambah entity extraction dan intent metadata;
- dashboard monitoring dirapikan agar cache tampil sebagai Cache, Kecepatan AI untuk cache = "-", dan token cache tampil 0 token.

Sebelum kerja, cek git status dan diff. Jangan reset/revert.
Setelah edit, jalankan dotnet build --no-restore dan dotnet test --no-restore jika menyentuh service inti.
```

