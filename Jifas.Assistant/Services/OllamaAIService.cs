using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Ollama AI Service - menggunakan Ollama API untuk generasi respons
    /// Model dikonfigurasi via Ollama:Model di appsettings.json
    /// </summary>
    public class OllamaAIService : IOllamaService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILoggerService _logger;
        private readonly IPromptEngineeringService _promptEngineering;
        private readonly IKnowledgeBaseSearchService _kbSearch;
        private readonly IMonitoringService _monitoring;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string _baseUrl;
        private readonly float _temperature;
        private readonly int _maxOutputTokens;

        private const string OLLAMA_CHAT_ENDPOINT = "/api/chat";

        // AsyncLocal is safe across await boundaries (unlike [ThreadStatic])
        private static readonly AsyncLocal<string?> _currentCallType  = new();
        private static readonly AsyncLocal<string?> _currentUserId    = new();
        private static readonly AsyncLocal<string?> _currentSessionId = new();
        private static readonly AsyncLocal<string?> _currentModule    = new();
        // Recent conversation turns to include in Ollama messages array (true multi-turn)
        private static readonly AsyncLocal<List<(string user, string assistant)>?> _conversationTurns = new();

        /// <inheritdoc />
        public void SetCallContext(string? userId, string? sessionId, string? activeModule, string callType = "chat")
        {
            _currentCallType.Value  = callType;
            _currentUserId.Value    = userId;
            _currentSessionId.Value = sessionId;
            _currentModule.Value    = activeModule;
        }

        /// <inheritdoc />
        public void SetConversationHistory(List<(string user, string assistant)>? turns)
        {
            _conversationTurns.Value = turns;
        }

        public OllamaAIService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILoggerService logger,
            IPromptEngineeringService promptEngineering,
            IKnowledgeBaseSearchService kbSearch,
            IMonitoringService monitoring)
        {
            _httpClient = httpClient;
            _monitoring = monitoring;
            _configuration = configuration;
            _logger = logger;
            _promptEngineering = promptEngineering ?? throw new ArgumentNullException(nameof(promptEngineering));
            _kbSearch = kbSearch ?? throw new ArgumentNullException(nameof(kbSearch));

            _apiKey = _configuration["Ollama:ApiKey"] ?? string.Empty;
            _model = _configuration["Ollama:Model"] ?? "qwen3:8b";
            _baseUrl = _configuration["Ollama:BaseUrl"] ?? "http://10.0.12.54:11434";
            _temperature = _configuration.GetValue<float>("Ollama:Temperature", 0.3f);
            _maxOutputTokens = _configuration.GetValue<int>("Ollama:MaxTokens", 2048);

            var timeout = _configuration.GetValue<int>("Ollama:TimeoutSeconds", 120);
            _httpClient.Timeout = TimeSpan.FromSeconds(timeout);

            _logger.LogInformation("[OllamaAIService] Initialized with model: {0}", _model);
        }

        /// <summary>
        /// Generate response menggunakan Ollama dengan knowledge base context
        /// </summary>
        public async Task<string> GenerateResponseAsync(string userQuery, List<KnowledgeBaseResult> kbResults, string? sessionContext = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userQuery))
                    return "Pertanyaan tidak valid. Silakan berikan pertanyaan yang jelas.";

                _logger.LogInformation("[OllamaAIService] Processing query: {0}", userQuery);

                // Always build a prompt and call the AI - even with no KB results
                // The rich system instruction has enough JIFAS domain knowledge to answer
                string intelligentPrompt;
                if (kbResults == null || kbResults.Count == 0)
                {
                    _logger.LogWarning("[OllamaAIService] No KB results for query: {0} - using system knowledge only", userQuery);
                    // Build a lean prompt that relies on the system instruction
                    intelligentPrompt = await _promptEngineering.BuildIntelligentPromptAsync(
                        userQuery, new List<KnowledgeBaseResult>(), sessionContext: sessionContext);
                }
                else
                {
                    _logger.LogInformation("[OllamaAIService] Found {0} KB results (relevance: {1:P0}), context: {2}",
                        kbResults.Count, kbResults.Max(r => r.Score), sessionContext ?? "(none)");
                    intelligentPrompt = await _promptEngineering.BuildIntelligentPromptAsync(
                        userQuery, kbResults, sessionContext: sessionContext);
                }

                var response = await CallOllamaApiAsync(intelligentPrompt);

                if (string.IsNullOrEmpty(response))
                    return "Maaf, terjadi kesalahan dalam memproses jawaban. Silakan coba lagi.";

                _logger.LogInformation("[OllamaAIService] Generated response: {0} chars", response.Length);
                return response;
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError("[OllamaAIService] HTTP error calling Ollama API: {0}", httpEx, new object[] { httpEx.Message });
                return "Maaf, layanan AI saat ini tidak tersedia. Silakan coba lagi nanti.";
            }
            catch (Exception ex)
            {
                _logger.LogError("[OllamaAIService] Error generating response: {0}", ex, new object[] { ex.Message });
                return "Maaf, terjadi kesalahan dalam memproses permintaan Anda.";
            }
        }

        /// <summary>
        /// Generate 3 follow-up suggestions. Uses a short timeout (12s) so it never
        /// blocks the main response. Falls back to defaults if Ollama is slow.
        /// </summary>
        public async Task<List<string>> GenerateSuggestionsAsync(string userQuery, string response)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userQuery) || string.IsNullOrWhiteSpace(response))
                    return GetDefaultSuggestions();

                // Keep prompt very small: only the first 200 chars of the answer
                var snippet = response.Length > 200 ? response.Substring(0, 200) : response;
                var suggestionsPrompt =
                    $"Topik JIFAS: \"{TruncateForContext(userQuery, 80)}\"\n" +
                    $"Ringkasan jawaban: {snippet}\n\n" +
                    "Tulis 3 pertanyaan lanjutan singkat (maks 10 kata, Bahasa Indonesia).\n" +
                    "Format: 1. ... 2. ... 3. ...";

                // Hard-limit suggestions generation to 12 seconds
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(12));
                var text = await CallOllamaApiInternalAsync(suggestionsPrompt, maxTokens: 128, ct: cts.Token);
                var parsed = ExtractSuggestions(text);
                return parsed.Count > 0 ? parsed : GetDefaultSuggestions();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[OllamaAIService] Suggestions generation skipped: {0}", ex.Message);
                return GetDefaultSuggestions();
            }
        }

        /// <summary>
        /// Scope check tanpa LLM call - sudah dihandle OutOfScopeDetector via keyword matching.
        /// Method ini dipertahankan untuk kompatibilitas interface.
        /// </summary>
        public Task<bool> IsInScopeAsync(string userQuery)
        {
            // Keyword-based check tanpa menggunakan AI API
            var outOfScope = new[] { "cuaca", "berita", "politik", "film", "resep", "crypto", "bitcoin", "agama" };
            var query = userQuery?.ToLowerInvariant() ?? "";
            var isOut = outOfScope.Any(k => query.Contains(k));
            return Task.FromResult(!isOut);
        }

        /// <summary>
        /// Memanggil Ollama API dengan retry on error
        /// </summary>
        public async Task<string> CallOllamaApiAsync(string prompt)
        {
            const int maxRetries = 2;
            var retryDelaysMs = new[] { 3000, 8000 };

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await CallOllamaApiInternalAsync(prompt);
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("TooManyRequests") && attempt < maxRetries)
                {
                    var delayMs = retryDelaysMs[attempt];
                    _logger.LogWarning("[OllamaAIService] Rate limited (429), retry {0}/{1} in {2}ms...", (attempt + 1), maxRetries, delayMs);
                    await Task.Delay(delayMs);
                }
            }

            throw new HttpRequestException("Ollama API rate limit exceeded after retries");
        }

        private async Task<string> CallOllamaApiInternalAsync(
            string prompt,
            int? maxTokens = null,
            System.Threading.CancellationToken ct = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string? errorMsg = null;
            string responseText = string.Empty;

            try
            {
                var endpoint = $"{_baseUrl}{OLLAMA_CHAT_ENDPOINT}";

                // Build messages array — skip conversation history for fast (suggestion) calls
                var includeTurns = maxTokens == null; // only include turns for full main responses
                var messages = new List<object>
                {
                    new { role = "system", content = BuildJifasSystemInstruction() }
                };

                // Inject up to last 3 conversation turns for context (main call only)
                if (includeTurns)
                {
                    var turns = _conversationTurns.Value;
                    if (turns != null && turns.Count > 0)
                    {
                        foreach (var turn in turns.TakeLast(3))
                        {
                            if (!string.IsNullOrWhiteSpace(turn.user))
                                messages.Add(new { role = "user", content = turn.user });
                            if (!string.IsNullOrWhiteSpace(turn.assistant))
                                messages.Add(new { role = "assistant", content = TruncateForContext(turn.assistant, 400) });
                        }
                    }
                }

                // Add current user prompt
                messages.Add(new { role = "user", content = prompt });

                // Ollama /api/chat request body
                var requestBody = new
                {
                    model = _model,
                    messages,
                    stream = false,
                    options = new
                    {
                        temperature = _temperature,
                        top_p = 0.85,
                        top_k = 40,
                        num_predict = maxTokens ?? _maxOutputTokens
                    }
                };

                var jsonContent = JsonConvert.SerializeObject(requestBody, Formatting.None);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                _logger.LogDebug("[OllamaAIService] Calling Ollama endpoint: {0}", endpoint);

                var response = await _httpClient.PostAsync(endpoint, httpContent, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"[OllamaAIService] API error {response.StatusCode}: {errorBody}");
                    throw new HttpRequestException($"Ollama API returned {response.StatusCode}: {errorBody}");
                }

                responseText = await response.Content.ReadAsStringAsync();
                sw.Stop();
                _logger.LogDebug("[OllamaAIService] Response received, parsing...");

                // ── Extract Ollama performance metrics ──────────────────────
                var parsed = ExtractOllamaMetrics(responseText, prompt, sw.ElapsedMilliseconds);
                await _monitoring.RecordAsync(parsed);
                // ────────────────────────────────────────────────────────────

                return ParseOllamaResponse(responseText);
            }
            catch (HttpRequestException)
            {
                sw.Stop();
                errorMsg = "HTTP error calling Ollama";
                await _monitoring.RecordAsync(new AiCallMetrics
                {
                    Model       = _model,
                    CallType    = _currentCallType.Value ?? "chat",
                    PromptLengthChars = prompt.Length,
                    TotalDurationMs   = sw.ElapsedMilliseconds,
                    IsError     = true,
                    ErrorMessage = errorMsg,
                    CreatedAt   = DateTime.UtcNow
                });
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError("[OllamaAIService] Error calling Ollama API: {0}", ex, new object[] { ex.Message });
                await _monitoring.RecordAsync(new AiCallMetrics
                {
                    Model       = _model,
                    CallType    = _currentCallType.Value ?? "chat",
                    PromptLengthChars = prompt.Length,
                    TotalDurationMs   = sw.ElapsedMilliseconds,
                    IsError     = true,
                    ErrorMessage = ex.Message,
                    CreatedAt   = DateTime.UtcNow
                });
                throw;
            }
        } // end CallOllamaApiInternalAsync

        /// <summary>
        /// Extracts token counts and timing from Ollama /api/chat response JSON.
        /// Ollama returns durations in nanoseconds; we convert to milliseconds.
        /// Fields: prompt_eval_count, eval_count, total_duration, load_duration,
        ///         prompt_eval_duration, eval_duration.
        /// </summary>
        private AiCallMetrics ExtractOllamaMetrics(string responseJson, string prompt, long wallClockMs)
        {
            try
            {
                var j = JObject.Parse(responseJson);

                long NsToMs(string field) => j[field] != null
                    ? (long)(j[field]!.Value<long>() / 1_000_000.0)
                    : 0;

                var promptTokens     = j["prompt_eval_count"]?.Value<int>() ?? 0;
                var completionTokens = j["eval_count"]?.Value<int>() ?? 0;
                var evalDurationMs   = NsToMs("eval_duration");
                var tokensPerSec     = evalDurationMs > 0
                    ? completionTokens / (evalDurationMs / 1000.0) : 0;

                var responseContent  = j["message"]?["content"]?.ToString() ?? string.Empty;

                _logger.LogInformation(
                    "[OllamaAIService][Metrics] prompt={0}t completion={1}t total={2}ms tps={3:F1} | callType={4} userId={5}",
                    promptTokens, completionTokens, wallClockMs, tokensPerSec, 
                    _currentCallType.Value ?? "(null)", _currentUserId.Value ?? "(null)");

                return new AiCallMetrics
                {
                    UserId               = _currentUserId.Value,
                    SessionId            = _currentSessionId.Value,
                    ActiveModule         = _currentModule.Value,
                    Model                = _model,
                    CallType             = _currentCallType.Value ?? "chat",
                    PromptTokens         = promptTokens,
                    CompletionTokens     = completionTokens,
                    TotalDurationMs      = NsToMs("total_duration") > 0 ? NsToMs("total_duration") : wallClockMs,
                    LoadDurationMs       = NsToMs("load_duration"),
                    PromptEvalDurationMs = NsToMs("prompt_eval_duration"),
                    EvalDurationMs       = evalDurationMs,
                    PromptLengthChars    = prompt.Length,
                    ResponseLengthChars  = responseContent.Length,
                    IsError              = false,
                    CreatedAt            = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[OllamaAIService] Could not extract metrics: {0}", ex.Message);
                return new AiCallMetrics
                {
                    Model = _model, CallType = _currentCallType.Value ?? "chat",
                    TotalDurationMs = wallClockMs, PromptLengthChars = prompt.Length,
                    CreatedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Parse response dari Ollama API format JSON.
        /// Format: { "message": { "role": "assistant", "content": "..." } }
        /// </summary>
        private string ParseOllamaResponse(string responseJson)
        {
            try
            {
                var json = JObject.Parse(responseJson);

                // Ollama /api/chat response: message.content
                var text = json["message"]?["content"]?.ToString();

                if (!string.IsNullOrEmpty(text))
                    return text.Trim();

                // Fallback: cek field "response" (Ollama /api/generate format)
                var fallback = json["response"]?.ToString();
                if (!string.IsNullOrEmpty(fallback))
                    return fallback.Trim();

                _logger.LogWarning("[OllamaAIService] Could not extract text from response: {0}", responseJson.Substring(0, Math.Min(200, responseJson.Length)));
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError("[OllamaAIService] Error parsing Ollama response: {0}", ex, new object[] { ex.Message });
                return string.Empty;
            }
        }

        /// <summary>
        /// System instruction mendalam untuk JIFAS AI Persona Agent
        /// Dibangun dari Knowledge Base nyata JIFAS PT Jababeka Tbk
        /// </summary>
        private string BuildJifasSystemInstruction()
        {
            return
"""
Kamu adalah JIFAS AI Assistant - AI Persona Agent resmi untuk sistem JIFAS (Jababeka Integrated Finance & Accounting System) milik PT Jababeka Tbk dan seluruh anak perusahaannya.

=== IDENTITAS & PERSONA ===
Namamu: JIFAS AI
Peranmu: Expert JIFAS System Advisor & Business Process Consultant
Karaktermu: Cerdas, profesional, jujur, helpful, dan bicara seperti rekan kerja senior yang sangat paham sistem.
Bahasa: Bahasa Indonesia yang natural, hangat, dan mudah dipahami oleh user bisnis.

=== PRINSIP UTAMA ===
1. Jawab dengan bahasa mudah dipahami user bisnis.
2. Error teknis (API, token, server error, data tidak loading) -> arahkan ke IT Help Desk.
3. Masalah akses, role, menu tidak muncul, login -> arahkan ke IT/Admin JIFAS.
4. Masalah COA, jurnal, posting, Trial Balance, Balance Sheet -> arahkan ke Accounting.
5. Masalah approval, pembayaran, budget, PUM, invoice, cash/bank -> arahkan ke Finance.
6. Masalah PPN, PPH, NPWP, faktur pajak, bukti potong, tax correction -> arahkan ke Tax.
7. Dokumen sudah Posted/Confirmed/Paid/Void/Removed -> JANGAN sarankan edit biasa.
8. Dokumen final yang salah -> arahkan ke Void, Reverse, atau koreksi resmi.
9. Jangan mengarang data transaksi.
10. Untuk eskalasi: minta user siapkan nomor dokumen, company code, status, screenshot error, waktu kejadian.

=== TENTANG JIFAS ===
JIFAS adalah sistem ERP keuangan terintegrasi berbasis web milik Jababeka Group.
Fungsi: mengelola invoice, PUM, receiving, payment, cashbank, accounting, budget, dan report secara terpusat.
JIFAS adalah mesin kontrol keuangan perusahaan - tidak ada transaksi bergerak tanpa proses approval, checking, tax validation, dan posting.

URL Akses:
- KIJ, GBC, MPK, JM, BW, TL, SPPK: http://jifas.jababeka.com atau http://10.0.8.57/
- JI, ICTEL, NGE: http://jifasweb.jiinfra.com/ atau http://10.10.1.30/
- BP, UP, TS: http://jifas-bp.bekasipower.co.id/ atau http://10.12.0.47/
- KIK: http://jifas.kik.com atau http://10.5.1.240/

Login: username Windows TANPA @jababeka.com | Password: password Windows domain.
Jika tidak bisa login: cek URL, username tanpa domain, Caps Lock, clear cache, coba Chrome/Edge, hubungi IT jika tetap gagal.

=== MODUL-MODUL JIFAS ===

1. ACCOUNT / LOGIN / USER ACCESS
   - Login dengan akun Windows tanpa @domain
   - Role: WMTR (IT/Webmaster), USER (umum), USRL (bisa pilih dept di PUM), FINA (Finance)
   - Masalah login/akses/role -> eskalasi IT

2. HOME / DASHBOARD
   - Halaman awal setelah login, ringkasan status dokumen perusahaan
   - Card: Billing, Invoice, PUM, Receiving, Payment, Cashbank, SPK, Over Budget
   - Read-only; berdasarkan perusahaan & periode aktif
   - Semua angka 0: kemungkinan periode belum diatur atau data belum ada

3. MASTER DATA (Fondasi Seluruh Modul)
   - Company: profil perusahaan, cabang, kode company
   - Employee: data karyawan (dipakai di PUM)
   - Vendor: supplier dan rekanan bisnis
   - Division/Department: struktur organisasi
   - COA (Chart of Accounts): kode akun keuangan untuk semua transaksi
   - Account Period: buka/tutup periode akuntansi - WAJIB terbuka sebelum input transaksi
   - List COA: daftar CoA aktif
   - Report Setup: konfigurasi laporan keuangan
   - Budget: input dan kelola anggaran per cost center
   - Roles & Authorization: WMTR=IT, USER=umum, USRL=pilih dept di PUM, FINA=Finance

4. INVOICE (Pengajuan Tagihan/Biaya)
   - Sub-modul: Finance Invoice, Head Approval, Tax, Create, ApprovalIncomplete
   - Alur: Create -> Submit -> Finance Checking -> Head Approval -> Tax Approval -> Posting
   - Status: Draft -> Need Finance Checking -> Need Head Approval -> Need Tax Approval -> Need Posting -> Posted
   - Tombol: Save, Submit, Approve, Reject, Post, Void
   - Draft: bisa diedit | Posted: TIDAK bisa diedit
   - ApprovalIncomplete: muncul jika approval chain belum lengkap

5. PUM (Perjalanan Uang Muka / Perjalanan Dinas)
   - Sub-modul: Pengajuan, Head Approval, Tax Approval, PPUM & Realization, OLD PUM
   - Alur: Pengajuan -> Finance Approval -> Head Approval -> Distribusi -> Realisasi -> Settlement
   - Status: Draft -> Submitted -> Need Finance Approval -> Need Head Approval -> Distributed -> Need Realization -> Need Settlement -> Settled
   - Settlement: laporan realisasi pengeluaran vs uang muka
   - Realisasi < uang muka -> karyawan kembalikan sisa
   - Realisasi > uang muka -> perusahaan bayar kekurangan
   - Role USRL: bisa pilih department/divisi saat buat PUM
   - OLD PUM: akses data historis PUM lama

6. RECEIVING (Penerimaan Barang/Jasa)
   - Sub-modul: Create, Receive of Sales, Tax Approval, Approval of Unidentified RV
   - RV = Receive Voucher (nomor dokumen penerimaan)
   - Alur: Create RV -> Finance Checking -> Tax Approval (jika ada pajak) -> Posted
   - ReceiveTax: NPWP vendor dan alamat Wajib Pajak HARUS lengkap sebelum approve
   - Jika tax rate salah -> Reject, buat dokumen baru
   - Unidentified RV: penerimaan yang belum bisa diidentifikasi vendornya

7. PAYMENT (Pembayaran)
   - Sub-modul: Payment Invoice, Payment PUM, PaymentTax, List BG
   - Alur: Finance Approval -> Head Approval -> Posting -> Paid
   - Metode: Transfer Bank, BG (Bank Garansi), Cek, Giro
   - List BG: daftar Bank Garansi tersedia
   - PaymentTax: pembayaran dengan aspek perpajakan

8. CASHBANK (Kas & Bank)
   - Sub-modul: Receive (penerimaan kas), Payment (pengeluaran kas), PaymentTax, ReceiveTax
   - Pengelolaan kas dan rekening bank perusahaan
   - Alur: Create -> Approval -> Posting
   - Setelah Posted -> tidak bisa diedit

9. OVER BUDGET
   - Ketika transaksi melebihi batas anggaran
   - Budget Status: Remaining (sisa), Committed (terikat), Actual (realisasi)
   - Sub-modul: Finance Approval, Head Approval
   - Butuh approval khusus atau revisi budget terlebih dahulu

10. SPK (Surat Perintah Kerja / Kontrak)
    - Pengelolaan kontrak pekerjaan atau pengadaan
    - Status: Draft -> Confirmed -> (terkait ke invoice/payment)
    - Dokumen Confirmed tidak bisa diedit sembarangan

11. REPORT (Laporan Keuangan)
    - Budget Card: kartu anggaran per cost center/departemen
    - Budget Committed: komitmen anggaran belum terealisasi
    - Budget Payment: realisasi pembayaran vs anggaran
    - Budget Realization: laporan realisasi anggaran
    - Budget Receive: penerimaan terkait anggaran
    - Cashbank Detail: detail transaksi kas dan bank
    - Cashbank Recap: rekap kas dan bank per periode
    - Daily Cashflow: arus kas harian
    - Deposito Aktif: daftar deposito aktif
    - Inquiry AP: saldo hutang ke vendor
    - Inquiry AR: saldo piutang dari customer
    - Inquiry CB: saldo kas dan bank
    - Inquiry PUM: status uang muka karyawan
    - Realisasi PUM: laporan realisasi uang muka
    - Saldo Buku Bank: rekonsiliasi buku vs rekening bank
    - Commited Realization: komitmen vs realisasi

12. ACCOUNTING (Jurnal & Buku Besar)
    - GL (General Ledger): buku besar, jurnal manual
    - AP (Account Payable): hutang ke vendor/supplier
    - AR (Account Receivable): piutang dari customer
    - Posting: merekam transaksi ke buku besar
    - Bulk Posting: posting banyak dokumen sekaligus
    - Acc Period: buka/tutup periode akuntansi

13. CONSOLIDATION ACCOUNTING
    - Konsolidasi laporan keuangan dari beberapa perusahaan/cabang dalam group

=== STATUS GLOBAL JIFAS ===
Draft/New | Need Head Approval | Need Supervisor Approval | Need Finance Approval |
Need Finance Checking | Need Tax Approval | Need Accounting Checking | Need Posting |
Ready To Pay | Paid | Posted | Complete | Rejected | Void/Removed | Confirmed | Need Reverse

=== ALUR APPROVAL UMUM ===
Creator (buat & submit) -> Head/Supervisor Approval -> Finance Checking/Approval -> Tax Approval -> Accounting Checking -> Posting ke GL -> Payment/Paid/Complete

=== ATURAN PENTING ===
- Dokumen Posted: TIDAK bisa diedit. Harus Void atau Reverse.
- Periode akuntansi ditutup: TIDAK bisa input transaksi baru.
- Reject = kembali ke Creator untuk diperbaiki.
- Void = pembatalan dokumen final.
- Login: username Windows TANPA @jababeka.com.

=== CARA MENJAWAB (ATURAN WAJIB) ===
1. GROUNDED: Jawab berdasarkan Knowledge Base yang diberikan - sumber kebenaran utama.
2. HONEST: Jika KB tidak punya info, katakan: "Informasi ini belum tersedia di KB JIFAS. Hubungi IT Help Desk: it@jababeka.com"
3. NO HALLUCINATION: Jangan mengarang langkah, menu, atau fitur yang tidak ada di KB.
4. CONTEXT AWARE: Prioritaskan jawaban sesuai modul/halaman aktif user.
5. ACTIONABLE: Langkah-langkah konkret dan bisa langsung dilakukan.
6. NATURAL: Seperti rekan senior yang paham sistem.
7. STRUCTURED: Gunakan bullet/numbering untuk langkah-langkah.
8. CONCISE: Jawab yang ditanya, tidak perlu preamble panjang.

=== ESKALASI ===
- IT Help Desk (login, akses, error teknis, API): it@jababeka.com
- Finance (approval, pembayaran, budget, PUM): bagian keuangan
- Accounting (COA, jurnal, posting, laporan): bagian akuntansi
- Tax (PPN, PPH, NPWP, faktur pajak): bagian perpajakan

Kamu adalah wajah digital JIFAS - bantu user dengan penuh keyakinan, keakuratan, dan empati.
""";
        }

        private string BuildNoResultsMessage(string query) =>
            $"Maaf, saya tidak menemukan informasi tentang '{query}' di Knowledge Base JIFAS. " +
            "Silakan coba dengan kata kunci berbeda, atau hubungi Tim IT Help Desk di it@jababeka.com untuk bantuan lebih lanjut.";

        private List<string> ExtractSuggestions(string responseText)
        {
            var suggestions = new List<string>();
            if (string.IsNullOrEmpty(responseText)) return GetDefaultSuggestions();

            try
            {
                var lines = responseText.Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var match = Regex.Match(line.Trim(), @"^\d+[.)]\s*(.+)$");
                    if (match.Success)
                    {
                        var suggestion = match.Groups[1].Value.Trim();
                        if (suggestion.Length >= 5 && suggestion.Length <= 200)
                            suggestions.Add(suggestion);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[OllamaAIService] Error parsing suggestions: {0}", ex.Message);
            }

            return suggestions.Count > 0 ? suggestions.Take(3).ToList() : GetDefaultSuggestions();
        }

        private static List<string> GetDefaultSuggestions() => new List<string>
        {
            "Bagaimana cara membuat dokumen baru di JIFAS?",
            "Apa saja status yang ada dalam workflow approval?",
            "Bagaimana cara melihat riwayat transaksi di JIFAS?"
        };

        private static string TruncateForContext(string text, int maxLength) =>
            text?.Length > maxLength ? text.Substring(0, maxLength) + "..." : text ?? string.Empty;
    }
}





