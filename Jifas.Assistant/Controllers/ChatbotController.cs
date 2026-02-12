using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jifas.Assistant.Services;
using Jifas.Assistant.Models;
using jifas_assistant.DAL.Models;

namespace Jifas.Assistant.Controllers
{
    /// <summary>
    /// JIFAS AI Assistant API Controller
    /// Handles chat conversations, knowledge base queries, and ticket creation
    /// </summary>
    [ApiController]
    [Route("api/chatbot")]
    public class ChatbotController : ControllerBase
    {
        private readonly IChatService _chatService;
        private readonly ITicketService _ticketService;
        private readonly IHealthCheckService _healthCheckService;
        private readonly IPerformanceMonitorService _performanceMonitor;
        private readonly IAnalyticsService _analyticsService;
        private readonly JIFAS_AssistantContext _db;

        public ChatbotController(
            IChatService chatService,
            ITicketService ticketService,
            IHealthCheckService healthCheckService,
            IPerformanceMonitorService performanceMonitor,
            IAnalyticsService analyticsService,
            JIFAS_AssistantContext db)
        {
            _chatService = chatService;
            _ticketService = ticketService;
            _healthCheckService = healthCheckService;
            _performanceMonitor = performanceMonitor;
            _analyticsService = analyticsService;
            _db = db;
        }

        /// <summary>
        /// Process a chat message and return AI response
        /// </summary>
        /// <param name="request">Chat request containing user message</param>
        /// <returns>AI response with suggestions</returns>
        [HttpPost("conversation")]
        public async Task<IActionResult> Conversation([FromBody] ChatRequest request)
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
                return StatusCode(500, new { error = "Terjadi kesalahan dalam memproses permintaan Anda." });
            }
        }

        /// <summary>
        /// Create a JIFAS support ticket
        /// </summary>
        /// <param name="request">Ticket creation request</param>
        /// <returns>Ticket creation result</returns>
        [HttpPost("ticket")]
        public async Task<IActionResult> CreateTicket([FromBody] TicketRequest request)
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
                    Title = request.Subject,
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
                return StatusCode(500, new { error = "Gagal membuat ticket." });
            }
        }

        /// <summary>
        /// Test endpoint to verify API is running
        /// </summary>
        [HttpGet("test")]
        public IActionResult Test()
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
        [HttpGet("health")]
        public async Task<IActionResult> Health()
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
        [HttpGet("health/detailed")]
        public async Task<IActionResult> HealthDetailed()
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
        [HttpGet("kb/stats")]
        public async Task<IActionResult> GetKnowledgeBaseStats()
        {
            try
            {
                var docCount = await _db.KnowledgeBaseDocuments.CountAsync(d => d.IsActive == true);
                var chunkCount = await _db.KnowledgeBaseChunks.CountAsync();

                return Ok(new
                {
                    documents = docCount,
                    chunks = chunkCount,
                    lastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Error getting stats: {ex.Message}" });
            }
        }

        /// <summary>
        /// Get document performance analytics
        /// </summary>
        [HttpGet("analytics/documents")]
        public async Task<IActionResult> GetDocumentAnalytics()
        {
            try
            {
                var docPerformance = await _analyticsService.GetDocumentPerformanceAsync();

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
                return StatusCode(500, new { error = $"Error getting analytics: {ex.Message}" });
            }
        }

        /// <summary>
        /// Get popular queries analytics
        /// </summary>
        [HttpGet("analytics/queries")]
        public async Task<IActionResult> GetQueryAnalytics([FromQuery] int days = 30)
        {
            try
            {
                var queries = await _analyticsService.GetPopularQueriesAsync(days);

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
                return StatusCode(500, new { error = $"Error getting query analytics: {ex.Message}" });
            }
        }

        /// <summary>
        /// Get system health metrics
        /// </summary>
        [HttpGet("analytics/health")]
        public async Task<IActionResult> GetSystemHealth()
        {
            try
            {
                var health = await _analyticsService.GetSystemHealthAsync();

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
                return StatusCode(500, new { error = $"Error getting system health: {ex.Message}" });
            }
        }

        /// <summary>
        /// Get improvement recommendations
        /// </summary>
        [HttpGet("analytics/recommendations")]
        public async Task<IActionResult> GetRecommendations()
        {
            try
            {
                var recommendations = await _analyticsService.GetRecommendationsAsync();

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
                return StatusCode(500, new { error = $"Error getting recommendations: {ex.Message}" });
            }
        }

        /// <summary>
        /// Get performance metrics for all tracked operations
        /// </summary>
        [HttpGet("performance/metrics")]
        public IActionResult GetPerformanceMetrics()
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
                return StatusCode(500, new { error = $"Error getting performance metrics: {ex.Message}" });
            }
        }

        /// <summary>
        /// Get slow operations exceeding threshold
        /// </summary>
        [HttpGet("performance/slow")]
        public IActionResult GetSlowOperations([FromQuery] double threshold = 1000)
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
                return StatusCode(500, new { error = $"Error getting slow operations: {ex.Message}" });
            }
        }

        /// <summary>
        /// Clear all performance metrics
        /// </summary>
        [HttpPost("performance/clear")]
        public IActionResult ClearPerformanceMetrics()
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
                return StatusCode(500, new { error = $"Error clearing metrics: {ex.Message}" });
            }
        }
    }
}
