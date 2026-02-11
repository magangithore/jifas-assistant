using Jifas.Chatbot.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Service for tracking suggestion metrics and analytics
    /// </summary>
    public class MetricsService : IMetricsService
    {
        private readonly ILoggerService _logger;
        private readonly ICacheService _cacheService;

        public MetricsService()
        {
            _logger = LoggerFactory.GetLogger();
            _cacheService = new MemoryCacheService();
        }

        public MetricsService(ILoggerService logger, ICacheService cacheService)
        {
            _logger = logger;
            _cacheService = cacheService;
        }

        /// <summary>
        /// Log suggestion display event
        /// </summary>
        public async Task LogSuggestionDisplayAsync(string sessionId, string userId, string query, List<string> suggestions)
        {
            try
            {
                if (suggestions == null || suggestions.Count == 0)
                {
                    _logger.LogWarning("[MetricsService] No suggestions to log for session {0}", sessionId);
                    return;
                }

                bool enableMetrics = bool.TryParse(
                    ConfigurationManager.AppSettings["Metrics:EnableTracking"] ?? "true", out bool result) && result;

                if (!enableMetrics)
                {
                    return;
                }

                // Log each suggestion display
                foreach (var suggestion in suggestions)
                {
                    var metric = new SuggestionMetric
                    {
                        SessionId = sessionId,
                        UserId = userId,
                        Query = query,
                        Suggestion = suggestion,
                        DisplayCount = 1,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        Notes = "Suggestion displayed to user"
                    };

                    // Store in cache with session ID as key
                    string cacheKey = $"METRIC_{sessionId}_{suggestion.GetHashCode()}";
                    _cacheService.Set(cacheKey, metric, 1440); // 24 hours cache

                    _logger.LogInformation("[MetricsService] Logged suggestion display for session {0}: {1}", 
                        sessionId, suggestion.Substring(0, Math.Min(50, suggestion.Length)));
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError("[MetricsService] Error logging suggestion display", ex);
            }
        }

        /// <summary>
        /// Log suggestion click event
        /// </summary>
        public async Task LogSuggestionClickAsync(string sessionId, string userId, string suggestion)
        {
            try
            {
                bool enableMetrics = bool.TryParse(
                    ConfigurationManager.AppSettings["Metrics:EnableTracking"] ?? "true", out bool result) && result;

                if (!enableMetrics)
                {
                    return;
                }

                string cacheKey = $"METRIC_{sessionId}_{suggestion.GetHashCode()}";
                var metric = _cacheService.Get<SuggestionMetric>(cacheKey);

                if (metric != null)
                {
                    metric.ClickCount++;
                    metric.ClickThroughRate = (decimal)metric.ClickCount / metric.DisplayCount;
                    metric.UpdatedAt = DateTime.UtcNow;
                    _cacheService.Set(cacheKey, metric, 1440);

                    _logger.LogInformation("[MetricsService] Logged suggestion click for session {0}, CTR: {1:P}", 
                        sessionId, metric.ClickThroughRate);
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError("[MetricsService] Error logging suggestion click", ex);
            }
        }

        /// <summary>
        /// Log suggestion feedback
        /// </summary>
        public async Task LogSuggestionFeedbackAsync(string sessionId, string userId, string suggestion, bool isHelpful)
        {
            try
            {
                bool enableMetrics = bool.TryParse(
                    ConfigurationManager.AppSettings["Metrics:EnableTracking"] ?? "true", out bool result) && result;

                if (!enableMetrics)
                {
                    return;
                }

                string cacheKey = $"METRIC_{sessionId}_{suggestion.GetHashCode()}";
                var metric = _cacheService.Get<SuggestionMetric>(cacheKey);

                if (metric != null)
                {
                    metric.IsHelpful = isHelpful;
                    metric.UpdatedAt = DateTime.UtcNow;
                    _cacheService.Set(cacheKey, metric, 1440);

                    _logger.LogInformation("[MetricsService] Logged feedback for session {0}: {1}", 
                        sessionId, isHelpful ? "Helpful" : "Not Helpful");
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError("[MetricsService] Error logging feedback", ex);
            }
        }

        /// <summary>
        /// Get metrics for specific suggestion
        /// </summary>
        public async Task<SuggestionMetric> GetSuggestionMetricAsync(string suggestion)
        {
            try
            {
                // In real implementation, would query from database
                // For now, return null as we're using cache
                return await Task.FromResult<SuggestionMetric>(null);
            }
            catch (Exception ex)
            {
                _logger.LogError("[MetricsService] Error getting suggestion metric", ex);
                return null;
            }
        }

        /// <summary>
        /// Get metrics for session
        /// </summary>
        public async Task<List<SuggestionMetric>> GetSessionMetricsAsync(string sessionId)
        {
            try
            {
                // In real implementation, would query from database
                var metrics = new List<SuggestionMetric>();
                _logger.LogInformation("[MetricsService] Retrieved metrics for session {0}", sessionId);
                return await Task.FromResult(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError("[MetricsService] Error getting session metrics", ex);
                return new List<SuggestionMetric>();
            }
        }

        /// <summary>
        /// Get top suggestions by CTR
        /// </summary>
        public async Task<List<SuggestionMetric>> GetTopSuggestionsAsync(int topCount = 10)
        {
            try
            {
                // In real implementation, would query from database and order by CTR
                var suggestions = new List<SuggestionMetric>();
                _logger.LogInformation("[MetricsService] Retrieved top {0} suggestions", topCount);
                return await Task.FromResult(suggestions);
            }
            catch (Exception ex)
            {
                _logger.LogError("[MetricsService] Error getting top suggestions", ex);
                return new List<SuggestionMetric>();
            }
        }

        /// <summary>
        /// Get metrics summary statistics
        /// </summary>
        public async Task<Dictionary<string, object>> GetMetricsSummaryAsync()
        {
            try
            {
                var summary = new Dictionary<string, object>
                {
                    { "total_suggestions_logged", 0 },
                    { "average_ctr", 0m },
                    { "helpful_suggestions", 0 },
                    { "unhelpful_suggestions", 0 },
                    { "last_updated", DateTime.UtcNow }
                };

                _logger.LogInformation("[MetricsService] Retrieved metrics summary");
                return await Task.FromResult(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError("[MetricsService] Error getting metrics summary", ex);
                return new Dictionary<string, object>();
            }
        }
    }
}
