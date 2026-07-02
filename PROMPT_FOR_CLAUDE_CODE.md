# JIFAS Assistant: Natural Conversational AI Transformation

## Project Context

JIFAS Assistant adalah chatbot internal berbasis Ollama + RAG untuk sistem ERP keuangan Jababeka.

**Tech Stack:**
- LLM: Ollama qwen3:8b (local inference)
- Vector DB: PostgreSQL + pgvector (embedding search, 2560 dims)
- Embedding Model: qwen3-embedding:4b
- Cache: Redis (TTL 30 min untuk conversation context)
- Framework: ASP.NET Core
- Database tables: `ChatHistory` (conversation), `KnowledgeBaseChunks` (RAG), `UserMemories` (cross-session)

**Current Architecture:**
```
Frontend (separate repo) → API Controller → ChatService (orchestrator)
  → OutOfScopeDetector (keyword-based)
  → ConversationIntelligenceService (build context from DB)
  → KnowledgeBaseSearchService (hybrid: keyword + pgvector semantic)
  → PromptEngineeringService (build prompt with KB context)
  → OllamaService (call qwen3:8b)
  → Save to ChatHistory table
```

---

## The Problem

### User's Goal

> "Aku mau chatbot ku bisa **ngobrol seperti ChatGPT** — natural, bisa saling balas chat bubble, paham konteks percakapan sebelumnya. Walaupun ada 1000 bubble chat, tetap nyambung semua. User bisa pakai bahasa apapun (formal, gaul, singkatan, bahasa campur), dan AI harus ngerti itu bagian dari percakapan yang sedang berlangsung.
>
> **Tapi tetap hanya dalam lingkup JIFAS** — bukan cuma nge-handle follow-up, tapi keseluruhan conversation flow harus stay in scope. Kalau keluar JIFAS (cuaca, berita, resep), baru ditolak."

### User's Frustration (Direct Quote)

> "Aku bingung banget gimana caranya utk perbaiki ini agar bisa saling chat jawab chat di room AI ini. Kalau di AI ku sekarang kan gabisa kaya gini — kayak misal aku chat lalu AI ku menjawab, lalu ku balas lagi hasil dari chat AI tadi, eh tiba-tiba ternyata context nya baru lagi, kayak ngga nyambung banget. **Walaupun disitu ada 1000 bubble chat, ya itu harus tetap nyambung semuanya dan BISA CHATTAN SEPERTI BIASA DENGAN CHATBOT KU.**
>
> Dan juga hasil jawabannya itu **terlalu banyak enter, ga rapih banget keliatannya format nya**, ntah dari UI atau dari backend nya itu ya."

### Full Real Conversation Transcript (From Production)

**SessionId:** `a9033674-034e-465e-a32c-30fadb85c359` (verified consistent across all messages)

```
[Bubble 1]
User: "Halo magang it 8! Saya JIFAS Assistant. Bagaimana saya bisa membantu?"
(This appears to be a greeting — might be UI pre-fill or system message)

[Bubble 2]
User: "aku lagi menu mana"
AI: "Kamu sedang berada di PUM - Tax - JIFAS dalam modul Pum. Berikut detail lokasinya:

Modul: Pum
Sub-menu: Tax (Pajak)
Halaman: PUM - Tax - JIFAS (URL relatif: /Pum/Tax/Index)

Tampilan: Halaman ini menampilkan daftar dokumen terkait pajak (misalnya, pengajuan pajak, laporan pajak, atau data terkait). Terdapat tombol seperti Preview, Export Excel, atau Print untuk mengelola data.

Catatan:
Jika kamu melihat daftar dokumen pajak dengan opsi untuk melihat detail atau mengedit, kamu berada di area pengelolaan pajak.
Pastikan kamu memiliki akses ke modul Pum dan sub-menu Tax (biasanya tersedia untuk departemen Accounting atau keuangan).
Jika kamu ingin melanjutkan ke bagian lain, tanya lagi ya! 😊"

Source: KB | Score: 10.1

[Bubble 3]
User: "apa aja yang bisa kulakukan disini"
AI: "Di modul CashBank, Anda bisa melakukan beberapa tindakan berikut berdasarkan kebutuhan bisnis:

Tujuan: Finalisasi transaksi ke buku besar.
Validasi: Pastikan data lengkap (nomor dokumen, tanggal, jumlah, kode akun).
Contoh:
- Input: Nomor Dokumen: UM123, Tanggal: 2023-10-05, Jumlah: 5.000.000, Kode Akun: 1101.
Konsekuensi jika tidak diisi: Transaksi tidak bisa diposting dan akan muncul pesan error: 'Cannot continue with empty data. Please contact your administrator.'

Tujuan: Membatalkan dokumen yang sudah diproses.
Validasi:
- Isi alasan pembatalan (misal: 'Kesalahan input', 'Dokumen tidak valid').
- Pastikan dokumen belum diposting.

[...continues with more fields and validations...]

Apakah Anda ingin tahu lebih detail tentang salah satu tindakan di atas, atau ada masalah spesifik yang ingin Anda selesaikan? 😊"

Source: KB | Score: 13.6
❌ PROBLEM: User sedang di PUM-Tax, tapi AI jawab tentang CashBank — context loss

[Bubble 4]
User: "barusan aku nanya apa"
AI: "Hmm, sepertinya itu bukan topik JIFAS. Saya bisa bantu soal Invoice, Payment, PUM, Budget, Receiving, atau Approval. Mau tanya yang mana?"

Source: Luar Scope | Score: 3.0
❌ REJECTED sebagai out-of-scope

[Bubble 5]
User: "gajelas"
AI: "Maaf, 'gajelas' di luar area saya. Saya fokus bantu soal JIFAS - Invoice, Payment, PUM, Budget, dan Approval. Ada yang mau ditanyakan dari topik itu?"

Source: Luar Scope | Score: 3.1
❌ REJECTED sebagai out-of-scope

[Bubble 6]
User: "gajelas itu out of scope?"
AI: "Jawaban:
Berdasarkan konteks yang tersedia, pertanyaan tentang 'gajelas' tidak tercakup dalam referensi yang diberikan. Berikut penjelasan terkait field yang relevan di modul PPUM&Realization (Pum):

Field 'Void Reason'
- Tipe: Text (string)
- Validasi:
- Wajib diisi jika dokumen dibatalkan (VOID).
- Minimal 1 karakter, tidak boleh kosong.
[...continues with unrelated field explanations...]

Langkah berikutnya:
Apakah Anda merujuk pada field tertentu di modul Pum? Jika ya, mohon jelaskan detailnya agar saya bisa bantu lebih lanjut."

Source: KB | Score: 14.8
❌ AI tidak paham "gajelas" = "ga jelas" (bahasa gaul untuk "not clear")
❌ Instead of clarifying previous answer, AI responds with random field documentation

[Bubble 7]
User: "coba tolong singkat"
AI: "Halo! 👋
Untuk mempercepat, berikut field utama di TaxApproval (Pum) yang perlu diperhatikan:

DPP Header
- Tipe: Nominal (angka)
- Validasi: Harus diisi, tidak boleh kosong.
- Contoh: 10.000.000
- Konsekuensi: Jika kosong, muncul error 'Cannot continue with empty data.'

[...continues with more fields...]

Pastikan data sesuai format untuk menghindari error. Jika masih ada kendala, bisa dijelaskan lebih detail? 😊"

Source: KB | Score: 10.0
✅ AI attempts to simplify (responds to "singkat" = brief)
❌ But still doesn't address the original confusion about CashBank vs PUM-Tax

[Bubble 8]
User: "tadi aku nanya apa"
AI: "Maaf, 'tadi aku nanya apa' di luar area saya. Saya fokus bantu soal JIFAS - Invoice, Payment, PUM, Budget, dan Approval. Ada yang mau ditanyakan dari topik itu?"

Source: Luar Scope | Score: 5.0
❌ REJECTED sebagai out-of-scope
❌ This is a META-QUESTION asking about conversation history — should be answered from ChatHistory table

[Bubble 9]
User: "hah gimana?"
AI: "Maaf, 'hah gimana?' di luar area saya. Saya fokus bantu soal JIFAS - Invoice, Payment, PUM, Budget, dan Approval. Ada yang mau ditanyakan dari topik itu?"

Source: Luar Scope | Score: 3.6
❌ REJECTED sebagai out-of-scope
❌ This is a clarification request in casual language — should trigger clarification of previous answer

[Bubble 10]
User: "how many you understand about jifas?"
AI: "How Much Do I Understand About JIFAS?
Here's a structured breakdown of my knowledge based on your context (Accounting department, working with PUM and Dashboard):

Centralized Financial Control: Manages cash flow, budgeting, approvals, and reporting for Jababeka.

Key Modules:
- Invoice: Track vendor payments, approvals, and tax validations.
- PUM (Purchase & Usage Management): Monitor procurement, usage, and cost allocation.
[...extensive breakdown...]

Next Step: Would you like a step-by-step guide for processing a PUM transaction or reconciling a CashBank entry? 📊"

Source: KB | Score: 9.4
✅ English recognized and answered
❌ But previous context (bubble 2-9) not referenced at all

[Bubble 11]
User: "can you tell me a little?"
AI: "PUM (Pengajuan Umum) di JIFAS adalah modul untuk mengelola permintaan pengadaan atau pembelian yang memerlukan persetujuan dari berbagai divisi, termasuk divisi pajak (Tax). [...]"

Source: KB | Score: 10.8
✅ Follow-up recognized (because "a little" is ambiguous enough to trigger KB search)
```

### Analysis of This Conversation

**What went wrong:**
1. **Context loss between bubbles:** Bubble 3 answered about CashBank when user is on PUM-Tax page
2. **Meta-questions rejected:** "barusan aku nanya apa", "tadi aku nanya apa" treated as OOS
3. **Casual language rejected:** "gajelas" (ga jelas = not clear), "hah gimana" (hah how?) treated as OOS
4. **No clarification mechanism:** When user says "gajelas", AI should say "Oh sorry, let me clarify..." not dump unrelated docs
5. **Format issues:** Excessive newlines, bullet point formatting inconsistent
6. **English works but disconnected:** Bubble 10-11 work in English but don't reference Indonesian conversation before it

**What user expects (Natural Conversation):**

```
[After Bubble 3's wrong answer about CashBank]
User: "gajelas"
AI: "Oh maaf kurang jelas ya. Oke aku klarifikasi lagi — kamu sekarang di halaman PUM Tax (bukan CashBank, aku salah tadi). Di PUM Tax ini, kamu bisa:
1. Lihat daftar dokumen PUM yang butuh validasi pajak
2. Preview detail dokumen
3. Approve atau reject berdasarkan data pajak (PPN/PPH)
4. Export ke Excel atau print untuk laporan

Yang mana yang mau kamu lakukan?"

User: "tadi aku nanya apa"
AI: "Kamu tadi nanya 'aku lagi menu mana' di bubble pertama, terus nanya 'apa aja yang bisa kulakukan disini'. Aku jawab tentang CashBank (salah, harusnya PUM Tax). Terus kamu bilang 'gajelas' dan aku udah klarifikasi sekarang. Ada yang mau ditanya lagi tentang PUM Tax?"
```

**Key Insight:**
> User doesn't care about technical implementation. User hanya mau: "Chatbot yang bisa ngobrol normal kayak manusia, paham context dari 1000 bubble sebelumnya, dan stay fokus di JIFAS."

---

## Technical Investigation

### How Context Currently Works

**Data flow:**
1. User kirim pesan → `ChatService.ProcessMessageAsync()`
2. **Out-of-scope check (keyword-based)** ← happens BEFORE context is built
3. Build conversation context: `ConversationIntelligenceService.BuildContextAsync(sessionId)`
   - Fetch last 5 turns from `ChatHistory` table (WHERE SessionId = ...)
   - Format sebagai string + cache (TTL 30 min)
4. KB search: hybrid keyword + pgvector cosine similarity
5. Build prompt dengan KB results + conversation context
6. Send to Ollama via `/api/chat` endpoint dengan messages array:
   ```json
   {
     "messages": [
       {"role": "system", "content": "..."},
       {"role": "user", "content": "turn 1"},
       {"role": "assistant", "content": "turn 1 response (truncated 400 chars)"},
       ...last 5 turns,
       {"role": "user", "content": "[current prompt with KB context]"}
     ]
   }
   ```
7. Save response ke `ChatHistory` table
8. Return response to frontend

**Findings:**
- ✅ History DOES save to DB correctly (SessionId consistent)
- ✅ Context IS passed to Ollama (last 5 turns, both as messages array & formatted text)
- ❌ Out-of-scope decision made BEFORE context is available
- ❌ Cache tidak di-invalidate setelah save → next request dapat stale context
- ❌ Scope enforcement via **hardcoded keyword arrays**, not semantic understanding

---

## Root Cause: Keyword-Based Approach is Fundamentally Wrong

Sistem saat ini menggunakan **rule-based, string-matching approach** untuk SEMUA decision points:

### Files dengan Hardcoded Patterns

1. **`OutOfScopeDetector.cs`:**
   - `ObviousInScopeKeywords[]`: 40+ keywords ("invoice", "payment", "pum", "posting", "gl", "approve", ...)
   - `ObviousOutOfScopeKeywords[]`: 30+ keywords ("cuaca", "weather", "berita", "politik", "resep", ...)

2. **`ConversationIntelligenceService.cs`:**
   - `FollowUpIndicators[]`: ("itu", "ini", "tadi", "lanjut", "terus", ...)
   - `ReferencePronouns[]`: ("itu", "ini", "dia", "nya", ...)
   - `TopicKeywords{}`: Dictionary mapping topics to keyword lists

3. **Potentially others** throughout codebase

### Why This Doesn't Scale

**Problem 1: Language Variations**
```
✅ "cara posting invoice" → detected (keywords: posting, invoice)
❌ "gmn caranya post inv" → NOT detected (singkatan/abbreviations)
❌ "how to post invoice" → NOT detected (English)
❌ "bikin invoice baru gmn" → NOT detected (synonym: bikin vs cara)
❌ "td gw tny ap" → NOT detected (bahasa gaul Jaksel)
```

**Problem 2: Context-Dependent Scope**

Scope bukan tentang keyword — it's about **conversational context**.

Example:
```
Conversation:
  User: "cara posting invoice di JIFAS"
  AI: "Klik menu Invoice, lalu pilih Post..."
  User: "gimana kalau lupa password?"

Current system:
  → Keyword check: No "invoice"/"payment" keywords
  → Ask Ollama (WITHOUT context): "gimana kalau lupa password?" → generic question
  → Decision: Out of scope ❌

Should be:
  → Context: User is working on JIFAS invoice posting
  → "lupa password" dalam konteks JIFAS workflow → valid question (JIFAS login issue)
  → Decision: In scope ✅
```

**Key insight:**
> Scope boundary is NOT fixed based on keywords. It's **contextual and dynamic**. Setiap percakapan tentang JIFAS bisa menyentuh berbagai topik (password, error, troubleshooting, navigation, recap) selama masih dalam konteks sistem JIFAS.

**Problem 3: Meta-Conversation**

User perlu bisa:
- Recap: "tadi kita ngomongin apa?"
- Clarify: "maksudmu gimana sih?"
- Navigate: "balik ke topik awal dong"
- Correct: "eh bukan, maksudku..."

Ini **valid conversational moves** tapi current system reject sebagai out-of-scope karena tidak ada keyword JIFAS.

**Problem 4: Maintenance Nightmare**

Setiap kali ada:
- Bahasa baru (bahasa gaul trend TikTok, istilah Jaksel baru)
- Synonym baru
- Multi-language usage

Engineer harus:
1. Update hardcoded array
2. Re-compile
3. Re-deploy

This is not sustainable.

---

## User's Core Concerns (Direct Quotes)

### 1. "Kenapa frasa-nya semua di-hardcode? Itu tidak ideal."

> "Tapi kenapa frasa nya smua di hardcoded gitu ya? itu kan ngga ideal bgt sbnrnya. juga pasti kan dari jutaan user yang pake itu nanti akan berbeda beda bahasanya dan promptnya. **Apakah ada cara lain agar AI sendiri yang menentukan itu? Kita hanya ngasih rules nya aja.**"

**User's expectation:**
- AI should determine intent and scope semantically, not through pattern matching
- We only provide the rules/principles
- System should adapt to ANY language variation without code changes

### 2. "Bukan hanya follow-up — keseluruhan konteks general JIFAS."

> "Nah tapi utk hardcoded frasa utk keseluruhan bukan hanya utk follow up, tapi utk konteks general jifas. jadi aku emang mau ngebuat **ai ku ini bisa chattan seperti biassa seprti chat gpt gitu, tapi tetep harus dan hanya dalam lingkup jifas.** apakah ai bisa melakukan itu???"

**User's expectation:**
- Natural conversation flow like ChatGPT
- Understands context across unlimited bubble chats
- Stays within JIFAS scope naturally, not through keyword filtering

### 3. "Jangan kasih langkah teknis — biar Claude yang berpikir."

> "Di prompt for claude mu itu keliatannya kamu terlalu ngasih langkah teknis apa aja yang harus dilakukan. kan aku bilang **ngga usah dikasih tau biar claude code yang berfikir.** tolong rapihin."

**User's expectation:**
- Define the problem and goals clearly
- Let Claude Code decide the technical implementation
- Focus on principles, not prescriptive steps

### 4. "Cache tidak langsung di-invalidate."**

- Bug confirmed: `ConversationContext_{sessionId}` cache TTL 30 min, tidak dihapus setelah save
- Impact: AI doesn't "remember" messages dari 10 detik lalu sampai cache expired

### 5. "Format response berantakan — banyak enter, tidak rapi."**

- Ollama output inconsistent formatting (extra newlines, markdown symbols)
- UI mungkin tidak render markdown, tampil sebagai plain text

---

## Your Mission

**Transform this chatbot from keyword-based search engine into natural conversational AI.**

### Core Objectives

1. **Natural conversation flow** — like talking to a human JIFAS expert who remembers everything
2. **Context continuity** — 1000 bubble chats should all connect logically
3. **Semantic scope enforcement** — stay in JIFAS naturally, not through pattern matching
4. **Language flexibility** — formal, casual, slang, abbreviations, multi-language all work
5. **Meta-conversation support** — recap history, clarify answers, navigate topics

### Critical Principle

> **Use AI to understand intent and scope semantically. Give the AI clear rules and examples, then let it decide. Avoid hardcoded keyword arrays.**

### Constraints

- **Must stay ONLY in JIFAS scope** — weather, news, recipes, politics must be rejected
- Use existing infrastructure (Ollama, pgvector, ChatHistory table, Redis cache)
- Don't break frontend API contracts
- Performance budget: +300ms acceptable for AI classification

### Non-Goals

- Don't change frontend (separate repo, out of scope)
- Don't migrate to different LLM provider
- Don't rebuild KB search from scratch

---

## Known Issues

You'll encounter these symptoms. **Think holistically** — don't just patch symptoms, fix root causes.

1. **Cache stale** — `ConversationContext_{sessionId}` not invalidated after save
2. **Pipeline order wrong** — scope check before context built
3. **Hardcoded patterns everywhere** — keyword arrays drive all decisions
4. **No meta-question handler** — "tadi aku nanya apa" goes to KB search, fails
5. **Response formatting messy** — excessive newlines, inconsistent markdown

---

## Success Criteria

### Functional Requirements

**Test 1: Follow-up with casual language**
```
User: "cara posting invoice"
AI: [KB response with steps]
User: "gajelas"
Expected: AI clarifies previous answer, not rejected as OOS
```

**Test 2: Meta-question**
```
User: [asks 3 JIFAS questions]
User: "tadi aku nanya apa aja?"
Expected: AI lists previous questions from session history
```

**Test 3: Multi-language**
```
User: "how to post invoice?"
AI: [English response]
User: "what if error?"
Expected: AI continues in English, understands as follow-up
```

**Test 4: Context-dependent scope**
```
User: "cara posting invoice"
AI: [response]
User: "gimana kalau lupa password?"
Expected: In-scope (JIFAS login context), not rejected
```

**Test 5: True out-of-scope**
```
User: "cuaca hari ini"
Expected: Rejected with polite redirect to JIFAS topics
```

**Test 6: Long conversation continuity**
```
User: [10 JIFAS questions and answers]
User: "kembali ke pertanyaan pertama tadi dong"
Expected: AI recalls first question, provides reference
```

**Test 7: Response formatting**
```
User: [any JIFAS question]
Expected: 
  - Clean formatting (no excessive newlines)
  - Consistent bullet points
  - If UI supports markdown: proper rendering
  - If UI doesn't: plain text should still be readable
```

### Non-Functional Requirements

- **Latency:** P95 < 3 seconds end-to-end (acceptable to add ~300ms for AI classification)
- **Accuracy:** False OOS rejection rate < 5% (currently ~30% based on user report)
- **Scalability:** Minimal hardcoded patterns — AI-driven decisions
- **Maintainability:** Clear, explainable intent classification logic

---

## Design Principles (Not Prescriptive Steps)

**Think about these questions:**
- How would ChatGPT maintain context across 1000 messages while staying on-topic?
- What's the RIGHT way to determine scope semantically?
- How can we make this work for ANY language variation without code changes?

**Key architectural principle:**
> Use AI to understand intent and scope. Give the AI clear rules + concrete examples, then trust its semantic understanding. Avoid keyword matching.

**Example thinking:**
```
❌ Wrong: if (msg.contains("invoice")) → inScope = true
✅ Right: AI sees: "User asked about invoice posting 3 bubbles ago. 
                   Now says 'gajelas'. This is clarification request 
                   within JIFAS context, not OOS."
```

---

## What Success Looks Like

When you're done:
1. **Users can chat naturally** — any language style, context flows across unlimited bubbles
2. **Scope enforced semantically** — JIFAS conversations stay in scope, weather/news rejected
3. **No engineer involvement for language variations** — system adapts automatically
4. **Clean, maintainable architecture** — future developers understand the flow

**Test it like this:**
- Have a 10-bubble conversation mixing formal, casual, slang, English, meta-questions
- All should flow naturally
- Try "cuaca hari ini" → should reject
- Try "tadi aku nanya apa" → should recap from ChatHistory

---

## Final Notes

**You have full autonomy on implementation.**

- Create new services, refactor existing ones, change pipeline order — whatever makes sense
- Just keep API contracts stable (don't break frontend)
- Focus on doing it RIGHT, not just making tests pass

**The goal is simple:**

> Make this chatbot feel like talking to a human JIFAS expert who has perfect memory, understands any language style, and stays laser-focused on helping with JIFAS.

Build something you'd be proud to use yourself. 🚀