using System;
using System.Collections.Generic;
using Microsoft.Extensions.Caching.Memory;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// In-memory cache implementation using Microsoft.Extensions.Caching.Memory
    /// </summary>
    public class MemoryCacheService : ICacheService
    {
        private readonly IMemoryCache _cache;

        public MemoryCacheService(IMemoryCache cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public T Get<T>(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return default;

            try
            {
                _cache.TryGetValue(key, out T value);
                return value;
            }
            catch
            {
                return default;
            }
        }

        public void Set<T>(string key, T value, int durationMinutes)
        {
            if (string.IsNullOrWhiteSpace(key) || value == null)
                return;

            try
            {
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(Math.Max(1, durationMinutes))
                };

                _cache.Set(key, value, cacheOptions);
            }
            catch
            {
                // Silently fail on cache write errors
            }
        }

        public void Remove(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            try
            {
                _cache.Remove(key);
            }
            catch
            {
                // Silently fail on cache removal errors
            }
        }

        public void Clear()
        {
            // IMemoryCache doesn't have a Clear() method,
            // so we would need to track keys manually or dispose/recreate
            // For now, this is a no-op as per standard IMemoryCache behavior
        }

        public bool Exists(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            try
            {
                return _cache.TryGetValue(key, out _);
            }
            catch
            {
                return false;
            }
        }
    }
}
