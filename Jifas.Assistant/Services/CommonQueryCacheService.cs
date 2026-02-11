using System;
using System.Collections.Generic;
using System.IO;
using System.Web;
using Newtonsoft.Json;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Option 1 Optimization: Common Query Cache Service
    /// Loads common queries from external JSON file (not hardcoded)
    /// Makes it easy to add/update cached queries without code changes
    /// </summary>
    public interface ICommonQueryCacheService
    {
        /// <summary>
        /// Get cached response for a common query
        /// </summary>
        string GetCachedResponse(string normalizedQuery);

        /// <summary>
        /// Get all cached queries
        /// </summary>
        Dictionary<string, string> GetAllCachedQueries();

        /// <summary>
        /// Reload cache from file (useful for runtime updates)
        /// </summary>
        void ReloadCache();
    }

    public class CommonQueryCacheService : ICommonQueryCacheService
    {
        private Dictionary<string, string> _cachedQueries;
        private readonly ILoggerService _logger;
        private readonly string _cacheFilePath;
        private DateTime _lastLoadTime;
        private const int RELOAD_INTERVAL_MINUTES = 60; // Reload cache every hour

        public CommonQueryCacheService()
        {
            _logger = LoggerFactory.GetLogger();
            _cacheFilePath = GetCacheFilePath();
            _cachedQueries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _lastLoadTime = DateTime.MinValue;
            
            // Load cache on initialization
            ReloadCache();
        }

        public string GetCachedResponse(string normalizedQuery)
        {
            try
            {
                // Check if cache needs reload (hourly)
                if ((DateTime.Now - _lastLoadTime).TotalMinutes > RELOAD_INTERVAL_MINUTES)
                {
                    ReloadCache();
                }

                if (string.IsNullOrWhiteSpace(normalizedQuery))
                    return null;

                // Try exact match first
                if (_cachedQueries.ContainsKey(normalizedQuery))
                {
                    _logger.LogDebug("[CommonQueryCache] Exact match found for: {0}", normalizedQuery);
                    return _cachedQueries[normalizedQuery];
                }

                // Try case-insensitive lookup
                foreach (var kvp in _cachedQueries)
                {
                    if (kvp.Key.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("[CommonQueryCache] Case-insensitive match found for: {0}", normalizedQuery);
                        return kvp.Value;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError("[CommonQueryCache] Error getting cached response: " + ex.Message);
                return null;
            }
        }

        public Dictionary<string, string> GetAllCachedQueries()
        {
            // Check if reload needed
            if ((DateTime.Now - _lastLoadTime).TotalMinutes > RELOAD_INTERVAL_MINUTES)
            {
                ReloadCache();
            }

            return new Dictionary<string, string>(_cachedQueries);
        }

        public void ReloadCache()
        {
            try
            {
                if (!File.Exists(_cacheFilePath))
                {
                    _logger.LogWarning("[CommonQueryCache] Cache file not found at: {0}", _cacheFilePath);
                    return;
                }

                var json = File.ReadAllText(_cacheFilePath);
                var data = JsonConvert.DeserializeObject<CommonQueriesData>(json);

                if (data?.CommonQueries == null || data.CommonQueries.Count == 0)
                {
                    _logger.LogWarning("[CommonQueryCache] No common queries found in cache file");
                    return;
                }

                _cachedQueries.Clear();

                // Load queries into dictionary
                foreach (var query in data.CommonQueries)
                {
                    if (!string.IsNullOrWhiteSpace(query.Key) && !string.IsNullOrWhiteSpace(query.Response))
                    {
                        _cachedQueries[query.Key.ToLower().Trim()] = query.Response;
                    }
                }

                _lastLoadTime = DateTime.Now;
                _logger.LogInformation("[CommonQueryCache] Loaded {0} common queries from file", _cachedQueries.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError("[CommonQueryCache] Error loading cache from file: " + ex.Message);
                _cachedQueries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private string GetCacheFilePath()
        {
            // First try: App_Data/Data folder (for production)
            var appDataPath = Path.Combine(HttpRuntime.AppDomainAppPath, "Data", "CommonQueries.json");
            if (File.Exists(appDataPath))
                return appDataPath;

            // Fallback: Project root Data folder (for development)
            var rootPath = Path.Combine(HttpRuntime.AppDomainAppPath, "Data", "CommonQueries.json");
            if (File.Exists(rootPath))
                return rootPath;

            // Return default path (will show warning if not exists)
            return appDataPath;
        }

        /// <summary>
        /// Internal DTO for deserializing JSON
        /// </summary>
        private class CommonQueriesData
        {
            [JsonProperty("commonQueries")]
            public List<CommonQueryItem> CommonQueries { get; set; }
        }

        private class CommonQueryItem
        {
            [JsonProperty("key")]
            public string Key { get; set; }

            [JsonProperty("response")]
            public string Response { get; set; }
        }
    }
}
