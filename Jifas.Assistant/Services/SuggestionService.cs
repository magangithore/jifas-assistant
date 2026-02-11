using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Interface for suggestion generation
    /// </summary>
    public interface ISuggestionService
    {
        Task<List<string>> GenerateSuggestionsAsync(string userQuery, string response);
    }

    /// <summary>
    /// Service to generate contextual suggestions based on KB and AI
    /// Suggestions are generated dynamically from actual KB content, not hardcoded
    /// </summary>
    public class SuggestionService : ISuggestionService
    {
        private readonly IGeminiService _geminiService;
        private readonly IKnowledgeBaseService _knowledgeBaseService;
        private readonly ILoggerService _logger;
        private readonly IMetricsService _metricsService;

        // Compiled regex patterns for performance
        private static readonly Regex _numberingPattern = new Regex(@"^\d+[\.\)]\s*", RegexOptions.Compiled);
        private static readonly Regex _bulletPattern = new Regex(@"^[-•*]\s*", RegexOptions.Compiled);

        public SuggestionService(IGeminiService geminiService, IKnowledgeBaseService knowledgeBaseService)
        {
            _geminiService = geminiService;
            _knowledgeBaseService = knowledgeBaseService;
            _logger = LoggerFactory.GetLogger();
            _metricsService = new MetricsService();
        }

        public SuggestionService(IGeminiService geminiService, IKnowledgeBaseService knowledgeBaseService, IMetricsService metricsService)
        {
            _geminiService = geminiService;
            _knowledgeBaseService = knowledgeBaseService;
            _logger = LoggerFactory.GetLogger();
            _metricsService = metricsService;
        }

        public async Task<List<string>> GenerateSuggestionsAsync(string userQuery, string response)
        {
            try
            {
                // Validate inputs
                if (string.IsNullOrWhiteSpace(userQuery) || string.IsNullOrWhiteSpace(response))
                {
                    return new List<string>();
                }

                // Get KB context for smarter suggestions
                var kbDocuments = await _knowledgeBaseService.GetAllDocumentsAsync();
                
                // Build KB context from available documents
                var kbTitles = string.Empty;
                if (kbDocuments?.Any() == true)
                {
                    kbTitles = string.Join(", ", kbDocuments.Select(d => d.Title).Take(10));
                }

                // If no KB context available, return empty (better than generate with no context)
                if (string.IsNullOrWhiteSpace(kbTitles))
                {
                    _logger.LogWarning("[SuggestionService] No KB context available for suggestion generation");
                    return new List<string>();
                }

                // AI generates suggestions based on actual KB and conversation context
                var suggestions = await GenerateContextualSuggestionsAsync(userQuery, response, kbTitles);
                
                if (suggestions?.Any() == true)
                {
                    var maxSuggestions = int.TryParse(ConfigurationManager.AppSettings["Suggestion:MaxSuggestions"], out var max) ? max : 3;
                    var minLength = int.TryParse(ConfigurationManager.AppSettings["Suggestion:MinLength"], out var min) ? min : 5;
                    var maxLength = int.TryParse(ConfigurationManager.AppSettings["Suggestion:MaxLength"], out var length) ? length : 200;
                    
                    var filteredSuggestions = suggestions.Where(s => !string.IsNullOrWhiteSpace(s) && s.Length >= minLength && s.Length <= maxLength)
                        .Distinct()
                        .Take(maxSuggestions)
                        .ToList();

                    // Log metrics for suggestion display
                    _ = _metricsService.LogSuggestionDisplayAsync(
                        sessionId: Guid.NewGuid().ToString(),
                        userId: "anonymous",
                        query: userQuery,
                        suggestions: filteredSuggestions);

                    return filteredSuggestions;
                }

                return new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError("[SuggestionService] Error generating suggestions", ex);
                return new List<string>();
            }
        }

        /// <summary>
        /// Generate suggestions using AI based on actual KB and conversation context
        /// No hardcoded suggestions - everything is dynamic
        /// </summary>
        private async Task<List<string>> GenerateContextualSuggestionsAsync(string userQuery, string response, string kbContext)
        {
            try
            {
                var prompt = $@"Anda adalah AI assistant untuk sistem JIFAS. Berdasarkan percakapan user berikut, 
generate 3 pertanyaan lanjutan yang relevan dan membantu user memahami JIFAS lebih baik.

KONTEKS KB yang tersedia:
{kbContext}

Pertanyaan user: {userQuery}
Jawaban sistem: {response}

INSTRUKSI:
- Generate 3 pertanyaan follow-up yang natural dan relevan
- Pertanyaan HARUS tentang JIFAS dan dokumentasi yang ada
- Pertanyaan HARUS dalam Bahasa Indonesia
- Pertanyaan harus membantu user explore fitur/topik terkait
- Format: Satu pertanyaan per baris, TANPA numbering atau bullet
- Jangan hanya copy dari jawaban, buat pertanyaan baru yang related

CONTOH OUTPUT:
Bagaimana cara mengakses JIFAS dari luar jaringan kantor?
Apa perbedaan JIFAS dengan sistem akuntansi sebelumnya?
Dimana saya bisa menemukan panduan lengkap JIFAS?

OUTPUT (3 pertanyaan):";

                var result = await _geminiService.GenerateSuggestionsAsync(userQuery, response);
                
                if (result != null && result.Any())
                {
                    return ParseSuggestions(string.Join("\n", result));
                }

                return new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError("[SuggestionService] Contextual generation error", ex);
                return new List<string>();
            }
        }

        private List<string> ParseSuggestions(string rawSuggestions)
        {
            var suggestions = new List<string>();
            var lines = rawSuggestions.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                // Remove numbering using compiled regex (1. 2. 3. or 1) 2) 3))
                trimmed = _numberingPattern.Replace(trimmed, "");
                // Remove bullet points using compiled regex
                trimmed = _bulletPattern.Replace(trimmed, "");

                if (!string.IsNullOrEmpty(trimmed) && trimmed.Length > 5)
                {
                    suggestions.Add(trimmed);
                }
            }

            return suggestions;
        }
    }
}
