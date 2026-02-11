using System;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Interface for caching service
    /// Abstraction for memory/distributed caching
    /// </summary>
    public interface ICacheService
    {
        /// <summary>
        /// Get value from cache
        /// </summary>
        /// <typeparam name="T">Type of cached value</typeparam>
        /// <param name="key">Cache key</param>
        /// <returns>Cached value or null if not found</returns>
        T Get<T>(string key);

        /// <summary>
        /// Set value in cache with expiration
        /// </summary>
        /// <typeparam name="T">Type of value to cache</typeparam>
        /// <param name="key">Cache key</param>
        /// <param name="value">Value to cache</param>
        /// <param name="durationMinutes">Cache duration in minutes</param>
        void Set<T>(string key, T value, int durationMinutes);

        /// <summary>
        /// Remove item from cache
        /// </summary>
        /// <param name="key">Cache key to remove</param>
        void Remove(string key);

        /// <summary>
        /// Clear all cache items
        /// </summary>
        void Clear();

        /// <summary>
        /// Check if key exists in cache
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <returns>True if key exists, false otherwise</returns>
        bool Exists(string key);
    }
}
