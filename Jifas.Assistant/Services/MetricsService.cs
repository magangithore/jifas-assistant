using Jifas.Assistant.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Service for tracking suggestion metrics and analytics
    /// 
    /// Logs suggestion displays, clicks, and user feedback.
    /// Stores metrics in cache for fast access with configurable tracking.
    /// </summary>
    public class MetricsService : IMetricsService
    {
        private readonly ILoggerService _logger;
        private readonly ICacheService _cacheService;
        private readonly IConfiguration _configuration;
        private readonly bool _enableMetrics;

        /// <summary>
        /// Initialize metrics service with dependency injection
        /// </summary>
        public MetricsService(
            ILoggerService logger,
            ICacheService cacheService,
            IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            // Read configuration setting for metrics tracking
            _enableMetrics = _configuration.GetValue("Metrics:EnableTracking", true);
            
            _logger.LogInformation("[MetricsService] Initialized with metrics tracking: {0}", 
                _enableMetrics ? "ENABLED" : "DISABLED");
        }

        /// <summary>
        /// Log suggestion display event
        /// </summary>
        public async Task LogSuggestionDisplayAsync(string sessionId, string userId, string query, List<string> suggestions)
        {
            try
            {
                if (!_enableMetrics)
                {
                    return;
                }

                if (suggestions == null || suggestions.Count == 0)
                {
                    _logger.LogWarning("[MetricsService] No suggestions to log for session {0}", sessionId);
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
                    var cacheDurationMinutes = _configuration.GetValue("Metrics:CacheDurationMinutes", 1440);
                    _cacheService.Set(cacheKey, metric, cacheDurationMinutes);

                    _logger.LogDebug("[MetricsService] Logged suggestion display for session {0}: {1}", 
                        sessionId, suggestion.Substring(0, Math.Min(50, suggestion.Length)));
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError("[MetricsService] Error logging suggestion display: {0}", ex, ex.Message);
            }
        }

        /// <summary>
        /// Log suggestion click event
        /// </summary>
        public async Task LogSuggestionClickAsync(string sessionId, string userId, string suggestion)
        {
            try
            {
                if (!_enableMetrics)
                {
                    return;
                }

                string cacheKey = $"METRIC_{sessionId}_{suggestion.GetHashCode()}";
                var metric = _cacheService.Get<SuggestionMetric>(cacheKey);

                if (metric != null)
                {
                    metric.ClickCount++;
                    metric.ClickThroughRate = metric.DisplayCount > 0 
                        ? (decimal)metric.ClickCount / metric.DisplayCount 
                        : 0;
                    metric.UpdatedAt = DateTime.UtcNow;
                    
                    var cacheDurationMinutes = _configuration.GetValue("Metrics:CacheDurationMinutes", 1440);
                    _cacheService.Set(cacheKey, metric, cacheDurationMinutes);

                    _logger.LogDebug("[MetricsService] Logged suggestion click for session {0}, CTR: {1:P}", 
                        sessionId, metric.ClickThroughRate);
                }
                else
                {
                    _logger.LogWarning("[MetricsService] Metric not found for click event: {0}", cacheKey);
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError("[MetricsService] Error logging suggestion click: {0}", ex, ex.Message);
            }
        }

        /// <summary>
        /// Log suggestion feedback
        /// </summary>
        public async Task LogSuggestionFeedbackAsync(string sessionId, string userId, string suggestion, bool isHelpful)
        {
            try
            {
                if (!_enableMetrics)
                {
                    return;
                }

                string cacheKey = $"METRIC_{sessionId}_{suggestion.GetHashCode()}";
                var metric = _cacheService.Get<SuggestionMetric>(cacheKey);

                if (metric != null)
                {
                    metric.IsHelpful = isHelpful;
                    metric.UpdatedAt = DateTime.UtcNow;
                    
                    var cacheDurationMinutes = _configuration.GetValue("Metrics:CacheDurationMinutes", 1440);
                    _cacheService.Set(cacheKey, metric, cacheDurationMinutes);

                    _logger.LogInformation("[MetricsService] Logged feedback for session {0}: {1}", 
                        sessionId, isHelpful ? "Helpful" : "Not Helpful");
                }
                else
                {
                    _logger.LogWarning("[MetricsService] Metric not found for feedback event: {0}", cacheKey);
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError("[MetricsService] Error logging feedback: {0}", ex, ex.Message);
            }
        }

        /// <summary>
        /// Get metrics for specific suggestion
        /// </summary>
        public async Task<SuggestionMetric> GetSuggestionMetricAsync(string suggestion)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(suggestion))
                {
                    _logger.LogWarning("[MetricsService] Empty suggestion provided for metric retrieval");
                    return null;
                }

                // In real implementation, would query from database
                // For now, return null as we're using cache
                _logger.LogDebug("[MetricsService] Retrieving metric for suggestion (length: {0})", suggestion.Length);
                return await Task.FromResult<SuggestionMetric>(null);
            }
            catch (Exception ex)
            {
                _logger.LogError("[MetricsService] Error getting suggestion metric: {0}", ex, ex.Message);
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
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    _logger.LogWarning("[MetricsService] Empty session ID provided");
                    return new List<SuggestionMetric>();
                }

                // In real implementation, would query from database
                var metrics = new List<SuggestionMetric>();
                _logger.LogDebug("[MetricsService] Retrieved metrics for session {0}", sessionId);
                return await Task.FromResult(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError("[MetricsService] Error getting session metrics: {0}", ex, ex.Message);
                return new List<SuggestionMetric>();
            }
        }

        /// <summary>
        /// Get top suggestions by CTR (Click-Through Rate)
        /// </summary>
        public async Task<List<SuggestionMetric>> GetTopSuggestionsAsync(int topCount = 10)
        {
            try
            {
                if (topCount <= 0)
                {
                    _logger.LogWarning("[MetricsService] Invalid top count: {0}", topCount);
                    topCount = 10;
                }

                // In real implementation, would query from database and order by CTR
                var suggestions = new List<SuggestionMetric>();
                _logger.LogDebug("[MetricsService] Retrieved top {0} suggestions", topCount);
                return await Task.FromResult(suggestions);
            }
            catch (Exception ex)
            {
                _logger.LogError("[MetricsService] Error getting top suggestions: {0}", ex, ex.Message);
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
                    { "metrics_enabled", _enableMetrics },
                    { "last_updated", DateTime.UtcNow }
                };

                _logger.LogDebug("[MetricsService] Retrieved metrics summary");
                return await Task.FromResult(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError("[MetricsService] Error getting metrics summary: {0}", ex, ex.Message);
                return new Dictionary<string, object>();
            }
        }
    }
}
    