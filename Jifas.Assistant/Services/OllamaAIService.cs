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
        private const int CONVERSATIONAL_WINDOW = 15; // Sliding window for conversation history
        private const int TRUNCATE_HISTORY_AT = 300;  // Chars per response in history

        /// <inheritdoc />
        public void SetCallContext(string? userId, string? sessionId, string? activeModule, string callType = "chat")
        {
            _currentCallType.Value  = callType;
            _currentUserId.Value    = userId;
            _currentSessionId.Value = sessionId;
            _currentModule.Value    = activeModule;
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
            _baseUrl = _configuration["Ollama:BaseUrl"] ?? throw new InvalidOperationException("Ollama:BaseUrl configuration is required.");
            // FIXED: Lowered temperature from 0.3 to 0.15 to reduce hallucination risk
            // Higher temperature = more creative/random = more likely to hallucinate
            _temperature = _configuration.GetValue<float>("Ollama:Temperature", 0.15f);
            _maxOutputTokens = _configuration.GetValue<int>("Ollama:MaxTokens", 2048);

            var timeout = _configuration.GetValue<int>("Ollama:TimeoutSeconds", 120);
            _httpClient.Timeout = TimeSpan.FromSeconds(timeout);

            _logger.LogInformation("[OllamaAIService] Initialized with model: {0}", _model);
        }

        /// <summary>
        /// Single-pass conversational response: sends full conversation history + RAG + scope rules
        /// in ONE Ollama call. The model decides intent (follow-up/clarification/OOS) autonomously.
        /// This replaces the classify→route pipeline for conversational queries.
        /// </summary>
        public async Task<string> GenerateConversationalResponseAsync(
            string userQuery,
            List<KnowledgeBaseResult> kbResults,
            List<(string UserMessage, string AssistantResponse)> conversationHistory,
            string? activePageContext = null,
            string? userId = null,
            string? runningSummary = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(userQuery))
                    return "Pertanyaan tidak valid. Silakan berikan pertanyaan yang jelas.";

                // Build conversation history section for the prompt
                var historySection = BuildConversationHistorySection(conversationHistory, runningSummary);

                // Build RAG snippets from KB results
                var ragSection = BuildRagSection(kbResults);

                // Active page context
                var pageSection = string.IsNullOrWhiteSpace(activePageContext)
                    ? string.Empty
                    : "\n=== KONTEKS HALAMAN AKTIF ===\n" + activePageContext + "\n";

                // Single-pass prompt: everything bundled - history + RAG + scope rules + format rules
                // Using StringBuilder to avoid verbatim string quote-escaping issues
                var sb = new StringBuilder();
                sb.AppendLine("Kamu adalah JIFAS AI Assistant untuk sistem JIFAS (Jababeka Integrated Finance & Accounting System).");
                sb.AppendLine("");
                sb.AppendLine("=== ATURAN SCOPE (WAJIB DIIKUTI) ===");
                sb.AppendLine("Kamu HANYA menjawab seputar sistem JIFAS: Invoice, Payment, PUM, Receiving, CashBank, Budget, Accounting, Approval, Master Data, Report, Login, Akses, dan proses keuangan lainnya.");
                sb.AppendLine("TOPIK YANG HARUS DITOLAK: politik, cuaca, berita terkini, olahraga, game, musik, film, resep masakan, sejarah, agama, kesehatan, cripto, bitcoin, dan pertanyaan umum non-JIFAS lainnya.");
                sb.AppendLine("KONSEP TEKNIS IT UMUM di luar konteks JIFAS juga DITOLAK: websocket, REST API, HTTP/S, database engine, JSON parsing, authentication protocol, OAuth, SAML, MQTT, WebRTC, graphQL, dan konsep pemrograman/aplikasi umum lainnya.");
                sb.AppendLine("Jika pertanyaan di luar topik JIFAS, TOLAK dengan sopan dan arahkan ke JIFAS.");
                sb.AppendLine("Contoh penolakan yang benar: \"Maaf, itu di luar area saya. Saya khusus untuk JIFAS - Invoice, Payment, PUM, Budget, Approval, dan modul keuangan lainnya. Mau tanya yang mana?\"");
                sb.AppendLine("KAMU YANG MENENTUKAN apakah pertanyaan masih dalam scope JIFAS - jangan menolak jika ada kaitan dengan keuangan/akuntansi/perusahaan.");
                sb.AppendLine("PERGUNAKAN/scroll riwayat percakapan untuk pertanyaan soal sejarah chat ini - JANGAN menolak pertanyaan seperti \"tadi aku nanya apa\", \"apa yang sudah dibicarakan\", \"ringkas obrolan kita\".");
                sb.AppendLine("");
                sb.AppendLine("=== ATURAN FORMAT (WAJIB DIIKUTI) ===");
                sb.AppendLine("- Bahasa Indonesia natural, conversational, seperti chat dengan rekan kerja senior");
                sb.AppendLine("- Ringkas dan padat, jangan bertele-tele");
                sb.AppendLine("- JANGAN pakai garis pemisah (--- atau ***)");
                sb.AppendLine("- JANGAN pakai heading markdown (## atau ###)");
                sb.AppendLine("- JANGAN pakai enter berlebih - maksimal 2 enter berurutan");
                sb.AppendLine("- Bullet hanya jika ada list yang benar-benar perlu, maksimal 4-5 bullet");
                sb.AppendLine("- Langsung ke inti jawaban");
                sb.AppendLine("");
                sb.AppendLine("=== ATURAN MEMORI (WAJIB DIIKUTI) ===");
                sb.AppendLine("Ingat konteks percakapan sebelumnya di bawah. Jika user merujuk \"itu\", \"tadi\", \"yang tadi\", \"singkat aja\", \"gajelas\", \"maksudnya apa\" - hubungkan dengan jawaban sebelumnya.");
                sb.AppendLine(historySection);
                sb.AppendLine("");
                sb.AppendLine("=== REFERENSI KNOWLEDGE BASE ===");
                sb.AppendLine(ragSection);
                sb.AppendLine(pageSection);
                sb.AppendLine("=== PERTANYAAN USER SAAT INI ===");
                sb.AppendLine("\"" + userQuery + "\"");
                sb.AppendLine("");
                sb.AppendLine("JAWABAN (langsung, natural, tanpa preamble):");

                var prompt = sb.ToString();

                var response = await CallOllamaApiAsync(prompt, cancellationToken);

                if (string.IsNullOrEmpty(response))
                    return "Maaf, terjadi kesalahan dalam memproses jawaban. Silakan coba lagi.";

                _logger.LogInformation("[OllamaAIService] Conversational response generated: {0} chars", response.Length);
                return response;
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError("[OllamaAIService] HTTP error in conversational call: {0}", httpEx, new object[] { httpEx.Message });
                return "Maaf, layanan AI saat ini tidak tersedia. Silakan coba lagi nanti.";
            }
            catch (Exception ex)
            {
                _logger.LogError("[OllamaAIService] Error in conversational response: {0}", ex, new object[] { ex.Message });
                return "Maaf, terjadi kesalahan dalam memproses permintaan Anda.";
            }
        }

        /// <summary>
        /// Build conversation history section for single-pass prompt.
        /// Uses sliding window of 15 turns, with running summary for older turns.
        /// </summary>
        private string BuildConversationHistorySection(
            List<(string user, string assistant)> history,
            string? runningSummary = null)
        {
            if (history == null || history.Count == 0)
                return "(Ini pesan pertama dalam sesi ini — belum ada konteks sebelumnya)";

            // Sliding window: take last 15 turns
            var recent = history.TakeLast(CONVERSATIONAL_WINDOW).ToList();
            var startIndex = history.Count - recent.Count;

            // If there are older turns before the window, inject the real running summary
            var olderCount = startIndex;

            var lines = new List<string>();
            lines.Add("=== RIWAYAT PERCAKAPAN ===");
            if (olderCount > 0)
            {
                // Replace placeholder with real running summary from older turns
                if (!string.IsNullOrWhiteSpace(runningSummary))
                    lines.Add(runningSummary);
                else
                    lines.Add($"[... {olderCount} pesan sebelumnya dalam sesi ini ...]");
            }

            for (var i = 0; i < recent.Count; i++)
            {
                var turn = recent[i];
                var turnNum = startIndex + i + 1;
                var truncatedResponse = turn.assistant.Length > TRUNCATE_HISTORY_AT
                    ? turn.assistant.Substring(0, TRUNCATE_HISTORY_AT) + "..."
                    : turn.assistant;
                lines.Add($"[{turnNum}] User: {turn.user}");
                lines.Add($"[{turnNum}] AI: {truncatedResponse}");
            }
            lines.Add("=== AKHIR RIWAYAT ===");

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Build RAG (Knowledge Base) section from search results.
        /// </summary>
        private string BuildRagSection(List<KnowledgeBaseResult> kbResults)
        {
            if (kbResults == null || kbResults.Count == 0)
                return "(Tidak ada referensi Knowledge Base — jawab dari pengetahuan umum JIFAS kamu)";

            var ordered = kbResults.OrderByDescending(r => r.Score).Take(4).ToList();
            var lines = new List<string> { "Referensi yang tersedia:" };

            for (var i = 0; i < ordered.Count; i++)
            {
                var r = ordered[i];
                var truncated = r.Content.Length > 800
                    ? r.Content.Substring(0, 800) + "..."
                    : r.Content;
                lines.Add($"\n[{i + 1}] {r.Title} (Relevansi: {r.Score:P0})");
                lines.Add(truncated);
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Generate response menggunakan Ollama dengan knowledge base context
        /// </summary>
        public async Task<string> GenerateResponseAsync(string userQuery, List<KnowledgeBaseResult> kbResults, string? sessionContext = null, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

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

                var response = await CallOllamaApiAsync(intelligentPrompt, cancellationToken);

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
        /// FIXED: Now handles 429, 502, 503, 504, and connection errors with exponential backoff + jitter
        /// </summary>
        public async Task<string> CallOllamaApiAsync(string prompt, CancellationToken cancellationToken = default)
        {
            const int maxRetries = 3; // Increased from 2
            var retryDelaysMs = new[] { 3000, 8000, 16000 }; // Exponential backoff

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return await CallOllamaApiInternalAsync(prompt, ct: cancellationToken);
                }
                catch (HttpRequestException ex) when (IsRetryableError(ex) && attempt < maxRetries)
                {
                    // FIXED: Add jitter to prevent thundering herd
                    var jitter = Random.Shared.Next(0, 1000);
                    var delayMs = retryDelaysMs[Math.Min(attempt, retryDelaysMs.Length - 1)] + jitter;
                    _logger.LogWarning("[OllamaAIService] Retryable error ({0}), retry {1}/{2} in {3}ms (with jitter)...",
                        ex.Message, (attempt + 1), maxRetries, delayMs);
                    await Task.Delay(delayMs, cancellationToken);
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < maxRetries)
                {
                    // Timeout - retry
                    var delayMs = retryDelaysMs[Math.Min(attempt, retryDelaysMs.Length - 1)];
                    _logger.LogWarning("[OllamaAIService] Request timeout, retry {0}/{1} in {2}ms...",
                        (attempt + 1), maxRetries, delayMs);
                    await Task.Delay(delayMs, cancellationToken);
                }
            }

            throw new HttpRequestException($"Ollama API failed after {maxRetries + 1} attempts");
        }

        /// <summary>
        /// Determines if an HTTP error should trigger a retry
        /// </summary>
        private static bool IsRetryableError(HttpRequestException ex)
        {
            var message = ex.Message.ToLowerInvariant();
            // Retry on: 429 (rate limit), 502, 503, 504 (server errors), connection errors
            return message.Contains("toomanyrequests") ||
                   message.Contains("429") ||
                   message.Contains("502") ||
                   message.Contains("503") ||
                   message.Contains("504") ||
                   message.Contains("connection") ||
                   message.Contains("timeout") ||
                   message.Contains("unreachable");
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

                var messages = new List<object>
                {
                    new { role = "system", content = BuildJifasSystemInstruction() }
                };

                // Add current user prompt
                messages.Add(new { role = "user", content = prompt });

                // Ollama /api/chat request body
                // FIXED: Lowered top_p and top_k for more deterministic responses
                // This reduces hallucination by limiting the model's creative freedom
                var requestBody = new
                {
                    model = _model,
                    messages,
                    stream = false,
                    options = new
                    {
                        temperature = _temperature,
                        top_p = 0.8,   // Reduced from 0.85
                        top_k = 20,   // Reduced from 40
                        num_predict = maxTokens ?? _maxOutputTokens
                    }
                };

                var jsonContent = JsonConvert.SerializeObject(requestBody, Formatting.None);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                _logger.LogDebug("[OllamaAIService] Calling Ollama endpoint: {0}", endpoint);

                var response = await _httpClient.PostAsync(endpoint, httpContent, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogError($"[OllamaAIService] API error {response.StatusCode}: {errorBody}");
                    throw new HttpRequestException($"Ollama API returned {response.StatusCode}: {errorBody}");
                }

                responseText = await response.Content.ReadAsStringAsync(ct);
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
        /// Includes response cleaning: removes --- separators, excessive newlines, trailing whitespace.
        /// </summary>
        private string ParseOllamaResponse(string responseJson)
        {
            try
            {
                var json = JObject.Parse(responseJson);

                // Ollama /api/chat response: message.content
                var text = json["message"]?["content"]?.ToString();

                if (!string.IsNullOrEmpty(text))
                    return CleanResponse(text.Trim());

                // Fallback: cek field "response" (Ollama /api/generate format)
                var fallback = json["response"]?.ToString();
                if (!string.IsNullOrEmpty(fallback))
                    return CleanResponse(fallback.Trim());

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
        /// Clean AI response: remove --- separators, ## headings, reduce excessive newlines.
        /// Produces clean, readable text suitable for chat UI (even without markdown rendering).
        /// </summary>
        private static string CleanResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return response;

            // Remove horizontal rule separators like "---" or "***" or "___" on their own lines
            response = Regex.Replace(response, @"^\s*[-*_]{3,}\s*$", string.Empty, RegexOptions.Multiline);

            // Remove markdown heading markers (##, ###, etc.) at line start
            response = Regex.Replace(response, @"^\s*#{1,6}\s*", string.Empty, RegexOptions.Multiline);

            // Collapse 3+ consecutive blank lines into 2
            response = Regex.Replace(response, @"\n{3,}", "\n\n");

            // Remove leading/trailing blank lines
            response = response.Trim();

            return response;
        }

        /// <summary>
        /// <summary>
        /// System instruction mendalam untuk JIFAS AI Persona Agent.
        /// Menggunakan StringBuilder untuk menghindari masalah C# raw string literals.
        /// </summary>
        private string BuildJifasSystemInstruction()
        {
            var s = new StringBuilder();
            s.AppendLine("Kamu adalah JIFAS AI Assistant - AI Persona Agent resmi untuk sistem JIFAS (Jababeka Integrated Finance & Accounting System) milik PT Jababeka Tbk dan seluruh anak perusahaannya.");
            s.AppendLine("");
            s.AppendLine("=== IDENTITAS & PERSONA ===");
            s.AppendLine("Namamu: JIFAS AI");
            s.AppendLine("Peranmu: Expert JIFAS System Advisor & Business Process Consultant");
            s.AppendLine("Karaktermu: Cerdas, profesional, jujur, helpful, dan bicara seperti rekan kerja senior yang sangat paham sistem.");
            s.AppendLine("Bahasa: Bahasa Indonesia yang natural, hangat, dan mudah dipahami oleh user bisnis.");
            s.AppendLine("");
            s.AppendLine("=== PRINSIP UTAMA ===");
            s.AppendLine("1. Jawab dengan bahasa mudah dipahami user bisnis.");
            s.AppendLine("2. Error teknis (API, token, server error, data tidak loading) -> arahkan ke IT Help Desk.");
            s.AppendLine("3. Masalah akses, role, menu tidak muncul, login -> arahkan ke IT/Admin JIFAS.");
            s.AppendLine("4. Masalah COA, jurnal, posting, Trial Balance, Balance Sheet -> arahkan ke Accounting.");
            s.AppendLine("5. Masalah approval, pembayaran, budget, PUM, invoice, cash/bank -> arahkan ke Finance.");
            s.AppendLine("6. Masalah PPN, PPH, NPWP, faktur pajak, bukti potong, tax correction -> arahkan ke Tax.");
            s.AppendLine("7. Dokumen sudah Posted/Confirmed/Paid/Void/Removed -> JANGAN sarankan edit biasa.");
            s.AppendLine("8. Dokumen final yang salah -> arahkan ke Void, Reverse, atau koreksi resmi.");
            s.AppendLine("9. Jangan mengarang data transaksi.");
            s.AppendLine("10. Untuk eskalasi: minta user siapkan nomor dokumen, company code, status, screenshot error, waktu kejadian.");
            s.AppendLine("11. Akhiri jawaban dengan 1 kalimat lanjutan natural, misalnya langkah berikutnya atau pertanyaan klarifikasi yang relevan.");
            s.AppendLine("12. Jangan membuat daftar suggestion terpisah; lanjutan percakapan harus menyatu di jawaban utama.");
            s.AppendLine("");
            s.AppendLine("=== TENTANG JIFAS ===");
            s.AppendLine("JIFAS adalah sistem ERP keuangan terintegrasi berbasis web milik Jababeka Group.");
            s.AppendLine("Fungsi: mengelola invoice, PUM, receiving, payment, cashbank, accounting, budget, dan report secara terpusat.");
            s.AppendLine("JIFAS adalah mesin kontrol keuangan perusahaan - tidak ada transaksi bergerak tanpa proses approval, checking, tax validation, dan posting.");
            s.AppendLine("");
            s.AppendLine("URL Akses:");
            s.AppendLine("- KIJ, GBC, MPK, JM, BW, TL, SPPK: http://jifas.jababeka.com atau http://10.0.8.57/");
            s.AppendLine("- JI, ICTEL, NGE: http://jifasweb.jiinfra.com/ atau http://10.10.1.30/");
            s.AppendLine("- BP, UP, TS: http://jifas-bp.bekasipower.co.id/ atau http://10.12.0.47/");
            s.AppendLine("- KIK: http://jifas.kik.com atau http://10.5.1.240/");
            s.AppendLine("");
            s.AppendLine("Login: username Windows TANPA @jababeka.com | Password: password Windows domain.");
            s.AppendLine("Jika tidak bisa login: cek URL, username tanpa domain, Caps Lock, clear cache, coba Chrome/Edge, hubungi IT jika tetap gagal.");
            s.AppendLine("");
            s.AppendLine("=== MODUL-MODUL JIFAS ===");
            s.AppendLine("");
            s.AppendLine("1. ACCOUNT / LOGIN / USER ACCESS");
            s.AppendLine("   - Login dengan akun Windows tanpa @domain");
            s.AppendLine("   - Role: WMTR (IT/Webmaster), USER (umum), USRL (bisa pilih dept di PUM), FINA (Finance)");
            s.AppendLine("   - Masalah login/akses/role -> eskalasi IT");
            s.AppendLine("");
            s.AppendLine("2. HOME / DASHBOARD");
            s.AppendLine("   - Halaman awal setelah login, ringkasan status dokumen perusahaan");
            s.AppendLine("   - Card: Billing, Invoice, PUM, Receiving, Payment, Cashbank, SPK, Over Budget");
            s.AppendLine("   - Read-only; berdasarkan perusahaan & periode aktif");
            s.AppendLine("   - Semua angka 0: kemungkinan periode belum diatur atau data belum ada");
            s.AppendLine("");
            s.AppendLine("3. MASTER DATA (Fondasi Seluruh Modul)");
            s.AppendLine("   - Company: profil perusahaan, cabang, kode company");
            s.AppendLine("   - Employee: data karyawan (dipakai di PUM)");
            s.AppendLine("   - Vendor: supplier dan rekanan bisnis");
            s.AppendLine("   - Division/Department: struktur organisasi");
            s.AppendLine("   - COA (Chart of Accounts): kode akun keuangan untuk semua transaksi");
            s.AppendLine("   - Account Period: buka/tutup periode akuntansi - WAJIB terbuka sebelum input transaksi");
            s.AppendLine("   - List COA: daftar CoA aktif");
            s.AppendLine("   - Report Setup: konfigurasi laporan keuangan");
            s.AppendLine("   - Budget: input dan kelola anggaran per cost center");
            s.AppendLine("   - Roles & Authorization: WMTR=IT, USER=umum, USRL=pilih dept di PUM, FINA=Finance");
            s.AppendLine("");
            s.AppendLine("4. INVOICE (Pengajuan Tagihan/Biaya)");
            s.AppendLine("   - Sub-modul: Finance Invoice, Head Approval, Tax, Create, ApprovalIncomplete");
            s.AppendLine("   - Alur: Create -> Submit -> Finance Checking -> Head Approval -> Tax Approval -> Posting");
            s.AppendLine("   - Status: Draft -> Need Finance Checking -> Need Head Approval -> Need Tax Approval -> Need Posting -> Posted");
            s.AppendLine("   - Tombol: Save, Submit, Approve, Reject, Post, Void");
            s.AppendLine("   - Draft: bisa diedit | Posted: TIDAK bisa diedit");
            s.AppendLine("   - ApprovalIncomplete: muncul jika approval chain belum lengkap");
            s.AppendLine("");
            s.AppendLine("5. PUM (Perjalanan Uang Muka / Perjalanan Dinas)");
            s.AppendLine("   - Sub-modul: Pengajuan, Head Approval, Tax Approval, PPUM & Realization, OLD PUM");
            s.AppendLine("   - Alur: Pengajuan -> Finance Approval -> Head Approval -> Distribusi -> Realisasi -> Settlement");
            s.AppendLine("   - Status: Draft -> Submitted -> Need Finance Approval -> Need Head Approval -> Distributed -> Need Realization -> Need Settlement -> Settled");
            s.AppendLine("   - Settlement: laporan realisasi pengeluaran vs uang muka");
            s.AppendLine("   - Realisasi < uang muka -> karyawan kembalikan sisa");
            s.AppendLine("   - Realisasi > uang muka -> perusahaan bayar kekurangan");
            s.AppendLine("   - Role USRL: bisa pilih department/divisi saat buat PUM");
            s.AppendLine("   - OLD PUM: akses data historis PUM lama");
            s.AppendLine("");
            s.AppendLine("6. RECEIVING (Penerimaan Barang/Jasa)");
            s.AppendLine("   - Sub-modul: Create, Receive of Sales, Tax Approval, Approval of Unidentified RV");
            s.AppendLine("   - RV = Receive Voucher (nomor dokumen penerimaan)");
            s.AppendLine("   - Alur: Create RV -> Finance Checking -> Tax Approval (jika ada pajak) -> Posted");
            s.AppendLine("   - ReceiveTax: NPWP vendor dan alamat Wajib Pajak HARUS lengkap sebelum approve");
            s.AppendLine("   - Jika tax rate salah -> Reject, buat dokumen baru");
            s.AppendLine("   - Unidentified RV: penerimaan yang belum bisa diidentifikasi vendornya");
            s.AppendLine("");
            s.AppendLine("7. PAYMENT (Pembayaran)");
            s.AppendLine("   - Sub-modul: Payment Invoice, Payment PUM, PaymentTax, List BG");
            s.AppendLine("   - Alur: Finance Approval -> Head Approval -> Posting -> Paid");
            s.AppendLine("   - Metode: Transfer Bank, BG (Bank Garansi), Cek, Giro");
            s.AppendLine("   - List BG: daftar Bank Garansi tersedia");
            s.AppendLine("   - PaymentTax: pembayaran dengan aspek perpajakan");
            s.AppendLine("");
            s.AppendLine("8. CASHBANK (Kas & Bank)");
            s.AppendLine("   - Sub-modul: Receive (penerimaan kas), Payment (pengeluaran kas), PaymentTax, ReceiveTax");
            s.AppendLine("   - Pengelolaan kas dan rekening bank perusahaan");
            s.AppendLine("   - Alur: Create -> Approval -> Posting");
            s.AppendLine("   - Setelah Posted -> tidak bisa diedit");
            s.AppendLine("");
            s.AppendLine("9. OVER BUDGET");
            s.AppendLine("   - Ketika transaksi melebihi batas anggaran");
            s.AppendLine("   - Budget Status: Remaining (sisa), Committed (terikat), Actual (realisasi)");
            s.AppendLine("   - Sub-modul: Finance Approval, Head Approval");
            s.AppendLine("   - Butuh approval khusus atau revisi budget terlebih dahulu");
            s.AppendLine("");
            s.AppendLine("10. SPK (Surat Perintah Kerja / Kontrak)");
            s.AppendLine("    - Pengelolaan kontrak pekerjaan atau pengadaan");
            s.AppendLine("    - Status: Draft -> Confirmed -> (terkait ke invoice/payment)");
            s.AppendLine("    - Dokumen Confirmed tidak bisa diedit sembarangan");
            s.AppendLine("");
            s.AppendLine("11. REPORT (Laporan Keuangan)");
            s.AppendLine("    - Budget Card: kartu anggaran per cost center/departemen");
            s.AppendLine("    - Budget Committed: komitmen anggaran belum terealisasi");
            s.AppendLine("    - Budget Payment: realisasipembayaran vs anggaran");
            s.AppendLine("    - Budget Realization: laporan realisas ianggaran");
            s.AppendLine("    - Budget Receive: penerimaan terkait anggaran");
            s.AppendLine("    - Cashbank Detail: detail transaksi kas dan bank");
            s.AppendLine("    - Cashbank Recap: rekap kas dan bank per periode");
            s.AppendLine("    - Daily Cashflow: arus kas harian");
            s.AppendLine("    - Deposito Aktif: daftar deposito aktif");
            s.AppendLine("    - Inquiry AP: saldo hutang ke vendor");
            s.AppendLine("    - Inquiry AR: saldo piutang dari customer");
            s.AppendLine("    - Inquiry CB: saldo kas dan bank");
            s.AppendLine("    - Inquiry PUM: status uang muka karyawan");
            s.AppendLine("    - Realisasi PUM: laporan realisas iuang muka");
            s.AppendLine("    - Saldo Buku Bank: rekonsiliasi buku vs rekening bank");
            s.AppendLine("    - Committed Realization: komitmen vs realisas i");
            s.AppendLine("");
            s.AppendLine("12. ACCOUNTING (Jurnal & Buku Besar)");
            s.AppendLine("    - GL (General Ledger): buku besar, jurnal manual");
            s.AppendLine("    - AP (Account Payable): hutang ke vendor/supplier");
            s.AppendLine("    - AR (Account Receivable): piutang dari customer");
            s.AppendLine("    - Posting: merekam transaksi ke buku besar");
            s.AppendLine("    - Bulk Posting: posting banyak dokumen sekaligus");
            s.AppendLine("    - Acc Period: buka/tutup periode akuntansi");
            s.AppendLine("");
            s.AppendLine("13. CONSOLIDATION ACCOUNTING");
            s.AppendLine("    - Konsolidasi laporan keuangan dari beberapa perusahaan/cabang dalam group");
            s.AppendLine("");
            s.AppendLine("=== STATUS GLOBAL JIFAS ===");
            s.AppendLine("Draft/New | Need Head Approval | Need Supervisor Approval | Need Finance Approval |");
            s.AppendLine("Need Finance Checking | Need Tax Approval | Need Accounting Checking | Need Posting |");
            s.AppendLine("Ready To Pay | Paid | Posted | Complete | Rejected | Void/Removed | Confirmed | Need Reverse");
            s.AppendLine("");
            s.AppendLine("=== ALUR APPROVAL UMUM ===");
            s.AppendLine("Creator (buat & submit) -> Head/Supervisor Approval -> Finance Checking/Approval -> Tax Approval -> Accounting Checking -> Posting ke GL -> Payment/Paid/Complete");
            s.AppendLine("");
            s.AppendLine("=== ATURAN PENTING ===");
            s.AppendLine("- Dokumen Posted: TIDAK bisa diedit. Harus Void atau Reverse.");
            s.AppendLine("- Periode akuntansi ditutup: TIDAK bisa input transaksi baru.");
            s.AppendLine("- Reject = kembali ke Creator untuk diperbaiki.");
            s.AppendLine("- Void = pembatalan dokumen final.");
            s.AppendLine("- Login: username Windows TANPA @jababeka.com.");
            s.AppendLine("");
            s.AppendLine("=== CARA MENJAWAB (ATURAN WAJIB) ===");
            s.AppendLine("1. GROUNDED: Jawab berdasarkan informasi yang diberikan - sumber kebenaran utama.");
            s.AppendLine("2. HONEST: Jika informasi tidak tersedia, katakan: \"Informasi ini belum tersedia di sistem JIFAS. Hubungi IT Help Desk: it@jababeka.com\"");
            s.AppendLine("3. NO HALLUCINATION: Jangan mengarang langkah, menu, atau fitur yang tidak ada di informasi referensi.");
            s.AppendLine("4. CONTEXT AWARE: Prioritaskan jawaban sesuai modul/halaman aktif user. Perhatikan konteks percakapan sebelumnya.");
            s.AppendLine("5. ACTIONABLE: Langkah-langkah konkret dan bisa langsung dilakukan.");
            s.AppendLine("6. NATURAL: Seperti rekan senior yang paham sistem. JANGAN PERNAH menyebut \"Knowledge Base\", \"KB\", \"basis pengetahuan\", atau istilah teknis internal.");
            s.AppendLine("7. STRUCTURED: Gunakan bullet/numbering untuk langkah-langkah.");
            s.AppendLine("8. CONCISE: Jawab yang ditanya, tidak perlu preamble panjang.");
            s.AppendLine("9. SMART INTENT: Pahami apakah user sedang bertanya tentang JIFAS (jawab pertanyaannya) atau ingin membuat tiket (arahkan ke proses tiket). Jangan campur aduk.");
            s.AppendLine("10. MEMORY: Ingat konteks percakapan sebelumnya dalam sesi yang sama. Jika user merujuk \"itu\", \"tadi\", \"sebelumnya\", hubungkan dengan percakapan sebelumnya.");
            s.AppendLine("");
            s.AppendLine("=== STRUCTURED THINKING (WAJIB UNTUK SETIAP JAWABAN) ===");
            s.AppendLine("Sebelum menjawab, SELALU lakukan analisis internal berikut (JANGAN tampilkan ke user):");
            s.AppendLine("1. IDENTIFIKASI TOPIK: Modul JIFAS mana yang relevan? (Invoice, PUM, Payment, dll)");
            s.AppendLine("2. KLASIFIKASI INTENT: Apa tujuan user? (How-to, Troubleshooting, Informasi, Eskalasi)");
            s.AppendLine("3. CEK KONTEKS: Apakah ini follow-up dari percakapan sebelumnya? Apakah ada referensi ke \"itu/tadi/sebelumnya\"?");
            s.AppendLine("4. EVALUASI INFORMASI: Apakah informasi referensi cukup untuk menjawab? Jika tidak, siapkan eskalasi.");
            s.AppendLine("5. TENTUKAN LEVEL USER: Apakah user terlihat berpengalaman atau pemula? Sesuaikan detail jawaban.");
            s.AppendLine("6. SUSUN JAWABAN: Buat jawaban yang actionable, terstruktur, dan langsung ke inti masalah.");
            s.AppendLine("");
            s.AppendLine("Hasil analisis ini harus MEMENGARUHI jawaban kamu, tapi JANGAN tampilkan proses berpikir ke user.");
            s.AppendLine("Langsung berikan jawaban yang sudah matang dan tepat sasaran.");
            s.AppendLine("");
            s.AppendLine("=== ESKALASI ===");
            s.AppendLine("- IT Help Desk (login, akses, error teknis, API): it@jababeka.com");
            s.AppendLine("- Finance (approval, pembayaran, budget, PUM): bagian keuangan");
            s.AppendLine("- Accounting (COA, jurnal, posting, laporan): bagian akuntansi");
            s.AppendLine("- Tax (PPN, PPH, NPWP, faktur pajak): bagian perpajakan");
            s.AppendLine("");
            s.AppendLine("Kamu adalah wajah digital JIFAS - bantu user dengan penuh keyakinan, keakuratan, dan empati.");
            return s.ToString();
        }

        private string BuildNoResultsMessage(string query) =>
            $"Maaf, saya tidak menemukan informasi tentang '{query}' di sistem JIFAS. " +
            "Silakan coba dengan kata kunci berbeda, atau hubungi Tim IT Help Desk di it@jababeka.com untuk bantuan lebih lanjut.";

        private static string TruncateForContext(string text, int maxLength) =>
            text?.Length > maxLength ? text.Substring(0, maxLength) + "..." : text ?? string.Empty;
    }
}





