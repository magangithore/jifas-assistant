using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Health check service for monitoring system components
    /// </summary>
    public interface IHealthCheckService
    {
        /// <summary>
        /// Check overall system health
        /// </summary>
        Task<Dictionary<string, object>> GetHealthStatusAsync();

        /// <summary>
        /// Check database connectivity
        /// </summary>
        Task<bool> CheckDatabaseHealthAsync();

        /// <summary>
        /// Check Gemini API availability
        /// </summary>
        Task<bool> CheckGeminiHealthAsync();

        /// <summary>
        /// Check Knowledge Base availability
        /// </summary>
        Task<bool> CheckKnowledgeBaseHealthAsync();

        /// <summary>
        /// Check cache system health
        /// </summary>
        Task<bool> CheckCacheHealthAsync();

        /// <summary>
        /// Get detailed service status
        /// </summary>
        Task<Dictionary<string, Dictionary<string, object>>> GetDetailedStatusAsync();
    }
}
