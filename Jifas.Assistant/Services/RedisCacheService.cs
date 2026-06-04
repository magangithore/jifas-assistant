using System;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Cache berbasis Redis untuk menyimpan jawaban chatbot, hasil KB, dan session.
    /// Memory cache tetap dipakai sebagai fallback singkat agar request tidak gagal saat Redis sementara bermasalah.
    /// </summary>
    public class RedisCacheService : ICacheService
    {
        private readonly IDistributedCache _distributedCache;
        private readonly IMemoryCache _memoryCache;
        private readonly JsonSerializerSettings _jsonSettings;

        public RedisCacheService(IDistributedCache distributedCache, IMemoryCache memoryCache)
        {
            _distributedCache = distributedCache ?? throw new ArgumentNullException(nameof(distributedCache));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _jsonSettings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.None,
                NullValueHandling = NullValueHandling.Ignore
            };
        }

        public T Get<T>(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return default!;

            try
            {
                var cachedJson = _distributedCache.GetString(key);
                if (!string.IsNullOrWhiteSpace(cachedJson))
                {
                    var value = JsonConvert.DeserializeObject<T>(cachedJson, _jsonSettings);
                    if (value != null)
                    {
                        _memoryCache.Set(key, value, TimeSpan.FromMinutes(5));
                    }

                    return value!;
                }
            }
            catch
            {
                // Redis is an optimization; fall back to local memory cache.
            }

            try
            {
                return _memoryCache.TryGetValue(key, out T? value)
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

            var duration = TimeSpan.FromMinutes(Math.Max(1, durationMinutes));

            try
            {
                _memoryCache.Set(key, value, duration);
            }
            catch
            {
                // Cache lokal hanya optimasi, jadi failure tidak boleh merusak request utama.
            }

            try
            {
                var json = JsonConvert.SerializeObject(value, _jsonSettings);
                _distributedCache.SetString(
                    key,
                    json,
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = duration
                    });
            }
            catch
            {
                // Redis hanya optimasi, jadi failure tidak boleh merusak request utama.
            }
        }

        public void Remove(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            try
            {
                _memoryCache.Remove(key);
            }
            catch
            {
            }

            try
            {
                _distributedCache.Remove(key);
            }
            catch
            {
            }
        }

        public void Clear()
        {
            // IDistributedCache tidak punya API clear-all yang aman lintas provider.
            // Jika butuh flush total, gunakan redis-cli dengan target database yang benar.
        }

        public bool Exists(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            try
            {
                if (_distributedCache.Get(key) != null)
                    return true;
            }
            catch
            {
            }

            try
            {
                return _memoryCache.TryGetValue(key, out _);
            }
            catch
            {
                return false;
            }
        }
    }
}
