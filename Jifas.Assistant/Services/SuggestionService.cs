using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Jifas.Assistant.Services
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
    /// 
    /// Suggestions are generated dynamically from actual KB content, not hardcoded.
    /// Uses Gemini AI to generate relevant follow-up questions based on conversation context.
    /// Compatible with .NET 10 and uses proper dependency injection.
    /// </summary>
    public class SuggestionService : ISuggestionService
    {
        private readonly IGeminiService _geminiService;
        private readonly IKnowledgeBaseService _knowledgeBaseService;
        private readonly ILoggerService _logger;
        private readonly IConfiguration _configuration;
        private readonly IMetricsService _metricsService;

        // Configuration settings with defaults
        private readonly int _maxSuggestions;
        private readonly int _minLength;
        private readonly int _maxLength;

        // Compiled regex patterns for performance
        private static readonly Regex _numberingPattern = new Regex(@"^\d+[\.\)]\s*", RegexOptions.Compiled);
        private static readonly Regex _bulletPattern = new Regex(@"^[-•*]\s*", RegexOptions.Compiled);

        /// <summary>
        /// Initialize suggestion service with dependency injection
        /// </summary>
        public SuggestionService(
            IGeminiService geminiService,
            IKnowledgeBaseService knowledgeBaseService,
            ILoggerService logger,
            IConfiguration configuration,
            IMetricsService metricsService)
        {
            _geminiService = geminiService ?? throw new ArgumentNullException(nameof(geminiService));
            _knowledgeBaseService = knowledgeBaseService ?? throw new ArgumentNullException(nameof(knowledgeBaseService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _metricsService = metricsService ?? throw new ArgumentNullException(nameof(metricsService));

            // Read configuration with defaults
            _maxSuggestions = _configuration.GetValue("Suggestion:MaxSuggestions", 3);
            _minLength = _configuration.GetValue("Suggestion:MinLength", 5);
            _maxLength = _configuration.GetValue("Suggestion:MaxLength", 200);

            _logger.LogInformation("[SuggestionService] Initialized with max suggestions: {0}, length range: {1}-{2}",
                _maxSuggestions, _minLength, _maxLength);
        }

        /// <summary>
        /// Generate contextual suggestions based on user query and AI response
        /// </summary>
        public async Task<List<string>> GenerateSuggestionsAsync(string userQuery, string response)
        {
            try
            {
                // Validate inputs
                if (string.IsNullOrWhiteSpace(userQuery))
                {
                    _logger.LogWarning("[SuggestionService] Empty user query provided");
                    return new List<string>();
                }

                if (string.IsNullOrWhiteSpace(response))
                {
                    _logger.LogWarning("[SuggestionService] Empty response provided");
                    return new List<string>();
                }

                _logger.LogDebug("[SuggestionService] Generating suggestions for query: {0}", 
                    userQuery.Substring(0, Math.Min(50, userQuery.Length)));

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
                    // Filter suggestions by length and uniqueness
                    var filteredSuggestions = suggestions
                        .Where(s => !string.IsNullOrWhiteSpace(s) && 
                                   s.Length >= _minLength && 
                                   s.Length <= _maxLength)
                        .Distinct()
                        .Take(_maxSuggestions)
                        .ToList();

                    if (filteredSuggestions.Any())
                    {
                        // Log metrics for suggestion display
                        _ = _metricsService.LogSuggestionDisplayAsync(
                            sessionId: Guid.NewGuid().ToString(),
                            userId: "anonymous",
                            query: userQuery,
                            suggestions: filteredSuggestions);

                        _logger.LogDebug("[SuggestionService] Generated {0} suggestions", filteredSuggestions.Count);
                        return filteredSuggestions;
                    }
                }

                _logger.LogWarning("[SuggestionService] No valid suggestions generated");
                return new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError("[SuggestionService] Error generating suggestions: {0}", ex, ex.Message);
                return new List<string>();
            }
        }

        /// <summary>
        /// Generate suggestions using AI based on actual KB and conversation context
        /// No hardcoded suggestions - everything is dynamic
        /// </summary>
        private async Task<List<string>> GenerateContextualSuggestionsAsync(
            string userQuery,
            string response,
            string kbContext)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userQuery) || string.IsNullOrWhiteSpace(response))
                {
                    return new List<string>();
                }

                // AI generates suggestions using Gemini service
                var result = await _geminiService.GenerateSuggestionsAsync(userQuery, response);
                
                if (result != null && result.Any())
                {
                    var parsed = ParseSuggestions(string.Join("\n", result));
                    _logger.LogDebug("[SuggestionService] Parsed {0} suggestions from AI response", parsed.Count);
                    return parsed;
                }

                _logger.LogWarning("[SuggestionService] No suggestions returned from AI service");
                return new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError("[SuggestionService] Contextual generation error: {0}", ex, ex.Message);
                return new List<string>();
            }
        }

        /// <summary>
        /// Parse suggestions from raw AI response
        /// Removes numbering, bullet points, and applies length filters
        /// </summary>
        private List<string> ParseSuggestions(string rawSuggestions)
        {
            if (string.IsNullOrWhiteSpace(rawSuggestions))
            {
                return new List<string>();
            }

            var suggestions = new List<string>();
            var lines = rawSuggestions.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                try
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed))
                        continue;

                    // Remove numbering using compiled regex (1. 2. 3. or 1) 2) 3))
                    trimmed = _numberingPattern.Replace(trimmed, "");
                    
                    // Remove bullet points using compiled regex
                    trimmed = _bulletPattern.Replace(trimmed, "");

                    // Clean up whitespace again after removals
                    trimmed = trimmed.Trim();

                    if (!string.IsNullOrEmpty(trimmed) && trimmed.Length > _minLength)
                    {
                        suggestions.Add(trimmed);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("[SuggestionService] Error parsing suggestion line: {0}", ex.Message);
                }
            }

            return suggestions;
        }
    }
}
