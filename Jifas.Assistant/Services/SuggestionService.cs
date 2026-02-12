using System.Collections.Generic;
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

        public SuggestionService(IGeminiService geminiService, ILoggerService logger)
        {
            _geminiService = geminiService;
            _logger = logger;
        }

        public async Task<List<string>> GenerateSuggestionsAsync(string userQuery, string response)
        {
            try
            {
                _logger.LogDebug("[SuggestionService] Generating suggestions...");
                
                // Use Gemini to generate context-aware suggestions
                var suggestions = await _geminiService.GenerateSuggestionsAsync(userQuery, response);
                
                return suggestions ?? new List<string>
                {
                    "Bagaimana cara login ke JIFAS?",
                    "Apa saja modul yang tersedia di JIFAS?",
                    "Bagaimana cara menghubungi IT Help Desk?"
                };
            }
            catch (System.Exception ex)
            {
                _logger.LogError($"[SuggestionService] Error: {ex.Message}");
                
                // Return default suggestions on error
                return new List<string>
                {
                    "Bagaimana cara login ke JIFAS?",
                    "Apa saja modul yang tersedia di JIFAS?",
                    "Bagaimana cara menghubungi IT Help Desk?"
                };
            }
        }
    }
}
