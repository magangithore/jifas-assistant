# Ultrathink — Deep analysis command

## Usage
```
/ultrathink [task description]
```

## Prompt Template

Konteks:
Aku baru selesai refactor chatbot JIFAS (commit 0fc61e9) dari pipeline
classify->route (4-5 Ollama calls/message) menjadi SINGLE-PASS: satu call
GenerateConversationalResponseAsync yang membundel [history 15 turn] + [RAG] +
[scope rules] + [format rules], dan model sendiri yang menentukan intent
(follow-up / clarification / greeting / OOS / new topic).

File inti:
- Jifas.Assistant/Services/OllamaAIService.cs
  (GenerateConversationalResponseAsync, BuildConversationHistorySection,
   BuildRagSection, BuildJifasSystemInstruction, CleanResponse)
- Jifas.Assistant/Services/ChatService.cs (pipeline single-pass, ~line 474)
- Jifas.Assistant/Services/IOllamaService.cs
- Jifas.Assistant/Services/ConversationIntelligenceService.cs
  (CompactSessionAsync ADA tapi TIDAK dipakai; BuildContextAsync maxTurns=5)

## Task

TUGAS — jangan ubah kode dulu, cuma baca + analisa:
1. Baca kelima file di atas sampai paham alur end-to-end satu request chat.
2. Pikirkan mendalam (ultrathink) soal:
   a. Correctness: apakah history 15 turn benar-benar sampai ke prompt Ollama
      persis seperti yang diklaim? Trace variabelnya, jangan asumsi.
   b. Context coherence: aku PUNYA requirement "walaupun 1000 bubble, tetap
      nyambung". Sekarang turn >15 cuma diganti placeholder
      "[... N pesan sebelumnya ...]" (BUKAN summary riil). Analisa dampak
      nyata: skenario percakapan apa yang akan putus konteks karena ini?
   c. Konsistensi maxTurns: BuildContextAsync pakai 5, history section pakai 15.
      Apakah ada dua sumber "history" yang saling tabrakan / redundan / beda?
   d. Token budget: dengan 15 turn (truncate 300 char) + RAG + rules, berapa
      estimasi token yang dikirim ke Ollama? Ada risiko kepotong context window?
   e. Latency & per-request cost: apakah masih ada call tersembunyi (embedding,
      RAG, quality check) yang ikut jalan di path utama?
3. Output: satu laporan terstruktur berisi temuan + severity (Critical/High/
   Medium/Low) + rekomendasi konkret per temuan. TANPA mengubah kode.
   Kalau ada klaim, tunjukkan nomor baris sebagai bukti.

Jangan menyenangkan aku. Kalau arsitekturnya punya cacat mendasar, katakan.

## Arguments
$ARGUMENTS
