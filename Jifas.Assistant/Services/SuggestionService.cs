using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Jifas.Assistant.Services
{
    public interface ISuggestionService
    {
        Task<List<string>> GenerateSuggestionsAsync(string userQuery, string response);
    }

    public class SuggestionService : ISuggestionService
    {
        private readonly IGeminiService _geminiService;
        private readonly ILoggerService _logger;
        private const int MaxSuggestions = 3;
        private const int MinResponseLengthForSuggestions = 10;

        public SuggestionService(IGeminiService geminiService, ILoggerService logger)
        {
            _geminiService = geminiService;
            _logger = logger;
        }

        public async Task<List<string>> GenerateSuggestionsAsync(string userQuery, string response)
        {
            try
            {
                _logger.LogDebug("[SuggestionService] Generating smart suggestions...");

                // Validate inputs
                if (string.IsNullOrWhiteSpace(userQuery) || string.IsNullOrWhiteSpace(response))
                {
                    _logger.LogDebug("[SuggestionService] Invalid input for suggestions");
                    return new List<string>();
                }

                // Skip suggestions for very short responses
                if (response.Length < MinResponseLengthForSuggestions)
                {
                    _logger.LogDebug("[SuggestionService] Response too short for suggestions");
                    return new List<string>();
                }

                // ENHANCED: Generate highly contextual suggestions
                var suggestions = await GenerateSmartSuggestionsAsync(userQuery, response);

                if (suggestions != null && suggestions.Count > 0)
                {
                    _logger.LogInformation($"[SuggestionService] Generated {suggestions.Count} smart suggestions");
                    return suggestions.Take(MaxSuggestions).ToList();
                }

                _logger.LogDebug("[SuggestionService] Falling back to AI suggestions");
                return await _geminiService.GenerateSuggestionsAsync(userQuery, response);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SuggestionService] Error generating suggestions: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// Generate smart, contextual suggestions based on topic and intent
        /// </summary>
        private async Task<List<string>> GenerateSmartSuggestionsAsync(string userQuery, string response)
        {
            try
            {
                // Detect topic from query and response
                var topic = DetectTopic(userQuery, response);
                var intent = DetectIntent(userQuery);
                
                // Generate contextual follow-up questions
                var prompt = $@"Berdasarkan percakapan ini, buatlah 3 pertanyaan lanjutan yang NATURAL dan HELPFUL.

PERTANYAAN AWAL: {userQuery}
JAWABAN: {response.Substring(0, Math.Min(response.Length, 500))}
TOPIK: {topic}
INTENT: {intent}

ATURAN KETAT:
1. Pertanyaan harus RELEVAN dengan topik yang dibahas
2. Pertanyaan harus BERBEDA dari pertanyaan awal (bukan mengulang)
3. Pertanyaan harus ACTIONABLE (user bisa langsung tanya)
4. Singkat dan jelas (max 10 kata per pertanyaan)
5. Dalam Bahasa Indonesia natural

CONTOH FORMAT YANG BENAR:
1. Bagaimana cara approve invoice tersebut?
2. Siapa yang bisa melihat laporan ini?
3. Apa yang terjadi jika budget over?

PERTANYAAN LANJUTAN (langsung tulis 3 pertanyaan):";

                var suggestionsText = await _geminiService.CallGeminiApiAsync(prompt);
                return ExtractSuggestions(suggestionsText, topic);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[SuggestionService] Smart suggestions failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Detect topic from query and response
        /// </summary>
        private string DetectTopic(string query, string response)
        {
            var combined = (query + " " + response).ToLower();
            
            var topicPatterns = new Dictionary<string, string[]>
            {
                { "Invoice", new[] { "invoice", "faktur", "tagihan", "inv" } },
                { "Payment", new[] { "payment", "pembayaran", "bayar", "transfer", "bg", "cek" } },
                { "PUM", new[] { "pum", "uang muka", "advance", "kasbon" } },
                { "Budget", new[] { "budget", "anggaran", "overbudget", "over budget" } },
                { "Approval", new[] { "approval", "approve", "reject", "otorisasi", "persetujuan" } },
                { "Receiving", new[] { "receiving", "rv", "terima barang", "penerimaan" } },
                { "Master Data", new[] { "master", "vendor", "coa", "company", "divisi", "departemen" } },
                { "Accounting", new[] { "posting", "jurnal", "gl", "ledger", "akuntansi", "laporan" } }
            };

            foreach (var topic in topicPatterns)
            {
                if (topic.Value.Any(keyword => combined.Contains(keyword)))
                {
                    return topic.Key;
                }
            }

            return "General JIFAS";
        }

        /// <summary>
        /// Detect intent from query
        /// </summary>
        private string DetectIntent(string query)
        {
            var queryLower = query.ToLower();
            
            if (queryLower.Contains("cara") || queryLower.Contains("bagaimana") || queryLower.Contains("langkah"))
                return "HowTo";
            if (queryLower.Contains("error") || queryLower.Contains("masalah") || queryLower.Contains("gagal") || queryLower.Contains("tidak bisa"))
                return "Troubleshooting";
            if (queryLower.Contains("apa itu") || queryLower.Contains("jelaskan") || queryLower.Contains("pengertian"))
                return "Explanation";
            if (queryLower.Contains("siapa") || queryLower.Contains("yang bisa") || queryLower.Contains("yang harus"))
                return "Authorization";
            if (queryLower.Contains("dimana") || queryLower.Contains("menu") || queryLower.Contains("lokasi"))
                return "Navigation";
                
            return "General";
        }

        /// <summary>
        /// Extract suggestions from AI response with topic context
        /// </summary>
        private List<string> ExtractSuggestions(string responseText, string topic)
        {
            var suggestions = new List<string>();

            if (string.IsNullOrWhiteSpace(responseText))
                return suggestions;

            var lines = responseText.Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                // Match patterns like "1. [text]" or "1) [text]" or "- [text]"
                var cleanLine = line.Trim();
                
                // Remove numbering
                if (cleanLine.Length > 2 && (cleanLine[1] == '.' || cleanLine[1] == ')'))
                {
                    cleanLine = cleanLine.Substring(2).Trim();
                }
                else if (cleanLine.StartsWith("-"))
                {
                    cleanLine = cleanLine.Substring(1).Trim();
                }

                // Validate suggestion quality
                if (!string.IsNullOrWhiteSpace(cleanLine) && 
                    cleanLine.Length >= 10 && 
                    cleanLine.Length <= 100 &&
                    !cleanLine.ToLower().Contains("pertanyaan") &&
                    (cleanLine.Contains("?") || cleanLine.ToLower().StartsWith("bagaimana") || 
                     cleanLine.ToLower().StartsWith("apa") || cleanLine.ToLower().StartsWith("siapa") ||
                     cleanLine.ToLower().StartsWith("dimana") || cleanLine.ToLower().StartsWith("kapan") ||
                     cleanLine.ToLower().StartsWith("kenapa") || cleanLine.ToLower().StartsWith("berapa")))
                {
                    // Ensure ends with question mark
                    if (!cleanLine.EndsWith("?"))
                        cleanLine += "?";
                    
                    suggestions.Add(cleanLine);
                }
            }

            return suggestions.Take(MaxSuggestions).ToList();
        }
    }
}
