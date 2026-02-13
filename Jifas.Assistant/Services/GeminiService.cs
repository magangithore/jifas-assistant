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
                if (string.IsNullOrWhiteSpace(userQuery))
                {
                    _logger.LogWarning("[GeminiService] Empty query for suggestions");
                    return GetDefaultSuggestions();
                }

                var suggestionPrompt = $@"Berdasarkan percakapan tentang JIFAS berikut, berikan EXACTLY 3 pertanyaan lanjutan yang relevan.
Pertanyaan HARUS terkait dengan JIFAS (AR, AP, GL, Budget, PUM, Master Data, Reporting).

Pertanyaan user: {userQuery}
Jawaban AI: {response}

INSTRUKSI:
- Berikan HANYA 3 pertanyaan
- Satu pertanyaan per baris
- Tanpa numbering atau bullet
- Pilih pertanyaan praktis dan sering ditanyakan
- Format natural dalam bahasa Indonesia

Contoh:
Bagaimana cara membuat invoice di JIFAS?
Apa perbedaan AR dan AP?
Bagaimana proses approval document?";

                _logger.LogDebug("[GeminiService] Generating suggestions");
                var result = await CallGeminiApiAsync(suggestionPrompt);

                var suggestions = ParseSuggestions(result);

                if (suggestions.Count == 0)
                {
                    _logger.LogDebug("[GeminiService] Using default suggestions");
                    return GetDefaultSuggestions();
                }

                _logger.LogInformation("[GeminiService] Generated {0} suggestions", suggestions.Count);
                return suggestions;
            }
            catch (Exception ex)
            {
                _logger.LogError("[GeminiService] Error in GenerateSuggestionsAsync: {0}", ex, ex.Message);
                return GetDefaultSuggestions();
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
        /// </summary>
        private string FormatResponse(string response)
        {
            if (string.IsNullOrEmpty(response))
                return response;

            // Clean up common formatting issues
            response = response.Trim();

            // Ensure proper spacing after punctuation
            response = Regex.Replace(response, @"([.!?])\s+([A-Z])", "$1\n$2");

            return response;
        }

        /// <summary>
        /// Build message when no KB results found
        /// </summary>
        private string BuildNoResultsMessage(string userQuery)
        {
            return $@"Maaf, saya tidak menemukan informasi yang relevan di Knowledge Base JIFAS untuk pertanyaan Anda:

""{userQuery}""

**Kemungkinan penyebab:**
• Pertanyaan menggunakan istilah yang berbeda dari Knowledge Base
• Topik ini belum didokumentasikan
• Pertanyaan terlalu spesifik

**Saran:**
1. Coba rephrase dengan istilah JIFAS standar (AR, AP, GL, Budget, PUM)
2. Tanyakan fitur atau menu spesifik
3. Hubungi IT Help Desk: finance-it@jababeka.com

Apakah ada pertanyaan lain tentang JIFAS?";
        }

        /// <summary>
        /// Get error message for API failures
        /// </summary>
        private string GetErrorMessage()
        {
            return @"Maaf, terjadi kesalahan dalam memproses permintaan Anda.

**Silakan coba:**
1. Ulangi pertanyaan Anda
2. Gunakan kata kunci yang lebih spesifik
3. Hubungi IT Help Desk jika masalah berlanjut

Terima kasih atas kesabaran Anda!";
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

                // Remove numbering if present
                trimmed = Regex.Replace(trimmed, @"^\d+[\.\)]\s*", "");
                trimmed = Regex.Replace(trimmed, @"^[-•*]\s*", "");

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
        /// Get default suggestions
        /// </summary>
        private List<string> GetDefaultSuggestions()
        {
            return new List<string>
            {
                "Bagaimana cara membuat invoice di JIFAS?",
                "Apa perbedaan antara AR dan AP?",
                "Bagaimana proses approval document?"
            };
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




