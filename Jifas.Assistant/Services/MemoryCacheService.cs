using System;
using System.Collections.Generic;
using Microsoft.Extensions.Caching.Memory;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Cache lokal berbasis memory.
    /// Dipakai untuk development dan fallback singkat ketika Redis tidak tersedia.
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
                return default!;

            try
            {
                return _cache.TryGetValue(key, out T? value)
                    ? value!
                    : default!;
            }
            catch
            {
                return default!;
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
                // Cache hanya optimasi, jadi kegagalan tulis tidak boleh memutus request utama.
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
                // Cache hanya optimasi, jadi kegagalan hapus tidak boleh memutus request utama.
            }
        }

        public void Clear()
        {
            // IMemoryCache tidak punya API Clear bawaan.
            // Jika perlu clear total, provider cache perlu diganti atau key perlu dilacak sendiri.
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
