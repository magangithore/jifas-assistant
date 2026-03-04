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
    /// Local AI Service using Ollama/compatible server with Qwen3:8b model
    /// - Replaces Gemini API for development and internal deployment
    /// - Same interface as IGeminiService for easy switching
    /// - Base URL: 10.0.12.54:11434
    /// - Model: qwen3:8b
    /// </summary>
    public class LocalAIService : IGeminiService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILoggerService _logger;
        private readonly IPromptEngineeringService _promptEngineering;
        private readonly IKnowledgeBaseSearchService _kbSearch;
        private readonly string _baseUrl;
        private readonly string _model;

        public LocalAIService(
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

            // Configuration from appsettings.json
            _baseUrl = _configuration["LocalAI:BaseUrl"] ?? "http://10.0.12.54:11434";
            _model = _configuration["LocalAI:Model"] ?? "qwen3:8b";

            _logger.LogInformation("[LocalAIService] Initialized with model: {0} at {1}", _model, _baseUrl);
        }

        /// <summary>
        /// Generate response using local AI with knowledge base context
        /// </summary>
        public async Task<string> GenerateResponseAsync(string userQuery, List<KnowledgeBaseResult> kbResults)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userQuery))
                {
                    _logger.LogWarning("[LocalAIService] Empty user query provided");
                    return "Pertanyaan tidak valid. Silakan berikan pertanyaan yang jelas.";
                }

                var normalizedQuery = NormalizeQuery(userQuery);
                _logger.LogInformation("[LocalAIService] Processing query: {0}", normalizedQuery);

                if (kbResults == null || kbResults.Count == 0)
                {
                    _logger.LogWarning("[LocalAIService] No KB results found for query: {0}", normalizedQuery);
                    return BuildNoResultsMessage(normalizedQuery);
                }

                _logger.LogInformation("[LocalAIService] Found {0} KB results (relevance: {1}%)",
                    kbResults.Count,
                    (int)(kbResults.Max(r => r.Score) * 100));

                // Build intelligent prompt using the same service
                var intelligentPrompt = await _promptEngineering.BuildIntelligentPromptAsync(
                    normalizedQuery,
                    kbResults,
                    sessionContext: null
                );

                _logger.LogDebug("[LocalAIService] Calling local AI with prompt");
                var response = await CallLocalAIAsync(intelligentPrompt);

                if (string.IsNullOrEmpty(response))
                {
                    _logger.LogWarning("[LocalAIService] Empty response from local AI");
                    return "Maaf, terjadi kesalahan dalam memproses jawaban. Silakan coba lagi.";
                }

                _logger.LogInformation("[LocalAIService] Generated response length: {0} characters", response.Length);
                return response;
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError("[LocalAIService] HTTP error calling local AI server: {0}", httpEx, httpEx.Message);
                return "Maaf, layanan AI saat ini tidak tersedia. Silakan coba lagi nanti.";
            }
            catch (Exception ex)
            {
                _logger.LogError("[LocalAIService] Error generating response: {0}", ex, ex.Message);
                return "Maaf, terjadi kesalahan dalam memproses permintaan Anda.";
            }
        }

        /// <summary>
        /// Generate follow-up suggestions based on response and context
        /// </summary>
        public async Task<List<string>> GenerateSuggestionsAsync(string userQuery, string response)
        {
            try
            {
                var suggestionsPrompt = $@"Berdasarkan pertanyaan dan jawaban berikut, berikan 3 pertanyaan lanjutan yang relevan.
Pertanyaan: {userQuery}
Jawaban: {response}

Format HARUS:
1. [Pertanyaan 1]
2. [Pertanyaan 2]
3. [Pertanyaan 3]

Pertanyaan harus singkat dan relevan dengan topik sebelumnya.";

                var suggestionsText = await CallLocalAIAsync(suggestionsPrompt);
                return ExtractSuggestions(suggestionsText);
            }
            catch (Exception ex)
            {
                _logger.LogError("[LocalAIService] Error generating suggestions: {0}", ex, ex.Message);
                return new List<string>();
            }
        }

        /// <summary>
        /// Check if query is within JIFAS scope
        /// </summary>
        public async Task<bool> IsInScopeAsync(string userQuery)
        {
            try
            {
                var scopePrompt = $@"Apakah pertanyaan berikut berkaitan dengan JIFAS (sistem keuangan, PUM, Finance, Accounting, HR, atau operasional)?
Pertanyaan: {userQuery}

Jawab HANYA dengan 'Ya' atau 'Tidak' tanpa penjelasan.";

                var response = await CallLocalAIAsync(scopePrompt);
                return response.Contains("Ya", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogError("[LocalAIService] Error checking scope: {0}", ex, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Call local AI API directly with custom prompt
        /// Ollama/compatible endpoint: POST /api/generate
        /// </summary>
        public async Task<string> CallLocalAIAsync(string prompt)
        {
            try
            {
                var endpoint = $"{_baseUrl}/api/generate";
                
                var requestBody = new
                {
                    model = _model,
                    prompt = prompt,
                    stream = false,  // Get complete response at once
                    temperature = 0.7,
                    top_p = 0.9,
                    top_k = 40
                };

                var jsonContent = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                _logger.LogDebug("[LocalAIService] Calling {0} with model {1}", endpoint, _model);

                var response = await _httpClient.PostAsync(endpoint, content);
                response.EnsureSuccessStatusCode();

                var responseText = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("[LocalAIService] Response status: {0}", response.StatusCode);

                // Parse Ollama response format
                var jsonResponse = JsonConvert.DeserializeObject<dynamic>(responseText);
                var result = jsonResponse["response"]?.ToString() ?? string.Empty;

                return result.Trim();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError("[LocalAIService] HTTP error: {0}", ex, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError("[LocalAIService] Error calling local AI: {0}", ex, ex.Message);
                throw;
            }
        }

        // Implement IGeminiService.CallGeminiApiAsync to use local AI
        public async Task<string> CallGeminiApiAsync(string prompt)
        {
            return await CallLocalAIAsync(prompt);
        }

        /// <summary>
        /// Helper: Normalize query for better matching
        /// </summary>
        private string NormalizeQuery(string query)
        {
            return Regex.Replace(query.ToLower(), @"\s+", " ").Trim();
        }

        /// <summary>
        /// Helper: Build message when no KB results found
        /// </summary>
        private string BuildNoResultsMessage(string query)
        {
            return $"Maaf, saya tidak menemukan informasi tentang '{query}' di basis pengetahuan JIFAS. " +
                   "Silakan coba pertanyaan lain atau hubungi Tim IT untuk bantuan lebih lanjut.";
        }

        /// <summary>
        /// Helper: Extract suggestions from AI response
        /// </summary>
        private List<string> ExtractSuggestions(string responseText)
        {
            var suggestions = new List<string>();

            try
            {
                var lines = responseText.Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var line in lines)
                {
                    // Match patterns like "1. [text]" or "1) [text]"
                    var match = Regex.Match(line, @"^\d+[.)]\s*(.+)$");
                    if (match.Success)
                    {
                        var suggestion = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(suggestion) && suggestion.Length > 5)
                        {
                            suggestions.Add(suggestion);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[LocalAIService] Error parsing suggestions: {0}", ex, ex.Message);
            }

            return suggestions.Count > 0 ? suggestions : GetDefaultSuggestions();
        }

        /// <summary>
        /// Default suggestions when extraction fails
        /// </summary>
        private List<string> GetDefaultSuggestions()
        {
            return new List<string>
            {
                "Bagaimana cara mengajukan reimbursement?",
                "Siapa yang bertanggung jawab atas departemen ini?",
                "Di mana saya bisa menemukan dokumen pendukung?"
            };
        }
    }
}
