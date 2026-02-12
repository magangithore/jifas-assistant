using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private readonly string _cacheFilePath;
        private DateTime _lastLoadTime;
        private const int RELOAD_INTERVAL_MINUTES = 60; // Reload cache every hour

        public CommonQueryCacheService()
        {
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
                    System.Diagnostics.Debug.WriteLine($"[CommonQueryCache] Exact match found for: {normalizedQuery}");
                    return _cachedQueries[normalizedQuery];
                }

                // Try case-insensitive lookup
                foreach (var kvp in _cachedQueries)
                {
                    if (kvp.Key.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                    {
                        System.Diagnostics.Debug.WriteLine($"[CommonQueryCache] Case-insensitive match found for: {normalizedQuery}");
                        return kvp.Value;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CommonQueryCache] Error getting cached response: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine($"[CommonQueryCache] Cache file not found at: {_cacheFilePath}");
                    return;
                }

                var json = File.ReadAllText(_cacheFilePath);
                var data = JsonConvert.DeserializeObject<CommonQueriesData>(json);

                if (data?.CommonQueries == null || data.CommonQueries.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[CommonQueryCache] No common queries found in cache file");
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
                System.Diagnostics.Debug.WriteLine($"[CommonQueryCache] Loaded {_cachedQueries.Count} common queries from file");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CommonQueryCache] Error loading cache from file: {ex.Message}");
                _cachedQueries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private string GetCacheFilePath()
        {
            // For ASP.NET Core - use content root path
            var basePath = Directory.GetCurrentDirectory();
            
            // Try Data folder first (production style)
            var appDataPath = Path.Combine(basePath, "Data", "CommonQueries.json");
            if (File.Exists(appDataPath))
                return appDataPath;

            // Fallback: wwwroot/Data (for development)
            var wwwrootPath = Path.Combine(basePath, "wwwroot", "Data", "CommonQueries.json");
            if (File.Exists(wwwrootPath))
                return wwwrootPath;

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
