# Claude Code Reference Analysis

Tanggal analisa: 04 Juni 2026

## Sumber yang Dianalisa

- `https://github.com/tanbiralam/claude-code`
- `https://github.com/yasasbanukaofficial/claude-code`

Kedua repository tersebut menyatakan bahwa source aslinya berasal dari leak/sourcemap dan merupakan properti Anthropic. Karena itu, JIFAS Assistant tidak menyalin source code, struktur internal detail, atau implementasi spesifik dari repository tersebut.

Yang diambil hanya pola arsitektur level tinggi yang aman dan umum:

- Command system untuk perintah cepat.
- Capability/tool discovery agar UI dan dokumentasi bisa membaca kemampuan AI.
- Diagnostics/help command agar user tahu cara memakai assistant.
- Scope guard agar assistant tetap menjawab di domain yang benar.

## Yang Diimplementasikan ke JIFAS Assistant

### 1. Command Layer

File:

```text
Jifas.Assistant/Services/AssistantCommandService.cs
```

Command yang tersedia:

- `/help`
- `/commands`
- `/status`
- `/monitoring`
- `/ticket`
- `/kb`
- `/context`
- `/scope`

Command ini diproses cepat tanpa memanggil Knowledge Base atau LLM. Tujuannya agar bantuan dasar tidak menambah beban Ollama dan tetap responsif saat server ramai.

### 2. Capability Metadata

File:

```text
Jifas.Assistant/models/AssistantCapability.cs
```

Capability yang dipublikasikan:

- Knowledge Base RAG.
- Jira ticket flow.
- Page context.
- Monitoring dashboard.
- JIFAS scope guard.

### 3. Endpoint Capability

Endpoint:

```http
GET /api/chat/capabilities
```

Endpoint ini membantu frontend atau dokumentasi membaca daftar command dan kemampuan assistant tanpa hard-code.

### 4. Chat Pipeline Integration

File:

```text
Jifas.Assistant/Services/ChatService.cs
```

Slash command diproses setelah validasi input dan sebelum ticket flow/cache/KB/LLM. Ini membuat command:

- cepat;
- tidak masuk response cache;
- tidak memicu pencarian KB;
- tidak memicu call Ollama;
- tetap tersimpan di chat history untuk audit.

## Yang Tidak Diimplementasikan

- Tidak menyalin source code dari repo referensi.
- Tidak menambah agent swarm atau multi-agent karena belum dibutuhkan untuk JIFAS support chatbot.
- Tidak menambah plugin marketplace karena backend JIFAS saat ini butuh stabilitas, bukan extensibility publik.
- Tidak menambah terminal UI karena JIFAS Assistant berjalan sebagai Web API.
- Tidak menambah command yang membuat side effect langsung, seperti auto-create ticket tanpa konfirmasi.

## Dampak ke Produksi

- Tidak ada breaking change ke `POST /api/chat/message`.
- Command memakai response shape `ChatResponse` yang sudah ada.
- Field `suggestions` tetap kosong.
- Tidak ada secret baru.
- Tidak ada dependency package baru.
- Unit test baru menjaga command behavior dasar.

