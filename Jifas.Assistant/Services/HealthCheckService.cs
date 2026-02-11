using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Jifas.Assistant.Data;

namespace Jifas.Assistant.Services
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
        private readonly JifasAssistantDbContext _db;
        private readonly IConfiguration _configuration;
        private static readonly DateTime _startTime = DateTime.UtcNow;

        public HealthCheckService(
            ILoggerService logger,
            IGeminiService geminiService,
            IKnowledgeBaseService knowledgeBaseService,
            ICacheService cacheService,
            JifasAssistantDbContext db,
            IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _geminiService = geminiService ?? throw new ArgumentNullException(nameof(geminiService));
            _knowledgeBaseService = knowledgeBaseService ?? throw new ArgumentNullException(nameof(knowledgeBaseService));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
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
                    { "uptime_seconds", (int)DateTime.UtcNow.Subtract(_startTime).TotalSeconds },
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
                _logger.LogError("[HealthCheckService] Error checking health: {0}", ex, ex.Message);
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
                // Simple query to test database connection
                var test = await _db.KnowledgeBaseDocuments.FirstOrDefaultAsync();
                _logger.LogInformation("[HealthCheckService] Database: HEALTHY");
                return true;
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
                var apiKey = _configuration["Gemini:ApiKey"];
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    _logger.LogWarning("[HealthCheckService] Gemini API key not configured");
                    return false;
                }

                // API is configured (we won't make actual calls to avoid quota usage)
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
                // Check if KB documents exist and are accessible
                var docCount = await _db.KnowledgeBaseDocuments.CountAsync(d => d.IsActive == true);

                if (docCount > 0)
                {
                    _logger.LogInformation("[HealthCheckService] Knowledge Base: HEALTHY ({0} active documents)", docCount);
                    return true;
                }
                else
                {
                    _logger.LogWarning("[HealthCheckService] Knowledge Base: Degraded (no active documents)");
                    return false;
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
                string testKey = $"health_check_test_{Guid.NewGuid()}";
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
                try
                {
                    var docCount = await _db.KnowledgeBaseDocuments.CountAsync(d => d.IsActive == true);
                    var chatCount = await _db.Chats.CountAsync();
                    var lastDoc = await _db.KnowledgeBaseDocuments
                        .OrderByDescending(d => d.CreatedAt)
                        .FirstOrDefaultAsync();

                    detailed["database"] = new Dictionary<string, object>
                    {
                        { "status", "healthy" },
                        { "connection_string", "configured" },
                        { "kb_documents", docCount },
                        { "chats", chatCount },
                        { "checked_at", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss Z") }
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("[HealthCheckService] Error getting database details: {0}", ex.Message);
                    detailed["database"] = new Dictionary<string, object>
                    {
                        { "status", "unhealthy" },
                        { "error", ex.Message }
                    };
                }

                // Gemini API details
                var geminiConfigured = !string.IsNullOrWhiteSpace(_configuration["Gemini:ApiKey"]);
                detailed["gemini"] = new Dictionary<string, object>
                {
                    { "status", await CheckGeminiHealthAsync() ? "healthy" : "unhealthy" },
                    { "model", _configuration["Gemini:Model"] ?? "unknown" },
                    { "configured", geminiConfigured },
                    { "checked_at", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") }
                };

                // Knowledge Base details
                try
                {
                    var totalDocs = await _db.KnowledgeBaseDocuments.CountAsync();
                    var activeDocs = await _db.KnowledgeBaseDocuments.CountAsync(d => d.IsActive == true);
                    var lastDoc = await _db.KnowledgeBaseDocuments
                        .OrderByDescending(d => d.CreatedAt)
                        .FirstOrDefaultAsync();

                    detailed["knowledge_base"] = new Dictionary<string, object>
                    {
                        { "status", await CheckKnowledgeBaseHealthAsync() ? "healthy" : "unhealthy" },
                        { "total_documents", totalDocs },
                        { "active_documents", activeDocs },
                        { "last_document_added", lastDoc?.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss") ?? "never" },
                        { "checked_at", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") }
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("[HealthCheckService] Error getting KB details: {0}", ex.Message);
                    detailed["knowledge_base"] = new Dictionary<string, object>
                    {
                        { "status", "unhealthy" },
                        { "error", ex.Message }
                    };
                }

                // Cache details
                var cacheTtlMinutes = 30; // Default
                if (int.TryParse(_configuration["Caching:DefaultDurationMinutes"], out var mins))
                {
                    cacheTtlMinutes = mins;
                }

                detailed["cache"] = new Dictionary<string, object>
                {
                    { "status", await CheckCacheHealthAsync() ? "healthy" : "unhealthy" },
                    { "type", "in-memory" },
                    { "ttl_seconds", cacheTtlMinutes * 60 },
                    { "checked_at", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") }
                };

                _logger.LogInformation("[HealthCheckService] Retrieved detailed status for {0} services", detailed.Count);
                return detailed;
            }
            catch (Exception ex)
            {
                _logger.LogError("[HealthCheckService] Error getting detailed status: {0}", ex, ex.Message);
                return new Dictionary<string, Dictionary<string, object>>();
            }
        }
    }
}
