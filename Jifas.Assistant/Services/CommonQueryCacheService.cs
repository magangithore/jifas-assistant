using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Jifas.Assistant.Services
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
        private readonly IConfiguration _configuration;
        private readonly string _cacheFilePath;
        private DateTime _lastLoadTime;
        private const int RELOAD_INTERVAL_MINUTES = 60;

        public CommonQueryCacheService(ILoggerService logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _cacheFilePath = GetCacheFilePath();
            _cachedQueries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _lastLoadTime = DateTime.MinValue;
            
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
                _logger.LogError("[CommonQueryCache] Error getting cached response: {0}", ex, ex.Message);
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
                    _cachedQueries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    return;
                }

                var json = File.ReadAllText(_cacheFilePath);
                var data = JsonConvert.DeserializeObject<CommonQueriesData>(json);

                if (data?.CommonQueries == null || data.CommonQueries.Count == 0)
                {
                    _logger.LogWarning("[CommonQueryCache] No common queries found in cache file");
                    _cachedQueries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                _logger.LogError("[CommonQueryCache] Error loading cache from file: {0}", ex, ex.Message);
                _cachedQueries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private string GetCacheFilePath()
        {
            // Try to get path from configuration first
            var configPath = _configuration["CommonQueries:FilePath"];
            if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
            {
                _logger.LogInformation("[CommonQueryCache] Using configured cache file path: {0}", configPath);
                return configPath;
            }

            // Try standard locations in order
            var currentDirectory = Directory.GetCurrentDirectory();
            var possiblePaths = new[]
            {
                Path.Combine(currentDirectory, "Data", "CommonQueries.json"),
                Path.Combine(currentDirectory, "Data", "common-queries.json"),
                Path.Combine(currentDirectory, "Data", "common_queries.json"),
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    _logger.LogInformation("[CommonQueryCache] Found cache file at: {0}", path);
                    return path;
                }
            }

            // Return default path
            var defaultPath = Path.Combine(currentDirectory, "Data", "CommonQueries.json");
            _logger.LogWarning("[CommonQueryCache] Cache file not found, using default path: {0}", defaultPath);
            return defaultPath;
        }

        /// <summary>
        /// Internal DTO for deserializing JSON
        /// </summary>
        private class CommonQueriesData
        {
            [JsonProperty("commonQueries")]
            public List<CommonQueryItem> CommonQueries { get; set; } = new List<CommonQueryItem>();
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
