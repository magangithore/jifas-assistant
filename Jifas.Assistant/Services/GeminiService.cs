using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Gemini API service for JIFAS AI Assistant
    /// STRICT: Only uses JIFAS Knowledge Base for answers
    /// </summary>
    public class GeminiService : IGeminiService
    {
        private readonly HttpClient _httpClient;
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

        public GeminiService()
        {
            _httpClient = new HttpClient();
            _apiKey = System.Configuration.ConfigurationManager.AppSettings["Gemini:ApiKey"] 
                ?? "AIzaSyDdTDJEIjXTPI4IoJLTTP4giavhvkR8z0k";
            _model = System.Configuration.ConfigurationManager.AppSettings["Gemini:Model"] 
                ?? "gemini-2.0-flash";
            _baseUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent";
        }

        public async Task<string> GenerateResponseAsync(string userQuery, List<KnowledgeBaseResult> kbResults)
        {
            try
            {
                // Build context from knowledge base results
                var kbContext = BuildKnowledgeBaseContext(kbResults);

                // If no KB results found
                if (string.IsNullOrEmpty(kbContext))
                {
                    return "Mohon maaf, saya tidak menemukan informasi yang relevan di Knowledge Base JIFAS untuk pertanyaan Anda. " +
                           "Silakan hubungi IT Help Desk di finance-it@jababeka.com atau ext. 1234 untuk bantuan lebih lanjut.";
                }

                var prompt = $@"{JIFAS_SYSTEM_PROMPT}

=== KNOWLEDGE BASE JIFAS ===
{kbContext}
=== END KNOWLEDGE BASE ===

Pertanyaan User: {userQuery}

Berikan jawaban berdasarkan Knowledge Base di atas. Jika informasi tidak tersedia di Knowledge Base, katakan dengan jelas.";

                var response = await CallGeminiApiAsync(prompt);
                return response;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GeminiService] Error: {ex.Message}");
                return "Mohon maaf, terjadi kesalahan dalam memproses permintaan Anda. Silakan coba lagi atau hubungi IT Help Desk.";
            }
        }

        public async Task<List<string>> GenerateSuggestionsAsync(string userQuery, string response)
        {
            try
            {
                var prompt = $@"Berdasarkan percakapan berikut tentang JIFAS (Jababeka Integrated Finance Accounting System), 
berikan 3 pertanyaan lanjutan yang mungkin ingin ditanyakan user. 
Pertanyaan HARUS terkait dengan JIFAS saja.

Pertanyaan user: {userQuery}
Jawaban AI: {response}

Format output (HANYA 3 pertanyaan, satu per baris, tanpa numbering atau bullet):
Bagaimana cara...
Apa perbedaan...
Dimana saya bisa...";

                var result = await CallGeminiApiAsync(prompt);
                
                var suggestions = new List<string>();
                var lines = result.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && trimmed.Length > 10)
                    {
                        // Remove numbering if present
                        trimmed = System.Text.RegularExpressions.Regex.Replace(trimmed, @"^\d+[\.\)]\s*", "");
                        trimmed = System.Text.RegularExpressions.Regex.Replace(trimmed, @"^[-�]\s*", "");
                        
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
                    suggestions.AddRange(GetDefaultSuggestions());
                }

                return suggestions;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GeminiService] Suggestion Error: {ex.Message}");
                return GetDefaultSuggestions();
            }
        }

        public async Task<bool> IsInScopeAsync(string userQuery)
        {
            // Fast keyword-based check first
            var outOfScopeKeywords = new[]
            {
                "bitcoin", "crypto", "cryptocurrency", "dating", "pacaran", "cinta", 
                "resep", "masakan", "cuaca", "weather", "politik", "politik",
                "game", "gaming", "film", "movie", "musik", "lagu", "song",
                "covid", "corona", "virus", "vaksin", "agama", "religion",
                "seks", "sex", "porno", "bokep", "judi", "gambling",
                "taruhan", "bet", "saham", "stock", "forex", "trading"
            };

            var lowerQuery = userQuery.ToLower();
            foreach (var keyword in outOfScopeKeywords)
            {
                if (lowerQuery.Contains(keyword))
                {
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
                    return true;
                }
            }

            // For ambiguous queries, use AI to check
            try
            {
                var prompt = $@"Tentukan apakah pertanyaan berikut terkait dengan JIFAS (sistem keuangan dan akuntansi perusahaan).
Pertanyaan: {userQuery}

Jawab HANYA dengan 'YA' atau 'TIDAK'.";

                var result = await CallGeminiApiAsync(prompt);
                return result.ToUpper().Contains("YA");
            }
            catch
            {
                // Default to in-scope to avoid blocking legitimate queries
                return true;
            }
        }

        private string BuildKnowledgeBaseContext(List<KnowledgeBaseResult> kbResults)
        {
            if (kbResults == null || kbResults.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var result in kbResults)
            {
                sb.AppendLine($"[{result.Category}] {result.Title}");
                sb.AppendLine(result.Content);
                sb.AppendLine("---");
            }
            return sb.ToString();
        }

        private async Task<string> CallGeminiApiAsync(string prompt)
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
                throw new Exception($"Gemini API error: {response.StatusCode} - {responseContent}");
            }

            var jsonResponse = JObject.Parse(responseContent);
            var text = jsonResponse["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

            return text ?? "Tidak ada respons dari AI.";
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
