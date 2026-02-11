using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Simple in-memory cache service implementation
    /// Uses Dictionary with expiration tracking
    /// No external dependencies required
    /// </summary>
    public class MemoryCacheService : ICacheService
    {
        private class CacheItem<T>
        {
            public T Value { get; set; }
            public DateTime ExpirationTime { get; set; }
        }

        private readonly Dictionary<string, object> _cache = new Dictionary<string, object>();
        private readonly ILoggerService _logger;
        private static readonly object _lockObject = new object();

        public MemoryCacheService()
        {
            _logger = LoggerFactory.GetLogger();
            _logger.LogInformation("[MemoryCacheService] In-memory cache service initialized");
        }

        public T Get<T>(string key)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key))
                    return default(T);

                lock (_lockObject)
                {
                    if (!_cache.ContainsKey(key))
                    {
                        _logger.LogDebug("[MemoryCacheService] Cache MISS for key: {0}", key);
                        return default(T);
                    }

                    var cachedItem = _cache[key] as CacheItem<T>;
                    if (cachedItem == null)
                        return default(T);

                    // Check if expired
                    if (DateTime.UtcNow > cachedItem.ExpirationTime)
                    {
                        _cache.Remove(key);
                        _logger.LogDebug("[MemoryCacheService] Cache EXPIRED for key: {0}", key);
                        return default(T);
                    }

                    _logger.LogDebug("[MemoryCacheService] Cache HIT for key: {0}", key);
                    return cachedItem.Value;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[MemoryCacheService] Error retrieving from cache", ex);
                return default(T);
            }
        }

        public void Set<T>(string key, T value, int durationMinutes)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key) || value == null)
                    return;

                lock (_lockObject)
                {
                    // Remove if exists
                    if (_cache.ContainsKey(key))
                    {
                        _cache.Remove(key);
                    }

                    var cacheItem = new CacheItem<T>
                    {
                        Value = value,
                        ExpirationTime = DateTime.UtcNow.AddMinutes(durationMinutes)
                    };

                    _cache[key] = cacheItem;
                    _logger.LogDebug("[MemoryCacheService] Cache SET for key: {0} (duration: {1} min)", key, durationMinutes);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[MemoryCacheService] Error setting cache", ex);
            }
        }

        public void Remove(string key)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key))
                    return;

                lock (_lockObject)
                {
                    if (_cache.ContainsKey(key))
                    {
                        _cache.Remove(key);
                        _logger.LogDebug("[MemoryCacheService] Cache item removed: {0}", key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[MemoryCacheService] Error removing cache item", ex);
            }
        }

        public void Clear()
        {
            try
            {
                lock (_lockObject)
                {
                    var count = _cache.Count;
                    _cache.Clear();
                    _logger.LogInformation("[MemoryCacheService] Cache cleared ({0} items removed)", count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[MemoryCacheService] Error clearing cache", ex);
            }
        }

        public bool Exists(string key)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key))
                    return false;

                lock (_lockObject)
                {
                    if (!_cache.ContainsKey(key))
                        return false;

                    var cachedItem = _cache[key] as dynamic;
                    if (cachedItem?.ExpirationTime > DateTime.UtcNow)
                        return true;

                    // Remove expired item
                    _cache.Remove(key);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[MemoryCacheService] Error checking cache existence", ex);
                return false;
            }
        }
    }
}

