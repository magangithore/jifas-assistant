# PRD JIFAS AI Assistant

Tanggal dokumen: 09 Juni 2026  
Produk: JIFAS AI Assistant  
Target repo: `D:\Users\magang.it8\jifas-assistant`  
Target runtime: Docker internal  
Status dokumen: Draft lengkap untuk review bisnis dan teknis

## 1. Ringkasan Produk

JIFAS AI Assistant adalah backend chatbot internal untuk membantu user JIFAS memahami proses finance, accounting, navigasi menu, troubleshooting, dan pembuatan tiket bantuan. Sistem memakai pendekatan Retrieval-Augmented Generation (RAG): pertanyaan user dicocokkan dengan Knowledge Base JIFAS, lalu model AI internal membuat jawaban berdasarkan konteks yang ditemukan.

Produk ini ditujukan sebagai asisten operasional, bukan chatbot umum. AI harus fokus pada konteks JIFAS, menolak atau mengarahkan ulang pertanyaan di luar scope, serta menjaga data internal agar tidak sembarang dipublish atau dijadikan knowledge tanpa proses kurasi.

Stack utama:

| Area | Teknologi |
|---|---|
| Backend API | ASP.NET Core / .NET |
| Database utama | PostgreSQL 16 |
| Vector search | pgvector |
| Cache | Redis dengan fallback memory/no-cache |
| AI runtime | Ollama server internal |
| Ticketing | Jira Cloud |
| Monitoring | Dashboard HTML + API + SignalR/log |
| Deployment | Docker internal |
| Test tooling | PowerShell scripts, xUnit |

## 2. Latar Belakang

User JIFAS sering membutuhkan bantuan untuk:

- memahami fungsi modul finance/accounting,
- mencari lokasi tombol/menu,
- mengetahui alur approval,
- memahami status dokumen,
- menyelesaikan error umum,
- membuat tiket IT jika masalah tidak bisa diselesaikan sendiri.

Sebelum ada assistant, dukungan banyak bergantung pada dokumentasi manual, tanya langsung ke tim IT/finance, atau tiket support. Dampaknya:

- waktu tunggu support tinggi,
- pertanyaan berulang terus masuk ke IT,
- knowledge tersebar di dokumen dan pengalaman personal,
- user baru sulit belajar alur JIFAS,
- monitoring kualitas jawaban belum terukur.

JIFAS AI Assistant dibuat untuk menjadi layer bantuan pertama yang cepat, konsisten, terukur, dan dapat terus diperbaiki lewat AI Learning berbasis kurasi admin.

## 3. Visi Produk

Menjadi asisten internal JIFAS yang bisa menjawab pertanyaan user secara cepat, akurat, aman, dan kontekstual, sekaligus membantu tim IT mengubah percakapan berulang menjadi Knowledge Base resmi yang mudah dirawat.

## 4. Tujuan Produk

Tujuan utama:

1. Mengurangi pertanyaan berulang ke IT Help Desk.
2. Membantu user memahami modul JIFAS tanpa membaca dokumen panjang.
3. Memberikan jawaban berbasis Knowledge Base, bukan halusinasi.
4. Membantu navigasi halaman dan flow approval JIFAS.
5. Membuat tiket Jira dari percakapan jika user butuh bantuan lanjutan.
6. Menyediakan dashboard monitoring performa, kualitas, dan kesehatan runtime.
7. Membangun learning loop aman: chat -> candidate -> admin edit -> publish ke KB.
8. Menyiapkan backend untuk baseline internal 50 virtual users tanpa app-level rate limit.

## 5. Non-Goals

Hal yang tidak menjadi target versi ini:

- Fine-tuning model AI.
- Menggantikan approval resmi di JIFAS.
- Mengubah data finance, dokumen, invoice, payment, atau transaksi.
- Memberi akses ke data sensitif yang tidak ada di request/context.
- Menjadi chatbot umum untuk topik non-JIFAS.
- Menutup tiket Jira otomatis.
- Menjalankan Prometheus/Grafana/OpenTelemetry stack.
- Mengaktifkan app-level rate limit, sampai user meminta eksplisit.
- Mengimplementasikan ulang engine AI di `jifas-web`; `jifas-web` hanya client/proxy UI.

## 6. Stakeholder

| Stakeholder | Kepentingan |
|---|---|
| User JIFAS | Mendapat jawaban cepat dan jelas tentang JIFAS. |
| Finance/Accounting | Memastikan jawaban sesuai proses bisnis. |
| IT Help Desk | Mengurangi tiket berulang dan menerima tiket yang lebih rapi. |
| Admin Knowledge Base | Mengelola dokumen, kandidat learning, dan kualitas knowledge. |
| Developer JIFAS | Menjaga integrasi API, deployment, monitoring, dan reliability. |
| Management | Melihat efektivitas support dan kesiapan produksi. |

## 7. Persona Pengguna

### 7.1 User Operasional

Contoh: staff finance, accounting, tax, cashier, budget, atau procurement.

Kebutuhan:

- tanya "Apa itu JIFAS?",
- tanya alur invoice/payment/PUM,
- cari halaman approval,
- minta troubleshooting tombol/menu/status,
- buat tiket jika error tetap terjadi.

### 7.2 User Baru

Kebutuhan:

- belajar istilah JIFAS,
- memahami modul dasar,
- mengetahui langkah awal membuat dokumen,
- mengetahui siapa yang harus dihubungi jika akses tidak sesuai.

### 7.3 IT Support

Kebutuhan:

- menerima tiket dengan deskripsi yang jelas,
- melihat konteks percakapan,
- memahami modul dan kategori masalah,
- memonitor error, latency, cache, dan dependency.

### 7.4 Admin Knowledge Base

Kebutuhan:

- melihat kandidat AI Learning,
- mengedit jawaban sebelum publish,
- mengarsipkan jawaban yang tidak layak,
- publish knowledge baru ke KB resmi,
- memastikan data sensitif tidak masuk KB.

### 7.5 Developer / Maintainer

Kebutuhan:

- menjalankan stack Docker,
- test readiness,
- test smoke dan stress,
- debug dependency Redis/Postgres/Ollama/Jira,
- menjaga kontrak API tidak breaking.

## 8. Scope Rilis

### 8.1 Scope Saat Ini

Fitur yang termasuk:

- Chat API utama `POST /api/chat/message`.
- Health endpoint `/health`, `/api/chat/health`, `/api/KnowledgeBaseSearch/health`.
- RAG berbasis Knowledge Base JIFAS.
- Hybrid search keyword + semantic pgvector.
- Cache hybrid Redis.
- Out-of-scope detector.
- Input validation.
- Command cepat.
- Ticket flow conversational ke Jira.
- Feedback API.
- AI Learning candidate queue.
- Admin Learning Panel.
- Monitoring dashboard.
- Docker internal dengan Postgres pgvector dan Redis.
- Startup validation untuk production secrets.
- Smoke/stress/readiness scripts.

### 8.2 Scope Rollout Awal

Rollout awal fokus pada company `KI` untuk validasi internal. Desain tetap mendukung multi-company melalui field `userCompCode`, `companyId`, dan context.

### 8.3 Scope Masa Depan

- Rate limit configurable jika dibutuhkan.
- Dashboard admin yang lebih lengkap.
- Export laporan monitoring.
- Workflow approval knowledge bertingkat.
- Evaluasi model otomatis/golden set lebih besar.
- Integrasi notifikasi tiket.
- Observability stack eksternal.

## 9. User Journey

### 9.1 Tanya Knowledge Base

1. User membuka chatbot dari JIFAS Web atau tool API.
2. User bertanya, misalnya "Apa itu JIFAS?"
3. Sistem validasi input.
4. Sistem cek command dan ticket flow.
5. Sistem cek cache.
6. Sistem cek scope JIFAS.
7. Sistem mencari Knowledge Base.
8. Sistem membangun prompt berbasis hasil KB.
9. Ollama menghasilkan jawaban.
10. Sistem menyimpan history, metrics, dan cache jika aman.
11. User menerima jawaban dengan bahasa Indonesia yang mudah dipahami.

### 9.2 Tanya Navigasi Halaman

1. User bertanya "Tombol approve invoice ada di page mana?"
2. Frontend mengirim context halaman/modul aktif.
3. Assistant mencari KB terkait Invoice Approval.
4. Jawaban menyebut modul, submenu, halaman, ciri status, dan langkah cek.

### 9.3 Follow-Up Dalam Session

1. User bertanya pertanyaan awal.
2. User bertanya lanjutan, misalnya "Status apa yang biasanya muncul di halaman itu?"
3. Assistant memakai session/context untuk menjaga relevansi.
4. Jawaban tidak mengulang semua konteks dari awal jika tidak perlu.

### 9.4 Out-of-Scope

1. User bertanya topik umum non-JIFAS.
2. Sistem mendeteksi pertanyaan di luar scope.
3. Assistant menolak secara ramah dan mengarahkan kembali ke topik JIFAS.

### 9.5 Ticket Flow

1. User mengetik "Buat tiket".
2. Assistant meminta detail masalah.
3. User menjelaskan masalah.
4. Assistant mencoba memberi solusi awal jika relevan.
5. Jika user tetap ingin tiket, assistant membuat ringkasan tiket.
6. User konfirmasi judul/detail.
7. Sistem membuat tiket real di Jira.
8. Assistant menampilkan nomor tiket dan link Jira.

### 9.6 Ticket Cancel Flow

1. User mulai membuat tiket.
2. User memilih batal sebelum final create.
3. Sistem menghapus state ticket flow.
4. Tidak ada tiket Jira yang dibuat.

### 9.7 AI Learning Flow

1. Chat disimpan ke `ChatHistory`.
2. Feedback user disimpan lewat `POST /api/feedback`.
3. Scheduler/collector mengevaluasi chat.
4. Candidate masuk ke `LearningCandidates`.
5. Admin edit jawaban final.
6. Admin tandai `ReadyForPublish`.
7. Publisher membuat dokumen KB, chunk, embedding, dan pgvector.
8. Candidate berubah menjadi `Published`.

## 10. Functional Requirements

### 10.1 Chat API

| ID | Requirement | Priority | Acceptance Criteria |
|---|---|---:|---|
| CHAT-001 | Sistem menyediakan endpoint `POST /api/chat/message`. | Must | Request valid menghasilkan response JSON `ChatResponse`. |
| CHAT-002 | Sistem menerima `message`, `userId`, `sessionId`, `userRole`, `userCompCode`, `language`, `isFirstMessage`, dan `context`. | Must | Payload existing frontend tetap kompatibel. |
| CHAT-003 | Sistem mengembalikan `sender`, `message`, `success`, `source`, `sessionId`, `confidenceScore`, `suggestions`, `ticket`, dan `performanceMetrics`. | Must | UI lama tidak breaking walau `suggestions` kosong. |
| CHAT-004 | Sistem memakai `CancellationToken` dari controller ke service utama. | Must | Request cancelled tidak terus membebani dependency. |
| CHAT-005 | Sistem tetap memproses chat tanpa app-level rate limit. | Must | Acceptance test menghasilkan HTTP 429 = 0. |

### 10.2 Input Validation

| ID | Requirement | Priority | Acceptance Criteria |
|---|---|---:|---|
| VAL-001 | Message wajib diisi dan minimal 2 karakter. | Must | Empty/invalid message ditolak dengan 400 atau response validasi terkontrol. |
| VAL-002 | Message maksimal 2000 karakter. | Must | Payload terlalu panjang ditolak. |
| VAL-003 | SQL injection style dan input berbahaya ditolak. | Must | Tidak masuk KB/LLM/ticket/cache. |
| VAL-004 | Error validasi tidak dihitung sebagai bug server. | Should | Monitoring memberi label jelas. |

### 10.3 Command dan Capability Layer

| ID | Requirement | Priority | Acceptance Criteria |
|---|---|---:|---|
| CMD-001 | Sistem mendukung `/help`. | Must | Response cepat tanpa LLM. |
| CMD-002 | Sistem mendukung `/commands`. | Must | Menampilkan daftar command. |
| CMD-003 | Sistem mendukung `/status`. | Must | Menampilkan status ringkas. |
| CMD-004 | Sistem mendukung `/monitoring`. | Should | Mengarahkan user ke dashboard. |
| CMD-005 | Sistem mendukung `/ticket`, `/kb`, `/context`, `/scope`. | Should | Semua command menghasilkan response 200. |
| CMD-006 | Sistem menyediakan `GET /api/chat/capabilities`. | Must | Frontend bisa discover fitur tanpa hard-code. |

### 10.4 Scope Detection

| ID | Requirement | Priority | Acceptance Criteria |
|---|---|---:|---|
| SCOPE-001 | Sistem hanya menjawab topik JIFAS. | Must | Pertanyaan non-JIFAS diarahkan dengan aman. |
| SCOPE-002 | Sistem tidak salah menolak query JIFAS seperti history approval invoice. | Must | Query JIFAS tetap in-scope. |
| SCOPE-003 | Sistem menjaga jawaban tetap berbasis KB/context. | Must | Jawaban tidak mengarang fitur yang tidak ada basisnya. |

### 10.5 Knowledge Base dan RAG

| ID | Requirement | Priority | Acceptance Criteria |
|---|---|---:|---|
| KB-001 | Sistem menyimpan Knowledge Base dalam `KnowledgeBaseDocuments` dan `KnowledgeBaseChunks`. | Must | KB dapat dicari lewat API. |
| KB-002 | Sistem mendukung keyword search. | Must | `GET /api/KnowledgeBaseSearch/keyword` mengembalikan hasil. |
| KB-003 | Sistem mendukung semantic search. | Must | `POST /api/KnowledgeBaseSearch/semantic` mengembalikan hasil dari vector. |
| KB-004 | Sistem mendukung hybrid search. | Must | `POST /api/KnowledgeBaseSearch/query` generate embedding server-side. |
| KB-005 | Default hasil pencarian adalah top 5. | Must | TopK default 5 dan dapat dibatasi maksimal 20. |
| KB-006 | Hasil KB dipakai sebagai grounding jawaban AI. | Must | `isFromKnowledgeBase=true` jika jawaban berbasis KB. |
| KB-007 | Reindex KB tersedia lewat script. | Should | `scripts/ReindexKnowledgeBase.ps1` dapat dipakai operasional. |

### 10.6 Cache

| ID | Requirement | Priority | Acceptance Criteria |
|---|---|---:|---|
| CACHE-001 | Sistem memakai Redis sebagai cache utama. | Must | Cache hit muncul di performance metrics. |
| CACHE-002 | Redis failure tidak mematikan chat utama. | Must | Fallback memory/no-cache berjalan. |
| CACHE-003 | Pertanyaan umum memakai shared cache lintas user. | Must | Pertanyaan sama dari user berbeda bisa cache hit. |
| CACHE-004 | Pertanyaan kontekstual memakai contextual cache. | Must | Context user/page/company tidak tercampur. |
| CACHE-005 | Ticket flow tidak boleh masuk response cache. | Must | State tiket tidak bocor ke user lain. |
| CACHE-006 | Response invalid/error tidak boleh dicache. | Must | Error tidak menjadi jawaban permanen. |

### 10.7 AI Response Generation

| ID | Requirement | Priority | Acceptance Criteria |
|---|---|---:|---|
| AI-001 | Sistem memanggil Ollama untuk jawaban utama. | Must | Chat berbasis KB menghasilkan jawaban natural. |
| AI-002 | Pipeline suggestion LLM terpisah tidak dipakai. | Must | `suggestions` default kosong, latency lebih ringan. |
| AI-003 | Jawaban memakai bahasa Indonesia mudah dipahami. | Must | Jawaban tidak terlalu teknis untuk user biasa. |
| AI-004 | Jawaban menyertakan langkah berikutnya secara natural di `message`. | Should | UI tidak bergantung pada suggestions. |
| AI-005 | Sistem mencatat token input/output dan latency. | Must | Monitoring bisa menampilkan konsumsi token. |

### 10.8 Ticket Jira

| ID | Requirement | Priority | Acceptance Criteria |
|---|---|---:|---|
| TICKET-001 | Ticket dibuat melalui flow dialog, bukan endpoint terpisah untuk user. | Must | Path: detail masalah -> konfirmasi -> final create. |
| TICKET-002 | Sistem meminta detail masalah sebelum membuat tiket. | Must | Tiket tidak dibuat dari pesan kosong. |
| TICKET-003 | Sistem menampilkan ringkasan judul, kategori, dan prioritas sebelum final create. | Must | User bisa batal atau ubah judul. |
| TICKET-004 | Sistem membuat issue Jira hanya setelah final confirmation. | Must | Tidak ada tiket real saat flow cancel. |
| TICKET-005 | Response menampilkan `ticketNumber` dan `url`. | Must | User tahu tiket berhasil dibuat. |
| TICKET-006 | Jira failure ditangani terkontrol. | Must | Tidak menampilkan stack trace atau secret. |
| TICKET-007 | Payload Jira hanya mengirim field yang valid untuk create screen. | Must | Jira tidak gagal karena field invalid seperti priority yang tidak tersedia. |

### 10.9 Feedback

| ID | Requirement | Priority | Acceptance Criteria |
|---|---|---:|---|
| FB-001 | Sistem menyediakan `POST /api/feedback`. | Must | Feedback rating valid tersimpan. |
| FB-002 | Rating harus 1 sampai 5. | Must | Rating di luar range ditolak 400. |
| FB-003 | Feedback dapat memicu kandidat AI Learning. | Should | Jika `chatId` valid, candidate dapat dibuat/diperbarui. |

### 10.10 AI Learning

| ID | Requirement | Priority | Acceptance Criteria |
|---|---|---:|---|
| LEARN-001 | Sistem menyimpan kandidat pembelajaran di `LearningCandidates`. | Must | Candidate bisa dilihat admin. |
| LEARN-002 | Sistem menyimpan audit log di `LearningCandidateAuditLogs`. | Must | Perubahan status/edit dapat diaudit. |
| LEARN-003 | Candidate status mencakup `NeedsEdit`, `ReadyForPublish`, `Published`, `Archived`, `PublishFailed`. | Must | Tidak ada hard delete/reject permanen. |
| LEARN-004 | Ticket flow, invalid input, error response, dan out-of-scope non-JIFAS tidak otomatis dipublish. | Must | Policy test lulus. |
| LEARN-005 | Candidate sensitif wajib review/edit sebelum publish. | Must | Flag sensitif mencegah publish langsung. |
| LEARN-006 | Admin dapat edit question, answer, category, tags, notes. | Must | Panel/API mendukung edit. |
| LEARN-007 | Publisher membuat KB document, chunk, embedding, dan pgvector. | Must | Candidate berubah `Published` jika sukses. |
| LEARN-008 | Scheduler collector/publisher configurable. | Should | Interval dapat diatur via `AiLearning` config. |

### 10.11 Admin Learning Panel

| ID | Requirement | Priority | Acceptance Criteria |
|---|---|---:|---|
| ADMIN-001 | Panel tersedia di `/admin/learning/index.html`. | Must | Halaman dapat dibuka. |
| ADMIN-002 | API admin memakai policy `KnowledgeBaseAdmin`. | Must | Request tanpa admin key ditolak. |
| ADMIN-003 | Admin key dibaca dari `Admin__ApiKey`. | Must | Secret tidak hard-code. |
| ADMIN-004 | Admin dapat list, edit, ready, archive, collect, publish. | Must | Endpoint tersedia dan protected. |

### 10.12 Monitoring Dashboard

| ID | Requirement | Priority | Acceptance Criteria |
|---|---|---:|---|
| MON-001 | Dashboard tersedia di `/monitoring/index.html`. | Must | Halaman 200. |
| MON-002 | API monitoring tersedia di `/api/monitoring/all?minutes=60`. | Must | Response JSON berisi data terbaru. |
| MON-003 | Dashboard menampilkan total request, error rate, avg/p95 latency. | Must | Admin dapat membaca kondisi runtime. |
| MON-004 | Dashboard menampilkan token input/output, cache hit/miss, KB hit. | Must | Konsumsi AI dapat dipantau. |
| MON-005 | Dashboard menampilkan dependency failure dan latest request. | Should | Debug lebih mudah. |
| MON-006 | Section Learning Health menampilkan pending/ready/published/failed. | Should | Admin tahu kondisi AI Learning. |

### 10.13 Deployment dan Runtime

| ID | Requirement | Priority | Acceptance Criteria |
|---|---|---:|---|
| DEPLOY-001 | Runtime utama memakai Docker internal. | Must | Stack API/Postgres/Redis bisa start via script. |
| DEPLOY-002 | Docker memakai `.env`. | Must | Secret real tidak masuk git. |
| DEPLOY-003 | Startup production wajib validasi `Admin__ApiKey`. | Must | App gagal start jika secret kosong. |
| DEPLOY-004 | Jika JWT enabled, `Jwt__SigningKey` minimal 32 karakter. | Must | Startup validation mencegah config lemah. |
| DEPLOY-005 | PostgreSQL bootstrap memakai `Initialize-PostgresPgvector.sql`. | Must | DB baru bisa bootstrap idempotent. |
| DEPLOY-006 | Rate limit aplikasi tidak aktif. | Must | HTTP 429 harus 0 pada baseline sekarang. |

## 11. Public API

### 11.1 Chat

```http
POST /api/chat/message
Content-Type: application/json
```

Request:

```json
{
  "message": "Apa itu JIFAS?",
  "userId": "user-001",
  "sessionId": "session-001",
  "correlationId": "optional-correlation-id",
  "userRole": "FINA:KI",
  "currentModule": "Home",
  "companyId": "KI",
  "userCompCode": "KI",
  "userEmpCode": "EMP001",
  "language": "id",
  "isFirstMessage": true,
  "context": {
    "currentPage": "/Home",
    "activeModule": "Home",
    "pageTitle": "Home",
    "selectedDocumentId": null,
    "documentType": null,
    "documentStatus": null,
    "customData": {}
  }
}
```

Response:

```json
{
  "sender": "JIFAS AI Assistant",
  "message": "Jawaban AI...",
  "errors": [],
  "correlationId": "request-id",
  "source": "JIFAS (5 hasil)",
  "timestamp": "2026-06-09 09:00:00",
  "success": true,
  "sessionId": "session-001",
  "isFromKnowledgeBase": true,
  "confidenceScore": 0.85,
  "suggestions": [],
  "ticket": null,
  "knowledgeBaseResults": [],
  "performanceMetrics": {
    "inputValidationMs": 0,
    "cacheLookupMs": 1,
    "scopeDetectionMs": 2,
    "kbSearchMs": 100,
    "llmResponseMs": 3000,
    "totalMs": 3200,
    "wasCacheLit": false,
    "cacheScope": "shared"
  }
}
```

### 11.2 Capabilities

```http
GET /api/chat/capabilities
```

Mengembalikan daftar command dan capability assistant.

### 11.3 Health

```http
GET /health
GET /api/chat/health
GET /api/KnowledgeBaseSearch/health
```

### 11.4 Knowledge Base Search

```http
GET  /api/KnowledgeBaseSearch/keyword?query=invoice&topK=5
POST /api/KnowledgeBaseSearch/semantic
POST /api/KnowledgeBaseSearch/search
POST /api/KnowledgeBaseSearch/query
```

### 11.5 Feedback

```http
POST /api/feedback
```

Request:

```json
{
  "chatId": 123,
  "sessionId": "session-001",
  "messageId": "message-001",
  "userId": "user-001",
  "rating": 5,
  "comment": "Jawabannya membantu"
}
```

### 11.6 Learning Admin

Semua endpoint wajib memakai `X-Admin-Api-Key`.

```http
GET  /api/learning/stats
GET  /api/learning/candidates
GET  /api/learning/candidates/{id}
PUT  /api/learning/candidates/{id}/edit
POST /api/learning/candidates/{id}/ready
POST /api/learning/candidates/{id}/archive
POST /api/learning/collect/run
POST /api/learning/publish/run
```

### 11.7 Monitoring

```http
GET /api/monitoring/all?minutes=60
GET /monitoring/index.html
GET /admin/learning/index.html
```

## 12. Data Model

### 12.1 Knowledge Base

| Entity | Fungsi |
|---|---|
| `KnowledgeBaseDocuments` | Metadata dokumen KB: title, category, tags, active flag. |
| `KnowledgeBaseChunks` | Potongan dokumen dan embedding pgvector. |

### 12.2 Conversation

| Entity | Fungsi |
|---|---|
| `ChatHistory` | Riwayat pertanyaan, jawaban, source, confidence, session. |
| `Chats` | Model chat lama jika masih dipakai kompatibilitas. |
| `UserFeedbacks` | Feedback rating dan komentar user. |
| `UserMemory` | Profil/memori user jangka panjang. |

### 12.3 Monitoring

| Entity | Fungsi |
|---|---|
| `AiUsageLog` | Log request AI, token, latency, module, status, cache, type. |
| `Metrics` | Entity pendukung metrik. |

### 12.4 AI Learning

| Entity | Fungsi |
|---|---|
| `LearningCandidates` | Kandidat knowledge hasil evaluasi chat/feedback. |
| `LearningCandidateAuditLogs` | Audit perubahan candidate. |

## 13. Non-Functional Requirements

### 13.1 Performance

| ID | Requirement | Target |
|---|---|---|
| NFR-PERF-001 | Command cepat tidak memanggil LLM. | P95 < 1 detik. |
| NFR-PERF-002 | Cache hit harus sangat cepat. | Umumnya < 100 ms pada runtime sehat. |
| NFR-PERF-003 | KB query cached/shared harus cepat. | Target < 2 detik setelah warmup. |
| NFR-PERF-004 | Chat LLM boleh lebih lama karena tergantung model. | Harus tetap selesai atau timeout terkontrol. |
| NFR-PERF-005 | Baseline 50 VU tanpa rate limit. | HTTP 429 = 0, HTTP 5xx = 0, restart count 0. |

### 13.2 Reliability

| ID | Requirement | Target |
|---|---|---|
| NFR-REL-001 | Redis down tidak mematikan chat. | Fallback memory/no-cache. |
| NFR-REL-002 | Jira down tidak mematikan chat. | Response gagal terkontrol. |
| NFR-REL-003 | Ollama timeout tidak membocorkan exception mentah. | User mendapat pesan aman. |
| NFR-REL-004 | Postgres transient failure tercatat di monitoring/log. | Tidak silent failure. |
| NFR-REL-005 | Container restart count harus 0 pada smoke/stress baseline. | Wajib untuk acceptance. |

### 13.3 Security

| ID | Requirement | Target |
|---|---|---|
| NFR-SEC-001 | Secret tidak masuk git, docs, README, report, atau console output. | Wajib. |
| NFR-SEC-002 | Admin API protected dengan `KnowledgeBaseAdmin`. | Wajib. |
| NFR-SEC-003 | Production validasi `Admin__ApiKey`. | Wajib. |
| NFR-SEC-004 | JWT production supported jika enabled. | Signing key minimal 32 karakter. |
| NFR-SEC-005 | Security headers dasar aktif. | `nosniff`, `DENY`, CSP production. |
| NFR-SEC-006 | Input validation menahan payload berbahaya. | SQL-style test ditolak. |

### 13.4 Privacy dan Data Governance

| ID | Requirement | Target |
|---|---|---|
| NFR-PRIV-001 | Data personal/dokumen spesifik tidak otomatis menjadi KB. | Wajib. |
| NFR-PRIV-002 | Candidate sensitif harus `SensitiveReviewRequired`. | Wajib. |
| NFR-PRIV-003 | Tidak ada hard delete untuk learning audit. | Archive, bukan delete. |
| NFR-PRIV-004 | Source citation/performance metrics tidak perlu ditampilkan ke user akhir. | Bisa tetap ada di API untuk debug. |

### 13.5 Maintainability

| ID | Requirement | Target |
|---|---|---|
| NFR-MAIN-001 | Service dipisah berdasarkan tanggung jawab. | Chat, KB, cache, Jira, monitoring, learning. |
| NFR-MAIN-002 | Script operasional terdokumentasi. | README/docs/runbook. |
| NFR-MAIN-003 | Test unit untuk policy penting. | xUnit lulus. |
| NFR-MAIN-004 | PRD dan runbook disimpan di `docs/`. | Wajib. |

## 14. AI Quality Requirements

| ID | Requirement | Acceptance Criteria |
|---|---|---|
| Q-001 | Jawaban harus berdasarkan KB jika pertanyaan JIFAS. | Source `JIFAS` atau KB result tersedia. |
| Q-002 | Jawaban harus mudah dipahami user biasa. | Tidak terlalu teknis kecuali diminta. |
| Q-003 | Jawaban harus membedakan masalah user vs masalah IT. | Hak akses/server/API diarahkan ke IT. |
| Q-004 | Jawaban tidak boleh menyuruh bypass approval. | Safety prompt dan KB policy. |
| Q-005 | Jawaban ticket harus rapi, punya deskripsi masalah dan konteks. | Jira ticket terbaca tim IT. |
| Q-006 | Low confidence harus ditandai untuk learning/review. | Candidate `NeedsEdit` atau flag quality. |

## 15. Monitoring Requirements

Dashboard harus bisa menjawab pertanyaan:

- Apakah chatbot sedang aktif?
- Berapa request masuk dalam rentang waktu?
- Berapa error utama?
- Berapa rata-rata dan p95 latency?
- Apakah cache bekerja?
- Berapa token input/output yang dipakai?
- Modul apa yang paling sering ditanyakan?
- Apakah dependency bermasalah?
- Apakah AI Learning queue sehat?
- Request terbaru sukses atau gagal?

Metrik minimal:

| Metric | Deskripsi |
|---|---|
| Total request | Jumlah request chat/command/cache dalam rentang waktu. |
| Success/error count | Kualitas request utama. |
| Avg latency | Waktu rata-rata end-to-end. |
| P95 latency | Batas durasi 95 persen request. |
| Input token | Estimasi token prompt/input. |
| Output token | Estimasi token jawaban. |
| Cache hit/miss | Efektivitas Redis/cache. |
| KB hit | Persentase jawaban berbasis KB. |
| Dependency failure | Redis/Postgres/Ollama/Jira issue. |
| Learning health | Pending, ready, published, failed. |

## 16. Testing dan Acceptance

### 16.1 Local Gate

```powershell
dotnet build --no-restore
dotnet test --no-restore
powershell -ExecutionPolicy Bypass -File scripts\Test-ProductionReadiness.ps1
docker compose --env-file .env config --quiet
```

Acceptance:

- Build sukses.
- Unit test sukses.
- Readiness gate sukses.
- Tidak ada secret terdeteksi di file template/docs.

### 16.2 Runtime Smoke

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Start-DockerStack.ps1 -SkipTests
powershell -ExecutionPolicy Bypass -File scripts\Run-FullFeatureSmokeTest.ps1
```

Coverage wajib:

- KB question.
- Invoice/PUM/Payment/Report.
- Page navigation.
- Follow-up context.
- Out-of-scope.
- Greeting/gratitude.
- SQL-style validation.
- Ticket cancel path.
- KB hybrid query.
- Monitoring endpoint.

Acceptance:

- Semua checks passed.
- HTTP 429 = 0.
- HTTP 5xx = 0.
- Containers healthy.
- Restart count 0.

### 16.3 Real Jira Test

Real Jira ticket hanya dibuat jika user/admin eksplisit meminta.

Acceptance:

- Ticket dibuat di Jira.
- Response menampilkan ticket key dan URL.
- Jira issue bisa diverifikasi lewat Jira API/browser.
- Tidak ada secret di log/report.

### 16.4 AI Learning Test

Coverage:

- Unauthorized learning API ditolak.
- Admin key valid bisa stats/list.
- Collector manual sukses.
- Candidate bisa diedit/ready/archive.
- Publisher hanya memproses `ReadyForPublish`.

Acceptance:

- Candidate sensitif tidak dipublish otomatis.
- Archive tidak menghapus data.
- Publish failure masuk `PublishFailed`.

### 16.5 Stress Baseline

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Run-ChatStressTest.ps1 -VirtualUsers 50
```

Acceptance:

- Total success 50/50 untuk baseline clean jika dependency cukup kuat.
- HTTP 429 = 0.
- HTTP 5xx = 0.
- API/Postgres/Redis healthy.
- Restart count 0.
- Cache hit terlihat untuk repeated/common question.

## 17. Deployment Requirements

### 17.1 Environment

File:

- `.env.example`: template aman.
- `.env`: file lokal Docker tunggal.

Secret wajib production:

- `ConnectionStrings__DefaultConnection`
- `ConnectionStrings__Redis`
- `Admin__ApiKey`
- `Jira__BaseUrl`
- `Jira__ProjectKey`
- `Jira__AccountEmail`
- `Jira__ApiToken`

JWT jika enabled:

- `Jwt__Enabled=true`
- `Jwt__SigningKey` minimal 32 karakter
- `Jwt__Audience`
- `Jwt__Authority` jika memakai issuer eksternal

### 17.2 Docker Stack

Service:

- `jifas-assistant-api`
- `jifas-postgres`
- `jifas-redis`

Port default:

- API: `8888`
- Postgres: `5432`
- Redis: `6379`

### 17.3 Database Bootstrap

Bootstrap resmi:

```text
Jifas.Assistant/Database/Initialize-PostgresPgvector.sql
```

Harus idempotent:

- `CREATE EXTENSION IF NOT EXISTS vector`
- tabel runtime dibuat jika belum ada,
- schema learning tersedia,
- pgvector column tersedia,
- startup aman dijalankan berulang.

## 18. Risiko dan Mitigasi

| Risiko | Dampak | Mitigasi |
|---|---|---|
| Ollama lambat/panas | Latency tinggi | Cache, command fast path, warmup, monitoring p95. |
| Embedding timeout | KB hybrid lambat | Retry/fallback, warmup, monitor dependency. |
| Redis down | Cache hilang | Fallback memory/no-cache. |
| Jira field berubah | Ticket create gagal | Query createmeta, config field optional, error terkontrol. |
| KB tidak lengkap | Jawaban kurang akurat | AI Learning + admin curation. |
| Data sensitif masuk learning | Risiko privacy | Sensitive flag, admin edit wajib, no auto publish. |
| User menganggap AI selalu benar | Salah proses | Confidence, wording aman, arahkan IT untuk akses/server. |
| Stress test membuat server panas | Downtime lokal | Jalankan bertahap, monitor dashboard, stop jika overheating. |
| Real Jira test spam tiket | Noise di Jira | Flag eksplisit dan prefix `[TEST]`. |
| Secret bocor | Security incident | `.env`, secret scan, jangan print token. |

## 19. Success Metrics

### 19.1 Product Metrics

- Penurunan pertanyaan berulang ke IT.
- Jumlah KB hit.
- Jumlah cache hit untuk pertanyaan umum.
- Jumlah ticket yang berhasil dibuat lewat chatbot.
- Jumlah candidate AI Learning yang dipublish.
- Feedback rating rata-rata.

### 19.2 Operational Metrics

- Error rate.
- Avg latency.
- P95 latency.
- HTTP 5xx count.
- HTTP 429 count.
- Container restart count.
- Redis/Postgres/Ollama/Jira health.
- Token input/output per request.

### 19.3 Quality Metrics

- Confidence score rata-rata.
- Low confidence topics.
- Bad feedback topics.
- Candidate `NeedsEdit`.
- Candidate `PublishFailed`.
- Source stability.

## 20. Rollout Plan

### Phase 1 - Internal Validation

- Jalankan Docker stack internal.
- Test company `KI`.
- Validasi KB, chat, ticket cancel, monitoring, learning API.
- Jalankan 50 VU baseline.

Exit criteria:

- Build/test/readiness pass.
- Smoke pass.
- 429 = 0.
- 5xx = 0.
- Container healthy.

### Phase 2 - Limited User Trial

- Aktifkan widget untuk user pilot.
- Monitor dashboard harian.
- Kumpulkan feedback.
- Admin review learning candidate.

Exit criteria:

- Feedback mayoritas positif.
- Tidak ada incident secret/data.
- Ticket flow stabil.

### Phase 3 - Wider Rollout

- Perluas ke user/modul lain.
- Tambah KB dari AI Learning approved.
- Tinjau kebutuhan rate limit atau queue policy.
- Evaluasi observability eksternal.

## 21. Roadmap

### Short Term

- Rapikan warning EF query ordering.
- Tambah report visual untuk learning queue.
- Tambah golden questions lebih lengkap.
- Buat script test AI Learning lifecycle end-to-end.
- Tambah dokumentasi integrasi JIFAS Web final.

### Medium Term

- Admin dashboard dengan authentication UI.
- Export monitoring ke CSV/PDF.
- Knowledge approval multi-role.
- Per-module quality score.
- Alert jika Ollama/Redis/Postgres/Jira unhealthy.

### Long Term

- Observability stack resmi.
- Multi-company policy lengkap.
- Model comparison dashboard.
- Fine-tuning jika curated dataset sudah cukup matang.
- Automated regression eval sebelum deploy.

## 22. Open Questions

1. Apakah rollout production tetap hanya `KI` atau perlu multi-company sejak awal?
2. Siapa owner final untuk approve Knowledge Base: IT, Finance, Accounting, atau gabungan?
3. Apakah Jira ticket perlu auto-assign ke user/team tertentu?
4. Apakah dashboard perlu login UI sendiri atau cukup admin API key?
5. Berapa target latency bisnis yang dianggap nyaman untuk user?
6. Apakah historical chat perlu retention policy khusus?
7. Apakah user boleh melihat source KB atau cukup jawaban final?
8. Apakah perlu SLA untuk ticket creation failure?

## 23. Glossary

| Istilah | Arti |
|---|---|
| JIFAS | Jababeka Integrated Finance and Accounting System. |
| RAG | Retrieval-Augmented Generation, AI menjawab dengan konteks dokumen. |
| KB | Knowledge Base. |
| pgvector | Extension PostgreSQL untuk vector similarity search. |
| Ollama | Runtime model AI lokal/internal. |
| Redis | Distributed cache. |
| Jira | Sistem ticketing/support. |
| Candidate | Kandidat knowledge hasil chat/feedback. |
| ReadyForPublish | Candidate sudah diedit dan siap masuk KB resmi. |
| Cache hit | Jawaban diambil dari cache, tidak generate ulang AI. |
| P95 latency | Durasi yang mencakup 95 persen request tercepat. |

## 24. Lampiran: Endpoint Checklist

| Endpoint | Tujuan | Public/Admin |
|---|---|---|
| `GET /` | Info API sederhana | Public internal |
| `GET /api` | Info API sederhana | Public internal |
| `POST /api/chat/message` | Chat utama | Public internal |
| `GET /api/chat/health` | Health chat module | Public internal |
| `GET /api/chat/capabilities` | Daftar command/capability | Public internal |
| `GET /health` | Docker health | Public internal |
| `GET /api/KnowledgeBaseSearch/health` | Health KB search | Public internal |
| `GET /api/KnowledgeBaseSearch/keyword` | Keyword search | Public internal |
| `POST /api/KnowledgeBaseSearch/query` | Hybrid search server-side embedding | Public internal |
| `POST /api/feedback` | Feedback user | Public internal |
| `GET /api/monitoring/all` | Monitoring API | Public internal |
| `GET /monitoring/index.html` | Dashboard monitoring | Public internal |
| `GET /admin/learning/index.html` | Admin learning panel | Admin UI |
| `GET /api/learning/stats` | Learning stats | Admin |
| `GET /api/learning/candidates` | Candidate list | Admin |
| `PUT /api/learning/candidates/{id}/edit` | Edit candidate | Admin |
| `POST /api/learning/candidates/{id}/ready` | Mark ready | Admin |
| `POST /api/learning/candidates/{id}/archive` | Archive candidate | Admin |
| `POST /api/learning/collect/run` | Manual collector | Admin |
| `POST /api/learning/publish/run` | Manual publisher | Admin |

## 25. Lampiran: Definition of Done

Sebuah perubahan dianggap siap merge/deploy jika:

- Build sukses.
- Unit test sukses.
- Readiness gate sukses.
- Smoke test fitur utama sukses.
- Tidak ada secret baru di git/docs/report.
- Tidak ada perubahan kontrak API breaking.
- Dashboard tetap dapat dibuka.
- Health endpoint hijau.
- Docker stack healthy.
- Jika menyentuh ticket flow, test cancel path wajib lulus.
- Jika membuat real Jira ticket, ticket key dan URL harus diverifikasi.
- Jika menyentuh AI Learning, unauthorized/admin flow wajib dites.
