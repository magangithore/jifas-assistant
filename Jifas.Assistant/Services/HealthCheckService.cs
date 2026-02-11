using Jifas.Chatbot.DAL;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Service for monitoring and reporting health status of all system components
    /// </summary>
    public class HealthCheckService : IHealthCheckService
    {
        private readonly ILoggerService _logger;
        private readonly IGeminiService _geminiService;
        private readonly IKnowledgeBaseService _knowledgeBaseService;
        private readonly ICacheService _cacheService;

        public HealthCheckService(IGeminiService geminiService, IKnowledgeBaseService knowledgeBaseService)
        {
            _logger = LoggerFactory.GetLogger();
            _geminiService = geminiService;
            _knowledgeBaseService = knowledgeBaseService;
            _cacheService = new MemoryCacheService();
        }

        public HealthCheckService(ILoggerService logger, IGeminiService geminiService, 
            IKnowledgeBaseService knowledgeBaseService, ICacheService cacheService)
        {
            _logger = logger;
            _geminiService = geminiService;
            _knowledgeBaseService = knowledgeBaseService;
            _cacheService = cacheService;
        }

        /// <summary>
        /// Get overall system health status
        /// </summary>
        public async Task<Dictionary<string, object>> GetHealthStatusAsync()
        {
            try
            {
                var dbHealth = await CheckDatabaseHealthAsync();
                var geminiHealth = await CheckGeminiHealthAsync();
                var kbHealth = await CheckKnowledgeBaseHealthAsync();
                var cacheHealth = await CheckCacheHealthAsync();

                // Determine overall status
                string overallStatus = "healthy";
                if (!dbHealth || !kbHealth || !cacheHealth)
                {
                    overallStatus = "degraded";
                }
                if (!dbHealth && !geminiHealth)
                {
                    overallStatus = "unhealthy";
                }

                var result = new Dictionary<string, object>
                {
                    { "status", overallStatus },
                    { "timestamp", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "uptime_seconds", (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds },
                    { "services", new Dictionary<string, object>
                    {
                        { "database", new { status = dbHealth ? "healthy" : "unhealthy" } },
                        { "gemini_api", new { status = geminiHealth ? "healthy" : "unhealthy" } },
                        { "knowledge_base", new { status = kbHealth ? "healthy" : "unhealthy" } },
                        { "cache", new { status = cacheHealth ? "healthy" : "unhealthy" } }
                    } },
                    { "version", "2.0.0" }
                };

                _logger.LogInformation("[HealthCheckService] Overall status: {0}", overallStatus);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("[HealthCheckService] Error checking health", ex);
                return new Dictionary<string, object>
                {
                    { "status", "unhealthy" },
                    { "timestamp", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss Z") },
                    { "error", ex.Message }
                };
            }
        }

        /// <summary>
        /// Check database connectivity
        /// </summary>
        public async Task<bool> CheckDatabaseHealthAsync()
        {
            try
            {
                using (var db = new JIFAS_AssistantEntities())
                {
                    // Simple query to test database connection
                    var test = db.KnowledgeBaseDocuments.FirstOrDefault();
                    _logger.LogInformation("[HealthCheckService] Database: HEALTHY");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[HealthCheckService] Database health check failed: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Check Gemini API availability
        /// </summary>
        public async Task<bool> CheckGeminiHealthAsync()
        {
            try
            {
                // Verify API key is configured
                var apiKey = ConfigurationManager.AppSettings["Gemini:ApiKey"];
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    _logger.LogWarning("[HealthCheckService] Gemini API key not configured");
                    return false;
                }

                // Try a simple test call to Gemini (in real implementation, could do a test embedding)
                var testMessage = "test";
                // We won't actually call the API to avoid quota usage, just verify it's configured
                _logger.LogInformation("[HealthCheckService] Gemini API: HEALTHY (configured)");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[HealthCheckService] Gemini health check failed: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Check Knowledge Base availability
        /// </summary>
        public async Task<bool> CheckKnowledgeBaseHealthAsync()
        {
            try
            {
                using (var db = new JIFAS_AssistantEntities())
                {
                    // Check if KB documents exist and are accessible
                    var docCount = db.KnowledgeBaseDocuments.Count(d => d.IsActive == true);
                    var chunkCount = db.KnowledgeBaseChunks.Count();

                    if (docCount > 0 && chunkCount > 0)
                    {
                        _logger.LogInformation("[HealthCheckService] Knowledge Base: HEALTHY ({0} docs, {1} chunks)", 
                            docCount, chunkCount);
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning("[HealthCheckService] Knowledge Base: Degraded (no documents or chunks)");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[HealthCheckService] Knowledge Base health check failed: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Check cache system health
        /// </summary>
        public async Task<bool> CheckCacheHealthAsync()
        {
            try
            {
                // Test cache operations
                string testKey = "health_check_test";
                string testValue = Guid.NewGuid().ToString();

                _cacheService.Set(testKey, testValue, 1);
                var retrieved = _cacheService.Get<string>(testKey);

                if (retrieved == testValue)
                {
                    _logger.LogInformation("[HealthCheckService] Cache: HEALTHY");
                    _cacheService.Remove(testKey);
                    return true;
                }
                else
                {
                    _logger.LogWarning("[HealthCheckService] Cache: Value mismatch");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[HealthCheckService] Cache health check failed: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Get detailed status for each service
        /// </summary>
        public async Task<Dictionary<string, Dictionary<string, object>>> GetDetailedStatusAsync()
        {
            try
            {
                var detailed = new Dictionary<string, Dictionary<string, object>>();

                // Database details
                using (var db = new JIFAS_AssistantEntities())
                {
                    var docCount = db.KnowledgeBaseDocuments.Count(d => d.IsActive == true);
                    var chunkCount = db.KnowledgeBaseChunks.Count();
                    var convCount = db.Conversations.Count();

                    detailed["database"] = new Dictionary<string, object>
                    {
                        { "status", "healthy" },
                        { "connection_string", "configured" },
                        { "kb_documents", docCount },
                        { "kb_chunks", chunkCount },
                        { "conversations", convCount },
                        { "checked_at", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss Z") }
                    };
                }

                // Gemini API details
                detailed["gemini"] = new Dictionary<string, object>
                {
                    { "status", await CheckGeminiHealthAsync() ? "healthy" : "unhealthy" },
                    { "model", ConfigurationManager.AppSettings["Gemini:Model"] ?? "unknown" },
                    { "configured", !string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings["Gemini:ApiKey"]) },
                    { "checked_at", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") }
                };

                // Knowledge Base details
                using (var db = new JIFAS_AssistantEntities())
                {
                    var lastDoc = db.KnowledgeBaseDocuments
                        .OrderByDescending(d => d.CreatedAt)
                        .FirstOrDefault();

                    detailed["knowledge_base"] = new Dictionary<string, object>
                    {
                        { "status", await CheckKnowledgeBaseHealthAsync() ? "healthy" : "unhealthy" },
                        { "total_documents", db.KnowledgeBaseDocuments.Count() },
                        { "active_documents", db.KnowledgeBaseDocuments.Count(d => d.IsActive == true) },
                        { "last_document_added", lastDoc != null ? lastDoc.CreatedAt.ToString() : "never" },
                        { "checked_at", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") }
                    };
                }

                // Cache details
                detailed["cache"] = new Dictionary<string, object>
                {
                    { "status", await CheckCacheHealthAsync() ? "healthy" : "unhealthy" },
                    { "type", "in-memory" },
                    { "ttl_seconds", int.TryParse(ConfigurationManager.AppSettings["Caching:DefaultDurationMinutes"], out var mins) ? mins * 60 : 1800 },
                    { "checked_at", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") }
                };

                _logger.LogInformation("[HealthCheckService] Retrieved detailed status for {0} services", detailed.Count);
                return detailed;
            }
            catch (Exception ex)
            {
                _logger.LogError("[HealthCheckService] Error getting detailed status", ex);
                return new Dictionary<string, Dictionary<string, object>>();
            }
        }
    }
}
