using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Ollama AI Service — implementasi IOllamaService menggunakan Ollama (qwen3:8b).
    /// Mempertahankan seluruh kecerdasan JIFAS: system instruction mendalam,
    /// context-aware suggestions, hybrid scope detection, dan prompt engineering.
    /// Ollama API: POST /api/chat  (OpenAI-compatible chat format)
    /// </summary>
    public class OllamaService : IOllamaService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILoggerService _logger;
        private readonly IPromptEngineeringService _promptEngineering;
        private readonly IKnowledgeBaseSearchService _kbSearch;

        private readonly string _baseUrl;
        private readonly string _model;
        private readonly float _temperature;
        private readonly int _maxTokens;
        private readonly int _timeoutSeconds;

        public OllamaService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILoggerService logger,
            IPromptEngineeringService promptEngineering,
            IKnowledgeBaseSearchService kbSearch)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _promptEngineering = promptEngineering ?? throw new ArgumentNullException(nameof(promptEngineering));
            _kbSearch = kbSearch ?? throw new ArgumentNullException(nameof(kbSearch));

            _baseUrl = _configuration["Ollama:BaseUrl"] ?? "http://10.0.12.54:11434";
            _model = _configuration["Ollama:Model"] ?? "qwen3:8b";
            _temperature = _configuration.GetValue<float>("Ollama:Temperature", 0.3f);
            _maxTokens = _configuration.GetValue<int>("Ollama:MaxTokens", 2048);
            _timeoutSeconds = _configuration.GetValue<int>("Ollama:TimeoutSeconds", 120);
            _httpClient.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);

            _logger.LogInformation("[OllamaService] Initialized — BaseUrl: {0}, Model: {1}", _baseUrl, _model);
        }

        // ─────────────────────────────────────────────────────────────
        // IOllamaService — GenerateResponseAsync
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Generate respons JIFAS berdasarkan konteks Knowledge Base.
        /// </summary>
        public async Task<string> GenerateResponseAsync(string userQuery, List<KnowledgeBaseResult> kbResults, string? sessionContext = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userQuery))
                    return "Pertanyaan tidak valid. Silakan berikan pertanyaan yang jelas.";

                _logger.LogInformation("[OllamaService] Processing query: {0}", userQuery);

                if (kbResults == null || kbResults.Count == 0)
                {
                    _logger.LogWarning("[OllamaService] No KB results for query: {0}", userQuery);
                    return BuildNoResultsMessage(userQuery);
                }

                _logger.LogInformation("[OllamaService] Found {0} KB results (best: {1:P0}), context: {2}",
                    kbResults.Count, kbResults.Max(r => r.Score), sessionContext ?? "(none)");

                var intelligentPrompt = await _promptEngineering.BuildIntelligentPromptAsync(
                    userQuery, kbResults, sessionContext: sessionContext);

                var response = await CallOllamaApiAsync(intelligentPrompt);

                if (string.IsNullOrEmpty(response))
                    return "Maaf, terjadi kesalahan dalam memproses jawaban. Silakan coba lagi.";

                _logger.LogInformation("[OllamaService] Response: {0} chars", response.Length);
                return response;
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError("[OllamaService] HTTP error: {0}", httpEx, new object[] { httpEx.Message });
                return "Maaf, layanan AI (Ollama) saat ini tidak tersedia. Pastikan Ollama server berjalan di " + _baseUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError("[OllamaService] Error generating response: {0}", ex, new object[] { ex.Message });
                return "Maaf, terjadi kesalahan dalam memproses permintaan Anda.";
            }
        }

        // ─────────────────────────────────────────────────────────────
        // IOllamaService — GenerateSuggestionsAsync
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Generate 3 pertanyaan lanjutan yang relevan dan kontekstual menggunakan Ollama.
        /// </summary>
        public async Task<List<string>> GenerateSuggestionsAsync(string userQuery, string response)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userQuery) || string.IsNullOrWhiteSpace(response))
                    return GetDefaultSuggestions();

                var responseSummary = response.Length > 400
                    ? response[..400] + "..."
                    : response;

                var prompt = $"""
                    Konteks: pengguna bertanya tentang sistem JIFAS (ERP keuangan PT Jababeka).

                    Pertanyaan user: {userQuery}
                    Jawaban AI: {responseSummary}

                    Buatkan 3 pertanyaan lanjutan yang NATURAL, SPESIFIK tentang JIFAS, dan berguna bagi pengguna.
                    Syarat: singkat (maks 12 kata), Bahasa Indonesia, relevan dengan topik di atas.

                    Format output HANYA:
                    1. [pertanyaan]
                    2. [pertanyaan]
                    3. [pertanyaan]
                    """;

                var text = await CallOllamaApiAsync(prompt);
                var parsed = ExtractSuggestions(text);
                return parsed.Count > 0 ? parsed : GetDefaultSuggestions();
            }
            catch (Exception ex)
            {
                _logger.LogError("[OllamaService] Error generating suggestions: {0}", ex, new object[] { ex.Message });
                return GetDefaultSuggestions();
            }
        }

        // ─────────────────────────────────────────────────────────────
        // IOllamaService — IsInScopeAsync
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Scope check ringan via keyword — tanpa LLM call untuk hemat resource.
        /// Deteksi lebih dalam sudah ditangani OutOfScopeDetector.
        /// </summary>
        public Task<bool> IsInScopeAsync(string userQuery)
        {
            var outOfScope = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "cuaca", "weather", "berita", "news", "politik", "agama",
                "film", "musik", "lagu", "game", "resep", "masak",
                "crypto", "bitcoin", "investasi saham", "forex"
            };
            var q = userQuery?.ToLowerInvariant() ?? "";
            var isOut = outOfScope.Any(k => q.Contains(k));
            return Task.FromResult(!isOut);
        }

        // ─────────────────────────────────────────────────────────────
        // IOllamaService — CallOllamaApiAsync  (Ollama /api/chat)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Panggil Ollama /api/chat dengan system instruction JIFAS.
        /// </summary>
        public async Task<string> CallOllamaApiAsync(string prompt)
        {
            const int maxRetries = 2;
            Exception? lastEx = null;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await CallOllamaInternalAsync(prompt);
                }
                catch (TaskCanceledException ex)
                {
                    lastEx = ex;
                    _logger.LogWarning("[OllamaService] Timeout attempt {0}/{1}", attempt + 1, maxRetries);
                    await Task.Delay(2000);
                }
                catch (HttpRequestException ex)
                {
                    lastEx = ex;
                    _logger.LogWarning("[OllamaService] HTTP error attempt {0}/{1}: {2}", attempt + 1, maxRetries, ex.Message);
                    await Task.Delay(2000);
                }
            }

            throw lastEx ?? new Exception("Ollama API failed after retries");
        }

        // ─────────────────────────────────────────────────────────────
        // Private — Ollama API call
        // ─────────────────────────────────────────────────────────────

        private async Task<string> CallOllamaInternalAsync(string userPrompt)
        {
            var endpoint = $"{_baseUrl}/api/chat";

            // qwen3:8b supports /think tag — kita pakai /no_think untuk respons cepat dan clean
            var requestBody = new
            {
                model = _model,
                stream = false,
                options = new
                {
                    temperature = _temperature,
                    num_predict = _maxTokens,
                    top_p = 0.85,
                    top_k = 40,
                    repeat_penalty = 1.1
                },
                messages = new[]
                {
                    new { role = "system", content = BuildJifasSystemInstruction() },
                    new { role = "user",   content = userPrompt + "\n/no_think" }
                }
            };

            var json = JsonConvert.SerializeObject(requestBody, Formatting.None);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogDebug("[OllamaService] POST {0} | model={1}", endpoint, _model);

            var response = await _httpClient.PostAsync(endpoint, content);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                _logger.LogError($"[OllamaService] API error {(int)response.StatusCode}: {err}");
                throw new HttpRequestException($"Ollama API returned {response.StatusCode}: {err}");
            }

            var responseText = await response.Content.ReadAsStringAsync();
            return ParseOllamaResponse(responseText);
        }

        // ─────────────────────────────────────────────────────────────
        // Private — Parse Ollama response
        // ─────────────────────────────────────────────────────────────

        private string ParseOllamaResponse(string responseJson)
        {
            try
            {
                var jObj = JObject.Parse(responseJson);
                var text = jObj["message"]?["content"]?.ToString();

                if (!string.IsNullOrEmpty(text))
                {
                    // Bersihkan <think>...</think> block yang kadang dihasilkan qwen3
                    text = Regex.Replace(text, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase).Trim();
                    return text;
                }

                _logger.LogWarning("[OllamaService] Could not parse response: {0}",
                    responseJson.Length > 200 ? responseJson[..200] : responseJson);
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError("[OllamaService] Error parsing response: {0}", ex, new object[] { ex.Message });
                return string.Empty;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Private — System Instruction (JIFAS Persona Agent)
        // ─────────────────────────────────────────────────────────────

        private static string BuildJifasSystemInstruction() => """
            Kamu adalah JIFAS AI Assistant — AI Persona Agent resmi untuk sistem JIFAS (Jababeka Integrated Finance Accounting System) milik PT Jababeka Tbk dan seluruh anak perusahaannya.

            === IDENTITAS & PERSONA ===
            Namamu: JIFAS AI
            Peranmu: Expert JIFAS System Advisor & Business Process Consultant
            Karaktermu: Cerdas, profesional, jujur, helpful, dan bicara seperti rekan kerja senior yang paham sistem — bukan robot kaku.
            Bahasa: Bahasa Indonesia yang natural, hangat, dan mudah dipahami.

            === PENGETAHUAN MENDALAM TENTANG JIFAS ===

            JIFAS adalah sistem ERP keuangan terintegrasi yang dipakai semua unit bisnis Jababeka Group:
            - KIJ, GBC, MPK, JM, BW, TL, SPPK → jifas.jababeka.com
            - JI, ICTEL, NGE → jifasweb.jiinfra.com
            - BP, UP, TS → jifas-bp.bekasipower.co.id
            - KIK → jifas.kik.com
            Login: username Windows TANPA @jababeka.com, password = password Windows domain.

            MODUL UTAMA JIFAS:

            1. MASTER DATA — Company, Employee, Vendor, Division/Dept, COA, Account Period, List COA, Report Setup, Roles & Auth, Budget.
               - Roles: WMTR (IT admin), USER (umum), USRL (bisa pilih dept di PUM).

            2. INVOICE — Pengajuan tagihan/biaya.
               Alur: Create → Finance Approval → Head Approval → Tax → Posting.
               Status: Draft → Submitted → Finance Checking → Head Approval → Approved → Posted.
               Tombol: Save, Submit, Approve, Reject, Post, Void.

            3. PUM (Perjalanan Uang Muka / Perjalanan Dinas)
               Alur: Pengajuan → Finance Approval → Head Approval → Distribusi → Realisasi → Settlement.
               Settlement: laporkan realisasi vs uang muka; lebih → kembalikan; kurang → perusahaan bayar.
               Status: Draft → Submitted → Approved → Distributed → Realization → Settled.

            4. RECEIVING — Penerimaan barang/jasa.
               - Receive of Sales, Receive Tax (verifikasi NPWP + alamat WP lengkap).
               - RV (Receive Voucher) = nomor dokumen penerimaan.
               - Status: Need Finance Checking → Approved → Posted.

            5. PAYMENT — Pembayaran Invoice & PUM.
               Alur: Finance Approval → Head Approval → Posting.
               Metode: Transfer, BG (Bank Garansi), Cek, Giro.
               Sub-menu: Payment Invoice, Payment PUM, PaymentTax, List BG.

            6. ACCOUNTING — GL (jurnal), AP (hutang), AR (piutang), Bulk Posting, Inquiry AP/AR/CB/PUM, Acc Period, Konsolidasi.

            7. REPORT — Budget Card/Committed/Realization/Payment/Receive, Daily Cashflow, Cashbank Detail/Recap, Deposito Aktif, Inquiry AP/AR/CB/PUM, Realisasi PUM, Saldo Buku Bank, Commited Realization.

            8. OVER BUDGET — Terjadi saat transaksi melebihi batas anggaran; butuh approval khusus atau revisi budget.

            ALUR APPROVAL UMUM:
            Creator (submit) → Finance (verifikasi) → Head/Director (final) → Tax (jika ada) → Posting (ke GL).

            ATURAN PENTING:
            - Dokumen sudah di-posting → TIDAK bisa diedit.
            - Periode akuntansi sudah ditutup → TIDAK bisa input transaksi.
            - Reject = dikembalikan ke pembuat; Void = pembatalan dokumen final.

            === CARA MENJAWAB (WAJIB) ===

            1. GROUNDED IN KB: Jawab dari Knowledge Base yang diberikan sebagai sumber utama.
            2. HONEST: Jika KB tidak punya info → katakan jujur dan sarankan IT Help Desk: it@jababeka.com.
            3. NO HALLUCINATION: Jangan mengarang langkah, menu, atau fitur yang tidak ada di KB.
            4. CONTEXT AWARE: Prioritaskan jawaban yang relevan dengan halaman/modul yang sedang dibuka user.
            5. ACTIONABLE: Berikan langkah konkret, bukan jawaban abstrak.
            6. NATURAL: Seperti rekan senior yang paham sistem, bukan membaca manual.
            7. STRUCTURED: Bullet/numbering untuk langkah-langkah; **bold** untuk istilah penting.
            8. CONCISE: Langsung ke inti, tidak perlu preamble panjang atau kesimpulan berulang.

            Eskalasi: IT Help Desk → it@jababeka.com | Permission issues → admin sistem atau atasan.
            """;

        // ─────────────────────────────────────────────────────────────
        // Private — Helpers
        // ─────────────────────────────────────────────────────────────

        private string BuildNoResultsMessage(string query) =>
            $"Maaf, saya tidak menemukan informasi tentang \"{query}\" di Knowledge Base JIFAS. " +
            "Coba gunakan kata kunci yang lebih spesifik, atau hubungi IT Help Desk: it@jababeka.com.";

        private List<string> ExtractSuggestions(string text)
        {
            var suggestions = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return GetDefaultSuggestions();

            try
            {
                var lines = text.Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var match = Regex.Match(line.Trim(), @"^\d+[.)]\s*(.+)$");
                    if (match.Success)
                    {
                        var s = match.Groups[1].Value.Trim();
                        if (s.Length >= 5 && s.Length <= 200)
                            suggestions.Add(s);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[OllamaService] Error parsing suggestions: {0}", ex.Message);
            }

            return suggestions.Count > 0 ? suggestions.Take(3).ToList() : GetDefaultSuggestions();
        }

        private static List<string> GetDefaultSuggestions() =>
        [
            "Bagaimana cara membuat dokumen baru di JIFAS?",
            "Apa saja status dalam workflow approval JIFAS?",
            "Bagaimana cara melihat riwayat transaksi di JIFAS?"
        ];

        // OllamaService does not record metrics; SetCallContext is a no-op here.
        public void SetCallContext(string? userId, string? sessionId, string? activeModule, string callType = "chat") { }
    }
}
