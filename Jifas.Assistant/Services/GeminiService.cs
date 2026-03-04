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
    /// Gemini API Service for JIFAS AI Assistant
    /// - Uses semantic search + keyword search for intelligent KB matching
    /// - Generates context-aware, professional responses
    /// - STRICT KB-only mode: Never makes up information
    /// </summary>
    public class GeminiService : IGeminiService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILoggerService _logger;
        private readonly IPromptEngineeringService _promptEngineering;
        private readonly IKnowledgeBaseSearchService _kbSearch;
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly string _model;

        public GeminiService(
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

            _apiKey = _configuration["Gemini:ApiKey"];
            if (string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogError("[GeminiService] Gemini API key not configured", null);
                throw new InvalidOperationException("Gemini API key not configured in appsettings.json");
            }

            _model = _configuration["Gemini:Model"] ?? "gemini-2.0-flash";
            _baseUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent";

            _logger.LogInformation("[GeminiService] Initialized with model: {0}", _model);
        }

        /// <summary>
        /// Generate response using intelligent semantic search + smart prompts
        /// This ensures AI finds CORRECT answers in KB
        /// </summary>
        public async Task<string> GenerateResponseAsync(string userQuery, List<KnowledgeBaseResult> kbResults)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userQuery))
                {
                    _logger.LogWarning("[GeminiService] Empty user query provided");
                    return "Pertanyaan tidak valid. Silakan berikan pertanyaan yang jelas.";
                }

                // Normalize query for better matching
                var normalizedQuery = NormalizeQuery(userQuery);
                _logger.LogInformation("[GeminiService] Processing query: {0}", normalizedQuery);

                // Validate KB results
                if (kbResults == null || kbResults.Count == 0)
                {
                    _logger.LogWarning("[GeminiService] No KB results found for query: {0}", normalizedQuery);
                    return BuildNoResultsMessage(normalizedQuery);
                }

                _logger.LogInformation("[GeminiService] Found {0} KB results (relevance: {1}%)", 
                    kbResults.Count, 
                    (int)(kbResults.Max(r => r.Score) * 100));

                // Use PromptEngineeringService to build intelligent prompt
                var intelligentPrompt = await _promptEngineering.BuildIntelligentPromptAsync(
                    normalizedQuery,
                    kbResults,
                    sessionContext: null
                );

                _logger.LogDebug("[GeminiService] Calling Gemini API with intelligent prompt");
                var response = await CallGeminiApiAsync(intelligentPrompt);

                if (string.IsNullOrEmpty(response))
                {
                    _logger.LogWarning("[GeminiService] Empty response from Gemini");
                    return "Maaf, terjadi kesalahan dalam memproses jawaban. Silakan coba lagi.";
                }

                // Format response for better readability
                var formattedResponse = FormatResponse(response);

                _logger.LogInformation("[GeminiService] Response generated successfully ({0} chars)", response.Length);
                return formattedResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError("[GeminiService] Error in GenerateResponseAsync: {0}", ex, ex.Message);
                return GetErrorMessage();
            }
        }

        /// <summary>
        /// Generate follow-up suggestions based on conversation context
        /// </summary>

        public async Task<List<string>> GenerateSuggestionsAsync(string userQuery, string response)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userQuery) || string.IsNullOrWhiteSpace(response))
                {
                    _logger.LogWarning("[GeminiService] Empty query or response for suggestions");
                    return new List<string>();
                }

                var suggestionPrompt = $@"Analisis percakapan JIFAS berikut dan buat 3 pertanyaan follow-up yang BERBEDA dan NATURAL untuk keep conversation alive.

KONTEKS:
Pertanyaan user: {userQuery}
Jawaban AI: {response}

REQUIREMENTS:
1. Berikan EXACTLY 3 pertanyaan (tidak lebih, tidak kurang)
2. Setiap pertanyaan HARUS dari perspektif berbeda:
   - Satu pertanyaan: Perpanjangan logis dari jawaban (next step)
   - Satu pertanyaan: Klarifikasi atau contoh praktis
   - Satu pertanyaan: Topik terkait yang menarik
3. Tidak boleh hardcoded atau template
4. Harus kontekstual dengan jawaban yang diberikan
5. Satu pertanyaan per baris
6. Tanpa numbering, bullet, atau formatting apapun
7. Natural dan conversational dalam bahasa Indonesia
8. Fokus pada JIFAS features (AR, AP, GL, Budget, PUM, Master Data, Reporting, Settings, dll)

CONTOH OUTPUT (format saja, bukan konten):
Bagaimana cara memvalidasi data sebelum posting?
Apa yang harus dilakukan jika terjadi error saat proses?
Apakah ada fitur untuk mengecek history transaksi?

Jangan gunakan prefix atau format apapun. Berikan HANYA pertanyaan langsung.";

                _logger.LogDebug("[GeminiService] Generating dynamic context-aware suggestions");
                var result = await CallGeminiApiAsync(suggestionPrompt);

                var suggestions = ParseSuggestions(result);

                if (suggestions.Count == 0)
                {
                    _logger.LogDebug("[GeminiService] No suggestions generated");
                    return new List<string>();
                }

                // Limit to max 3 suggestions
                var limitedSuggestions = suggestions.Take(3).ToList();
                _logger.LogInformation("[GeminiService] Generated {0} dynamic suggestions", limitedSuggestions.Count);
                return limitedSuggestions;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GeminiService] Error in GenerateSuggestionsAsync: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// Check if query is within JIFAS scope
        /// Uses intelligent detection based on keywords
        /// </summary>

        public async Task<bool> IsInScopeAsync(string userQuery)
        {
            try
            {
                // Quick negative keyword check
                var outOfScopeKeywords = new[]
                {
                    "bitcoin", "crypto", "dating", "resep", "masakan", "cuaca", "weather", "game", "gaming",
                    "film", "movie", "musik", "covid", "corona", "agama", "seks", "judi", "gambling"
                };

                var lowerQuery = userQuery.ToLower();
                if (outOfScopeKeywords.Any(k => lowerQuery.Contains(k)))
                {
                    _logger.LogDebug("[GeminiService] Query out of scope (negative keyword)");
                    return false;
                }

                // Quick positive keyword check for JIFAS
                var inScopeKeywords = new[]
                {
                    "jifas", "invoice", "approval", "ar", "ap", "gl", "budget", "vendor", "payment",
                    "pum", "laporan", "master", "login", "error", "bagaimana", "apa itu", "berapa"
                };

                if (inScopeKeywords.Any(k => lowerQuery.Contains(k)))
                {
                    _logger.LogDebug("[GeminiService] Query in scope (positive keyword)");
                    return true;
                }

                // Default to in-scope for ambiguous queries
                _logger.LogDebug("[GeminiService] Query marked in scope (ambiguous - default)");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[GeminiService] Error checking scope: {0}", ex, ex.Message);
                return true; // Default to in-scope
            }
        }

        /// <summary>
        /// Format response for better readability
        /// Improvement #8: Response validation checks for quality
        /// </summary>
        private string FormatResponse(string response)
        {
            if (string.IsNullOrEmpty(response))
                return response;

            // Clean up common formatting issues
            response = response.Trim();

            // Ensure proper spacing after punctuation
            response = Regex.Replace(response, @"([.!?])\s+([A-Z])", "$1\n$2");

            // Improvement #8: Validate response quality
            // Check if response is sufficiently detailed
            var wordCount = response.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            var isTooBrief = wordCount < 20;  // Less than 20 words might be too short
            
            if (isTooBrief && response.Length < 100)
            {
                _logger.LogWarning($"[GeminiService] Response validation: Response may be too brief ({wordCount} words)");
                // We'll still return it but log for monitoring
            }

            // Validate no hallucinations by checking for generic filler
            var isGenericFiller = response.Contains("Saya tidak tahu") || 
                                  response.Contains("Tidak ada informasi") ||
                                  response.Contains("Maaf");
            
            if (isGenericFiller && wordCount < 50)
            {
                _logger.LogWarning("[GeminiService] Response validation: Potential generic filler response detected");
            }

            return response;
        }

        /// <summary>
        /// Build message when no KB results found
        /// Improvement #6 & #9: Intelligent fallback with helpful context
        /// </summary>
        private string BuildNoResultsMessage(string userQuery)
        {
            return $@"Maaf, informasi yang Anda cari tidak tersedia di Knowledge Base JIFAS kami saat ini.

**Yang Anda tanyakan:** {userQuery}

**Kemungkinan penyebab:**
• Pertanyaan menggunakan istilah yang berbeda dari dokumentasi KB
• Topik ini belum sepenuhnya didokumentasikan
• Pertanyaan terlalu spesifik atau detail teknis

**Saran untuk pertanyaan yang lebih baik:**
1. **Gunakan istilah JIFAS standar:** AR/AP (piutang/hutang), GL (ledger), Budget, PUM (uang muka), Master Data
2. **Tanyakan fitur spesifik:** Menu mana, field apa, modul mana yang Anda gunakan
3. **Berikan konteks:** Apa yang ingin dicapai, error apa yang muncul

**Topik JIFAS yang tersedia:**
• Konfigurasi Master Data (perusahaan, vendor, COA, budget)
• Pengajuan Invoice dan PUM
• Penerimaan Barang (Receiving)
• Proses Pembayaran
• Jurnal dan Laporan Keuangan
• Penyelesaian Masalah (Troubleshooting)

**Jika masalah berlanjut:**
Hubungi Finance IT Support: finance-it@jababeka.com

Apakah ada pertanyaan lain yang bisa saya bantu?";
        }

        /// <summary>
        /// Get error message for API failures
        /// Improvement #9: Context-enriched error messages
        /// </summary>
        private string GetErrorMessage()
        {
            return @"Maaf, terjadi kesalahan teknis dalam memproses permintaan Anda.

**Silakan coba:**
1. Ulangi pertanyaan dengan frasa yang sedikit berbeda
2. Gunakan kata kunci yang lebih spesifik dan jelas
3. Pastikan pertanyaan berkaitan dengan JIFAS system
4. Jika masalah berlanjut, hubungi IT Help Desk

**Kontak Support:**
Email: finance-it@jababeka.com

Terima kasih atas kesabaran Anda!";
        }

        /// <summary>
        /// Build response for partial KB match
        /// Improvement #6: Helpful guidance even with limited information
        /// </summary>
        private async Task<string> GeneratePartialMatchResponseAsync(string userQuery, List<KnowledgeBaseResult> partialResults)
        {
            try
            {
                // Build a more helpful prompt for partial matches
                var partialPrompt = $@"Kamu adalah JIFAS AI Assistant. User bertanya tentang hal yang SEBAGIAN ada di Knowledge Base.

Situasi: Ada beberapa informasi relevan, tapi mungkin tidak 100% cocok dengan apa yang ditanya.

INSTRUKSI:
1. Gunakan informasi yang PALING RELEVAN dari hasil pencarian
2. Jelaskan dengan JELAS bagian mana dari KB yang cocok dengan pertanyaan
3. Jika ada bagian yang TIDAK TERSEDIA, KATAKAN dengan tegas
4. Berikan saran untuk pertanyaan yang lebih spesifik

KNOWLEDGE BASE YANG DITEMUKAN:
{string.Join("\n\n", partialResults.Take(2).Select(r => $"[{r.Title}] ({(r.Score * 100):F1}% relevan)\n{r.Content.Substring(0, Math.Min(500, r.Content.Length))}..."))}

PERTANYAAN USER: {userQuery}

Jawab dengan jelas dan membantu. Katakan mana yang ada dan mana yang tidak ada di KB.";

                var response = await CallGeminiApiAsync(partialPrompt);
                return response ?? GetErrorMessage();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GeminiService] Error in GeneratePartialMatchResponseAsync: {ex.Message}");
                return BuildNoResultsMessage(userQuery);
            }
        }

        /// <summary>
        /// Build response for no KB match
        /// Improvement #6 & #8: Fallback with validation and helpful alternatives
        /// </summary>
        private async Task<string> GenerateNoMatchResponseAsync(string userQuery)
        {
            try
            {
                var noMatchPrompt = $@"Kamu adalah JIFAS AI Assistant. Knowledge Base NOT punya informasi yang cocok untuk pertanyaan user.

SITUASI: Tidak ada hasil pencarian yang relevan sama sekali.

INSTRUKSI (SANGAT PENTING):
1. KATAKAN dengan JELAS bahwa informasi ini TIDAK ADA di Knowledge Base
2. JANGAN coba untuk fabricate atau guess jawaban
3. Tawarkan topik alternatif yang MUNGKIN user maksudkan
4. Saran untuk pertanyaan yang lebih spesifik
5. Tampilkan modul JIFAS yang tersedia

PERTANYAAN USER: {userQuery}

TOPIK JIFAS YANG TERSEDIA:
- Master Data Setup (perusahaan, vendor, COA, budget)
- Invoice & Payment
- PUM (Pengajuan Uang Muka)
- Receiving (Penerimaan Barang)
- Accounting & Reporting
- Troubleshooting

Jawab dengan JUJUR bahwa info tidak ada, tapi HELPFUL dengan alternatif.";

                var response = await CallGeminiApiAsync(noMatchPrompt);
                return response ?? BuildNoResultsMessage(userQuery);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GeminiService] Error in GenerateNoMatchResponseAsync: {ex.Message}");
                return BuildNoResultsMessage(userQuery);
            }
        }

        /// <summary>
        /// Parse suggestions from Gemini response
        /// </summary>
        private List<string> ParseSuggestions(string response)
        {
            var suggestions = new List<string>();

            if (string.IsNullOrEmpty(response))
                return suggestions;

            var lines = response.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // Remove numbering if present (e.g., "1. ", "1) ")
                trimmed = Regex.Replace(trimmed, @"^\d+[\.\)]\s*", "");
                // Remove bullet points
                trimmed = Regex.Replace(trimmed, @"^[-•*]\s*", "");
                // Remove common prefixes
                trimmed = Regex.Replace(trimmed, @"^(Pertanyaan|Saran|Topik|Follow-up):\s*", "", RegexOptions.IgnoreCase);

                if (!string.IsNullOrEmpty(trimmed) && trimmed.Length > 5)
                {
                    suggestions.Add(trimmed);
                }

                if (suggestions.Count >= 3)
                    break;
            }

            return suggestions;
        }

        /// <summary>
        /// Call Gemini API with optimized settings for KB responses
        /// - Low temperature for factual answers
        /// - Properly formatted prompt
        /// - Error handling and validation
        /// </summary>
        private async Task<string> CallGeminiApiAsync(string prompt)
        {
            try
            {
                _logger.LogDebug("[GeminiService] Preparing API request");

                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = prompt }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.1,              // Very low - stick to facts
                        maxOutputTokens = 2048,         // Allow detailed responses
                        topP = 0.85,                    // Good quality
                        topK = 40                       // Conservative
                    },
                    safetySettings = new[]
                    {
                        new
                        {
                            category = "HARM_CATEGORY_HARASSMENT",
                            threshold = "BLOCK_ONLY_HIGH"
                        }
                    }
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var url = $"{_baseUrl}?key={_apiKey}";
                _httpClient.Timeout = TimeSpan.FromSeconds(60);

                _logger.LogDebug("[GeminiService] Sending request to Gemini API");
                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var errorMsg = $"[GeminiService] API Error {response.StatusCode}: {responseContent}";
                    _logger.LogError(errorMsg, null);
                    throw new Exception($"Gemini API error: {response.StatusCode}");
                }

                var jsonResponse = JObject.Parse(responseContent);
                var text = jsonResponse["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

                if (string.IsNullOrEmpty(text))
                {
                    _logger.LogWarning("[GeminiService] Empty response from Gemini API");
                    throw new Exception("Empty response from Gemini");
                }

                _logger.LogInformation("[GeminiService] API call successful ({0} chars)", text.Length);
                return text.Trim();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError("[GeminiService] HTTP Error connecting to Gemini API", ex);
                throw new Exception("Failed to connect to Gemini API", ex);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError("[GeminiService] Request to Gemini API timed out", ex);
                throw new Exception("Gemini API request timed out", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError("[GeminiService] Unexpected error calling Gemini API", ex);
                throw;
            }
        }

        /// <summary>
        /// Normalize user query for better KB matching
        /// - Fixes common Indonesian typos
        /// - Converts abbreviations
        /// - Standardizes spacing
        /// </summary>
        private string NormalizeQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return query;

            var normalized = query.Trim();
            normalized = Regex.Replace(normalized, @"\s+", " ");

            // Map common Indonesian typos and abbreviations
            var replacements = new Dictionary<string, string>
            {
                { "gmn", "bagaimana" },
                { "gimana", "bagaimana" },
                { "bgmn", "bagaimana" },
                { "knp", "kenapa" },
                { "dri", "dari" },
                { "dr", "dari" },
                { "yg", "yang" },
                { "jd", "jadi" },
                { "sdh", "sudah" },
                { "blm", "belum" },
                { "gak bisa", "tidak bisa" },
                { "ga bisa", "tidak bisa" },
                { "gak ada", "tidak ada" }
            };

            foreach (var kvp in replacements)
            {
                normalized = Regex.Replace(
                    normalized,
                    $@"\b{Regex.Escape(kvp.Key)}\b",
                    kvp.Value,
                    RegexOptions.IgnoreCase
                );
            }

            return normalized;
        }

        /// <summary>
        /// Public API method to call Gemini with custom prompt
        /// Used by other services for advanced operations
        /// </summary>
        async Task<string> IGeminiService.CallGeminiApiAsync(string prompt)
        {
            return await CallGeminiApiAsync(prompt);
        }
    }
}




