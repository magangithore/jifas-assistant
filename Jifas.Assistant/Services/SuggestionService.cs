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
                _logger.LogDebug("[SuggestionService] Generating context-aware suggestions...");

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

                // Generate context-aware suggestions using Gemini
                var suggestions = await _geminiService.GenerateSuggestionsAsync(userQuery, response);

                if (suggestions != null && suggestions.Count > 0)
                {
                    _logger.LogInformation($"[SuggestionService] Generated {suggestions.Count} context-aware suggestions");
                    
                    // Ensure we don't exceed max suggestions
                    return suggestions.Take(MaxSuggestions).ToList();
                }

                _logger.LogDebug("[SuggestionService] No suggestions generated from Gemini");
                return new List<string>();
            }
            catch (System.Exception ex)
            {
                _logger.LogError($"[SuggestionService] Error generating suggestions: {ex.Message}");
                
                // Return empty list on error - let response stand alone
                return new List<string>();
            }
        }
    }
}
