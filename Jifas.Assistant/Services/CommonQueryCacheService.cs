using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Cache untuk pertanyaan umum yang jawabannya statis.
    /// Data diambil dari file JSON agar bisa diubah tanpa compile ulang.
    /// </summary>
    public interface ICommonQueryCacheService
    {
        /// <summary>
        /// Get cached response for a common query
        /// </summary>
        string? GetCachedResponse(string normalizedQuery);

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
        private const int RELOAD_INTERVAL_MINUTES = 60; // Reload cache setiap 1 jam

        public CommonQueryCacheService()
        {
            _cacheFilePath = GetCacheFilePath();
            _cachedQueries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _lastLoadTime = DateTime.MinValue;
            
            // Muat cache saat service pertama kali dibuat.
            ReloadCache();
        }

        public string? GetCachedResponse(string normalizedQuery)
        {
            try
            {
                // Refresh otomatis supaya perubahan file JSON ikut terbaca.
                if ((DateTime.Now - _lastLoadTime).TotalMinutes > RELOAD_INTERVAL_MINUTES)
                {
                    ReloadCache();
                }

                if (string.IsNullOrWhiteSpace(normalizedQuery))
                    return null;

                // Cocokkan exact match terlebih dahulu.
                if (_cachedQueries.ContainsKey(normalizedQuery))
                {
                    System.Diagnostics.Debug.WriteLine($"[CommonQueryCache] Exact match found for: {normalizedQuery}");
                    return _cachedQueries[normalizedQuery];
                }

                // Fallback: cocokkan tanpa peduli huruf besar/kecil.
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
            // Refresh otomatis jika file sudah lama tidak dibaca.
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

                // Masukkan query ke dictionary dengan key yang sudah dinormalisasi.
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
            // Pakai content root ASP.NET Core sebagai base path.
            var basePath = Directory.GetCurrentDirectory();
            
            // Prioritas folder Data untuk deployment production.
            var appDataPath = Path.Combine(basePath, "Data", "CommonQueries.json");
            if (File.Exists(appDataPath))
                return appDataPath;

            // Fallback untuk development lama yang menyimpan data di wwwroot.
            var wwwrootPath = Path.Combine(basePath, "wwwroot", "Data", "CommonQueries.json");
            if (File.Exists(wwwrootPath))
                return wwwrootPath;

            // Return default agar warning path tetap jelas jika file belum ada.
            return appDataPath;
        }

        /// <summary>
        /// DTO internal untuk deserialisasi CommonQueries.json.
        /// </summary>
        private class CommonQueriesData
        {
            [JsonProperty("commonQueries")]
            public List<CommonQueryItem> CommonQueries { get; set; } = new();
        }

        private class CommonQueryItem
        {
            [JsonProperty("key")]
            public string Key { get; set; } = string.Empty;

            [JsonProperty("response")]
            public string Response { get; set; } = string.Empty;
        }
    }
}
