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
    /// Gemini API service for JIFAS AI Assistant
    /// STRICT: Only uses JIFAS Knowledge Base for answers
    /// </summary>
    public class GeminiService : IGeminiService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILoggerService _logger;
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly string _model;

        // JIFAS-specific system prompt
        private const string JIFAS_SYSTEM_PROMPT = @"
Kamu adalah JIFAS AI Assistant, asisten virtual khusus untuk Jababeka Integrated Finance Accounting System (JIFAS).

ATURAN KETAT:
1. HANYA jawab pertanyaan yang berkaitan dengan JIFAS berdasarkan Knowledge Base yang diberikan.
2. Jika konteks Knowledge Base tidak mencakup jawaban, katakan dengan jelas bahwa informasi tidak tersedia di Knowledge Base JIFAS.
3. JANGAN pernah menjawab pertanyaan di luar konteks JIFAS (seperti cuaca, berita, resep masakan, dll).
4. Jawab dalam Bahasa Indonesia yang profesional dan mudah dipahami.
5. Berikan jawaban yang ringkas namun lengkap.
6. Jika user bertanya hal yang tidak terkait JIFAS, tolak dengan sopan dan arahkan kembali ke topik JIFAS.

FORMAT JAWABAN:
- Gunakan bahasa yang ramah dan profesional
- Berikan langkah-langkah jika diperlukan
- Sertakan informasi kontak support jika relevan

TOPIK YANG DAPAT DIJAWAB:
- Login dan akses JIFAS
- Troubleshooting JIFAS
- Fitur dan menu JIFAS (AR, AP, GL, Budget, Reports)
- Konfigurasi dan pengaturan JIFAS
- User guide dan panduan JIFAS
- Pertanyaan teknis seputar JIFAS
";

        public GeminiService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILoggerService logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;

            _apiKey = _configuration["Gemini:ApiKey"];
            if (string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogError("[GeminiService] Gemini API key not configured", null);
                throw new InvalidOperationException(
                    "Gemini API key not found. Please set Gemini:ApiKey in appsettings.json"
                );
            }

            _model = _configuration["Gemini:Model"] ?? "gemini-2.0-flash";
            _baseUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent";

            _logger.LogInformation("[GeminiService] Initialized with model: {0}", _model);
        }

        public async Task<string> GenerateResponseAsync(string userQuery, List<KnowledgeBaseResult> kbResults)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userQuery))
                {
                    _logger.LogWarning("[GeminiService] Empty user query provided");
                    return "Pertanyaan tidak valid. Silakan berikan pertanyaan yang jelas.";
                }

                // Build context from knowledge base results
                var kbContext = BuildKnowledgeBaseContext(kbResults);

                // If no KB results found
                if (string.IsNullOrEmpty(kbContext))
                {
                    _logger.LogWarning("[GeminiService] No KB results found for query: {0}", userQuery);
                    return "Mohon maaf, saya tidak menemukan informasi yang relevan di Knowledge Base JIFAS untuk pertanyaan Anda. " +
                           "Silakan hubungi IT Help Desk di finance-it@jababeka.com untuk bantuan lebih lanjut.";
                }

                var prompt = $@"{JIFAS_SYSTEM_PROMPT}

=== KNOWLEDGE BASE JIFAS ===
{kbContext}
=== END KNOWLEDGE BASE ===

Pertanyaan User: {userQuery}

Berikan jawaban berdasarkan Knowledge Base di atas. Jika informasi tidak tersedia di Knowledge Base, katakan dengan jelas.";

                _logger.LogDebug("[GeminiService] Calling Gemini API for query: {0}", userQuery);
                var response = await CallGeminiApiAsync(prompt);
                
                _logger.LogInformation("[GeminiService] Successfully generated response for query");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError("[GeminiService] Error in GenerateResponseAsync: {0}", ex, ex.Message);
                return "Mohon maaf, terjadi kesalahan dalam memproses permintaan Anda. Silakan coba lagi atau hubungi IT Help Desk.";
            }
        }

        public async Task<List<string>> GenerateSuggestionsAsync(string userQuery, string response)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userQuery))
                {
                    _logger.LogWarning("[GeminiService] Empty query for suggestions");
                    return GetDefaultSuggestions();
                }

                var prompt = $@"Berdasarkan percakapan berikut tentang JIFAS (Jababeka Integrated Finance Accounting System), 
berikan 3 pertanyaan lanjutan yang mungkin ingin ditanyakan user. 
Pertanyaan HARUS terkait dengan JIFAS saja.

Pertanyaan user: {userQuery}
Jawaban AI: {response}

Format output (HANYA 3 pertanyaan, satu per baris, tanpa numbering atau bullet):
Bagaimana cara...
Apa perbedaan...
Dimana saya bisa...";

                _logger.LogDebug("[GeminiService] Generating suggestions for query: {0}", userQuery);
                var result = await CallGeminiApiAsync(prompt);
                
                var suggestions = new List<string>();
                var lines = result.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && trimmed.Length > 10)
                    {
                        // Remove numbering if present
                        trimmed = Regex.Replace(trimmed, @"^\d+[\.\)]\s*", "");
                        trimmed = Regex.Replace(trimmed, @"^[-•]\s*", "");
                        
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            suggestions.Add(trimmed);
                        }
                    }
                    
                    if (suggestions.Count >= 3) break;
                }

                // Fallback suggestions if AI doesn't return enough
                if (suggestions.Count == 0)
                {
                    _logger.LogDebug("[GeminiService] Using fallback suggestions");
                    suggestions.AddRange(GetDefaultSuggestions());
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

        public async Task<bool> IsInScopeAsync(string userQuery)
        {
            try
            {
                // Fast keyword-based check first
                var outOfScopeKeywords = new[]
                {
                    "bitcoin", "crypto", "cryptocurrency", "dating", "pacaran", "cinta", 
                    "resep", "masakan", "cuaca", "weather", "politik", "game", "gaming", 
                    "film", "movie", "musik", "lagu", "song", "covid", "corona", "virus", 
                    "vaksin", "agama", "religion", "seks", "sex", "porno", "judi", "gambling",
                    "taruhan", "bet", "saham", "stock", "forex", "trading"
                };

                var lowerQuery = userQuery.ToLower();
                foreach (var keyword in outOfScopeKeywords)
                {
                    if (lowerQuery.Contains(keyword))
                    {
                        _logger.LogDebug("[GeminiService] Query marked out-of-scope by keyword: {0}", keyword);
                        return false;
                    }
                }

                // JIFAS-related keywords (in-scope indicators)
                var inScopeKeywords = new[]
                {
                    "jifas", "login", "akses", "password", "menu", "modul",
                    "ar", "ap", "gl", "invoice", "payment", "vendor", "customer",
                    "budget", "anggaran", "report", "laporan", "finance", "keuangan",
                    "accounting", "akuntansi", "journal", "jurnal", "voucher",
                    "approval", "error", "masalah", "tidak bisa", "gagal", "help",
                    "bantuan", "cara", "bagaimana", "dimana", "apa itu", "user guide"
                };

                foreach (var keyword in inScopeKeywords)
                {
                    if (lowerQuery.Contains(keyword))
                    {
                        _logger.LogDebug("[GeminiService] Query marked in-scope by keyword: {0}", keyword);
                        return true;
                    }
                }

                // For ambiguous queries, use keyword matching as fallback
                _logger.LogDebug("[GeminiService] Query marked in-scope by default (ambiguous)");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[GeminiService] Error in IsInScopeAsync: {0}", ex, ex.Message);
                // Default to in-scope to avoid blocking legitimate queries
                return true;
            }
        }

        private string BuildKnowledgeBaseContext(List<KnowledgeBaseResult> kbResults)
        {
            if (kbResults == null || kbResults.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (var result in kbResults)
            {
                if (result == null) continue;

                sb.AppendLine($"[{result.Category}] {result.Title}");
                sb.AppendLine(result.Content);
                sb.AppendLine($"(Confidence: {result.Score:F2})");
                sb.AppendLine("---");
            }

            return sb.ToString();
        }

        private async Task<string> CallGeminiApiAsync(string prompt)
        {
            try
            {
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
                        temperature = 0.3,
                        maxOutputTokens = 1024,
                        topP = 0.8,
                        topK = 40
                    }
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var url = $"{_baseUrl}?key={_apiKey}";
                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("[GeminiService] API Error {0}: {1}", null, response.StatusCode, responseContent);
                    throw new Exception($"Gemini API error: {response.StatusCode}");
                }

                var jsonResponse = JObject.Parse(responseContent);
                var text = jsonResponse["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

                if (string.IsNullOrEmpty(text))
                {
                    _logger.LogWarning("[GeminiService] Empty response from Gemini API");
                    return "Tidak ada respons dari AI.";
                }

                return text;
            }
            catch (Exception ex)
            {
                _logger.LogError("[GeminiService] Error calling Gemini API: {0}", ex, ex.Message);
                throw;
            }
        }

        private List<string> GetDefaultSuggestions()
        {
            return new List<string>
            {
                "Bagaimana cara login ke JIFAS?",
                "Apa saja modul yang tersedia di JIFAS?",
                "Bagaimana cara menghubungi IT Help Desk?"
            };
        }
    }
}
