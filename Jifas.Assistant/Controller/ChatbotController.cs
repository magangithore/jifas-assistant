using Jifas.DAL;
using Jifas.DAL.Models;
using Jifas.Chatbot.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Jifas.Chatbot.Controllers
{
    /// <summary>
    /// JIFAS AI Assistant API Controller
    /// Handles chat conversations, knowledge base queries, and ticket creation
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class ChatbotController : ControllerBase
    {
        private readonly IChatService _chatService;
        private readonly ITicketService _ticketService;
        private readonly IHealthCheckService _healthCheckService;
        private readonly IPerformanceMonitorService _performanceMonitor;
        private readonly JifasAssistantDbContext _db;

        public ChatbotController()
        {
            _chatService = new ChatService();
            _ticketService = new TicketService();
            _healthCheckService = new HealthCheckService(
                new GeminiService(),
                new KnowledgeBaseService()
            );
            _performanceMonitor = new PerformanceMonitorService();
            _db = new JifasAssistantDbContext();
        }

        /// <summary>
        /// Process a chat message and return AI response
        /// </summary>
        /// <param name="request">Chat request containing user message</param>
        /// <returns>AI response with suggestions</returns>
        [HttpPost]
        [Route("conversation")]
        public async Task<ActionResult> Conversation([FromBody] ChatRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest("Message is required");
            }

            try
            {
                var response = await _chatService.ProcessMessageAsync(request);
                return Ok(response);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatbotController] Error: {ex.Message}");
                return StatusCode(500, "Terjadi kesalahan dalam memproses permintaan Anda.");
            }
        }

        /// <summary>
        /// Create a JIFAS support ticket
        /// </summary>
        /// <param name="request">Ticket creation request</param>
        /// <returns>Ticket creation result</returns>
        [HttpPost]
        [Route("ticket")]
        public async Task<ActionResult> CreateTicket([FromBody] TicketRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Subject))
            {
                return BadRequest("Subject is required");
            }

            if (string.IsNullOrWhiteSpace(request.Description))
            {
                return BadRequest("Description is required");
            }

            try
            {
                var result = await _ticketService.CreateTicketAsync(new CreateTicketRequest
                {
                    UserId = request.UserId,
                    Subject = request.Subject,
                    Description = request.Description,
                    Category = request.Category,
                    Priority = request.Priority,
                    SessionId = request.SessionId
                });

                var response = new TicketResponse
                {
                    Success = result.Success,
                    TicketNumber = result.TicketNumber,
                    Message = result.Message,
                    Status = result.Status,
                    CreatedAt = result.CreatedAt?.ToString("yyyy-MM-dd HH:mm:ss")
                };

                if (result.Success)
                    return Ok(response);
                else
                    return BadRequest(result.Message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatbotController] Ticket Error: {ex.Message}");
                return StatusCode(500, "Gagal membuat ticket.");
            }
        }

        /// <summary>
        /// Test endpoint to verify API is running
        /// </summary>
        [HttpGet]
        [Route("test")]
        public ActionResult Test()
        {
            var response = new
            {
                sender = "JIFAS AI Assistant",
                message = "JIFAS AI Assistant is running successfully!",
                version = "1.0.0",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                features = new[]
                {
                    "Knowledge Base Search",
                    "Gemini AI Integration",
                    "Ticket Creation",
                    "Conversation Logging"
                }
            };

            return Ok(response);
        }

        /// <summary>
        /// Enhanced health check endpoint - checks all system components
        /// </summary>
        [HttpGet]
        [Route("health")]
        public async Task<ActionResult> Health()
        {
            try
            {
                var healthStatus = await _healthCheckService.GetHealthStatusAsync();
                return Ok(healthStatus);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatbotController] Health check error: {ex.Message}");
                return Ok(new
                {
                    status = "unknown",
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Detailed health check endpoint - provides service-level details
        /// </summary>
        [HttpGet]
        [Route("health/detailed")]
        public async Task<ActionResult> HealthDetailed()
        {
            try
            {
                var detailedStatus = await _healthCheckService.GetDetailedStatusAsync();
                return Ok(new
                {
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    services = detailedStatus
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatbotController] Detailed health check error: {ex.Message}");
                return Ok(new
                {
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get Knowledge Base statistics
        /// </summary>
        [HttpGet]
        [Route("kb/stats")]
        public async Task<ActionResult> GetKnowledgeBaseStats()
        {
            try
            {
                var docCount = await Task.Run(() => _db.KnowledgeBaseDocuments.Count(d => d.IsActive == true));
                var chunkCount = await Task.Run(() => _db.KnowledgeBaseChunks.Count());
                var conversationCount = await Task.Run(() => _db.Conversations.Count());

                return Ok(new
                {
                    documents = docCount,
                    chunks = chunkCount,
                    conversations = conversationCount,
                    lastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error getting stats: {ex.Message}");
            }
        }

        /// <summary>
        /// Get document performance analytics
        /// </summary>
        [HttpGet]
        [Route("analytics/documents")]
        public async Task<ActionResult> GetDocumentAnalytics()
        {
            try
            {
                var analyticsService = new AnalyticsService(_db);
                var docPerformance = await analyticsService.GetDocumentPerformanceAsync();

                return Ok(new
                {
                    success = true,
                    data = docPerformance,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatbotController] Analytics Error: {ex.Message}");
                return StatusCode(500, $"Error getting analytics: {ex.Message}");
            }
        }

        /// <summary>
        /// Get popular queries analytics
        /// </summary>
        [HttpGet]
        [Route("analytics/queries")]
        public async Task<ActionResult> GetQueryAnalytics([FromUri] int days = 30)
        {
            try
            {
                var analyticsService = new AnalyticsService(_db);
                var queries = await analyticsService.GetPopularQueriesAsync(days);

                return Ok(new
                {
                    success = true,
                    period = $"last_{days}_days",
                    data = queries,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatbotController] Query Analytics Error: {ex.Message}");
                return StatusCode(500, $"Error getting query analytics: {ex.Message}");
            }
        }

        /// <summary>
        /// Get system health metrics
        /// </summary>
        [HttpGet]
        [Route("analytics/health")]
        public async Task<ActionResult> GetSystemHealth()
        {
            try
            {
                var analyticsService = new AnalyticsService(_db);
                var health = await analyticsService.GetSystemHealthAsync();

                return Ok(new
                {
                    success = true,
                    data = health,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatbotController] Health Check Error: {ex.Message}");
                return StatusCode(500, $"Error getting system health: {ex.Message}");
            }
        }

        /// <summary>
        /// Get improvement recommendations
        /// </summary>
        [HttpGet]
        [Route("analytics/recommendations")]
        public async Task<ActionResult> GetRecommendations()
        {
            try
            {
                var analyticsService = new AnalyticsService(_db);
                var recommendations = await analyticsService.GetRecommendationsAsync();

                return Ok(new
                {
                    success = true,
                    data = recommendations,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatbotController] Recommendations Error: {ex.Message}");
                return StatusCode(500, $"Error getting recommendations: {ex.Message}");
            }
        }

        /// <summary>
        /// Get performance metrics for all tracked operations
        /// </summary>
        [HttpGet]
        [Route("performance/metrics")]
        public ActionResult GetPerformanceMetrics()
        {
            try
            {
                var metrics = _performanceMonitor.GetAllMetrics();
                return Ok(new
                {
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    operations_tracked = metrics.Count,
                    metrics = metrics
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatbotController] Performance metrics error: {ex.Message}");
                return StatusCode(500, $"Error getting performance metrics: {ex.Message}");
            }
        }

        /// <summary>
        /// Get slow operations exceeding threshold
        /// </summary>
        [HttpGet]
        [Route("performance/slow")]
        public ActionResult GetSlowOperations([FromUri] double threshold = 1000)
        {
            try
            {
                var slowOps = _performanceMonitor.GetSlowOperations(threshold);
                return Ok(new
                {
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    threshold_ms = threshold,
                    slow_operations_count = slowOps.Count,
                    operations = slowOps
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatbotController] Slow operations error: {ex.Message}");
                return StatusCode(500, $"Error getting slow operations: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear all performance metrics
        /// </summary>
        [HttpPost]
        [Route("performance/clear")]
        public ActionResult ClearPerformanceMetrics()
        {
            try
            {
                _performanceMonitor.ClearMetrics();
                return Ok(new
                {
                    status = "cleared",
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatbotController] Clear metrics error: {ex.Message}");
                return StatusCode(500, $"Error clearing metrics: {ex.Message}");
            }
        }
    }
}