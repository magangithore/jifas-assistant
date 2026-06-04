using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Jifas.Assistant.Models;
using Jifas.Assistant.Services;

namespace Jifas.Assistant.Controllers
{
    /// <summary>
    /// Controller utama chatbot JIFAS.
    /// Endpoint di sini menerima pesan user, meneruskan ke service AI/RAG,
    /// dan menyediakan health check khusus modul chat.
    /// </summary>
    [ApiController]
    [Route("api/chat")]
    public class ChatController : ControllerBase
    {
        private readonly IChatService _chatService;
        private readonly ILoggerService _loggerService;
        private readonly IHealthCheckService _healthCheckService;
        private readonly IWebHostEnvironment _environment;

        public ChatController(
            IChatService chatService,
            ILoggerService loggerService,
            IHealthCheckService healthCheckService,
            IWebHostEnvironment environment)
        {
            _chatService = chatService;
            _loggerService = loggerService;
            _healthCheckService = healthCheckService;
            _environment = environment;
        }

        /// <summary>
        /// Terima pesan dari frontend/Postman dan kembalikan jawaban AI.
        /// </summary>
        [HttpPost("message")]
        [ProducesResponseType(typeof(ChatResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> SendMessage([FromBody] ChatRequest request, CancellationToken cancellationToken)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { error = "Invalid request", details = ModelState });
                }

                // Jika frontend tidak mengirim UserId, gunakan identity dari HTTP context.
                if (string.IsNullOrWhiteSpace(request.UserId))
                {
                    request.UserId = HttpContext?.User?.Identity?.Name ?? "unknown";
                }

                _loggerService.LogDebug(
                    $"[ChatController] Request received - UserId: {request.UserId}, " +
                    $"IsFirstMessage: {request.IsFirstMessage}, " +
                    $"UserRole: {request.UserRole}, " +
                    $"UserCompCode: {request.UserCompCode}, " +
                    $"ActiveModule: {request.Context?.ActiveModule}");

                var response = await _chatService.ProcessMessageAsync(request, cancellationToken);
                return Ok(response);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && HttpContext.RequestAborted.IsCancellationRequested)
                {
                    return StatusCode(499, new
                    {
                        error = "Request dibatalkan oleh client.",
                        traceId = HttpContext.TraceIdentifier
                    });
                }

                _loggerService.LogError($"Error in SendMessage: {ex.Message}", ex);
                return StatusCode(500, new
                {
                    error = "Terjadi kesalahan saat memproses pesan.",
                    message = _environment.IsDevelopment()
                        ? ex.Message
                        : "Silakan coba lagi. Jika masih gagal, hubungi IT Help Desk JIFAS.",
                    traceId = HttpContext.TraceIdentifier
                });
            }
        }

        /// <summary>
        /// Placeholder riwayat chat per user.
        /// Saat ini belum mengambil data historis penuh dari service.
        /// </summary>
        [HttpGet("history/{userId}")]
        [ProducesResponseType(typeof(List<object>), 200)]
        [ProducesResponseType(404)]
        public IActionResult GetChatHistory(string userId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return BadRequest(new { error = "UserId is required" });
                }

                return Ok(new { message = "Chat history endpoint", userId });
            }
            catch (Exception ex)
            {
                _loggerService.LogError($"Error in GetChatHistory: {ex.Message}", ex);
                return StatusCode(500, new
                {
                    error = "Terjadi kesalahan saat mengambil riwayat chat.",
                    message = _environment.IsDevelopment()
                        ? ex.Message
                        : "Silakan coba lagi. Jika masih gagal, hubungi IT Help Desk JIFAS.",
                    traceId = HttpContext.TraceIdentifier
                });
            }
        }

        /// <summary>
        /// Health check khusus modul chat.
        /// </summary>
        [HttpGet("health")]
        [ProducesResponseType(typeof(object), 200)]
        public async Task<IActionResult> HealthCheck()
        {
            try
            {
                var status = await _healthCheckService.GetHealthStatusAsync();
                return Ok(new
                {
                    status = "healthy",
                    timestamp = DateTime.UtcNow,
                    checks = status
                });
            }
            catch (Exception ex)
            {
                return StatusCode(503, new
                {
                    status = "unhealthy",
                    error = _environment.IsDevelopment()
                        ? ex.Message
                        : "Health check gagal.",
                    traceId = HttpContext.TraceIdentifier
                });
            }
        }
    }
}
