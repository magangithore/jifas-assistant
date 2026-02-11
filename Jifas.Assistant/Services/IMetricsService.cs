using Jifas.Assistant.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Interface for metrics and analytics tracking
    /// </summary>
    public interface IMetricsService
    {
        /// <summary>
        /// Log a suggestion display event
        /// </summary>
        Task LogSuggestionDisplayAsync(string sessionId, string userId, string query, List<string> suggestions);

        /// <summary>
        /// Log a suggestion click event
        /// </summary>
        Task LogSuggestionClickAsync(string sessionId, string userId, string suggestion);

        /// <summary>
        /// Log suggestion feedback (helpful or not)
        /// </summary>
        Task LogSuggestionFeedbackAsync(string sessionId, string userId, string suggestion, bool isHelpful);

        /// <summary>
        /// Get metrics for a specific suggestion
        /// </summary>
        Task<SuggestionMetric> GetSuggestionMetricAsync(string suggestion);

        /// <summary>
        /// Get metrics for session
        /// </summary>
        Task<List<SuggestionMetric>> GetSessionMetricsAsync(string sessionId);

        /// <summary>
        /// Get top suggestions by click-through rate
        /// </summary>
        Task<List<SuggestionMetric>> GetTopSuggestionsAsync(int topCount = 10);

        /// <summary>
        /// Get metrics summary statistics
        /// </summary>
        Task<Dictionary<string, object>> GetMetricsSummaryAsync();
    }
}
