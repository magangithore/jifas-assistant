using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Jifas.Assistant.Services;
using Jifas.Assistant.Models;

namespace Jifas.Assistant.Controllers
{
    /// <summary>
    /// Main Chat Controller for JIFAS AI Assistant
    /// Handles user messages and AI responses with RAG capabilities
    /// </summary>
    [ApiController]
    [Route("api/chat")]
    public class ChatController : ControllerBase
    {
        private readonly IChatService _chatService;
        private readonly ILoggerService _loggerService;
        private readonly IHealthCheckService _healthCheckService;

        public ChatController(
            IChatService chatService,
            ILoggerService loggerService,
            IHealthCheckService healthCheckService)
        {
            _chatService = chatService;
            _loggerService = loggerService;
            _healthCheckService = healthCheckService;
        }

        /// <summary>
        /// Send a message to the AI Assistant and get a response
        /// </summary>
        /// <param name="request">Chat request with user message</param>
        /// <returns>Chat response with AI answer</returns>
        /// <response code="200">Successfully processed the message</response>
        /// <response code="400">Invalid request</response>
        /// <response code="500">Internal server error</response>
        [HttpPost("message")]
        [ProducesResponseType(typeof(ChatResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> SendMessage([FromBody] ChatRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { error = "Invalid request", details = ModelState });
                }

                // Extract user from HTTP context if not provided in request
                if (string.IsNullOrWhiteSpace(request?.UserId))
                {
                    request.UserId = HttpContext?.User?.Identity?.Name ?? "unknown";
                }

                // ?? BACKEND DEBUG: Log received context from frontend
                _loggerService.LogDebug(
                    $"[ChatController] Request received — UserId: {request?.UserId}, " +
                    $"IsFirstMessage: {request?.IsFirstMessage}, " +
                    $"UserRole: {request?.UserRole}, " +
                    $"UserCompCode: {request?.UserCompCode}, " +
                    $"ActiveModule: {request?.Context?.ActiveModule}");

                var response = await _chatService.ProcessMessageAsync(request);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _loggerService.LogError($"Error in SendMessage: {ex.Message}", ex);
                return StatusCode(500, new { error = "An error occurred while processing your message", message = ex.Message });
            }
        }

        /// <summary>
        /// Get chat history for a user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>List of previous chat messages</returns>
        /// <response code="200">Successfully retrieved chat history</response>
        /// <response code="404">User not found</response>
        [HttpGet("history/{userId}")]
        [ProducesResponseType(typeof(List<object>), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetChatHistory(string userId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return BadRequest(new { error = "UserId is required" });
                }

                // This would need implementation in ChatService
                return Ok(new { message = "Chat history endpoint", userId });
            }
            catch (Exception ex)
            {
                _loggerService.LogError($"Error in GetChatHistory: {ex.Message}");
                return StatusCode(500, new { error = "An error occurred", message = ex.Message });
            }
        }

        /// <summary>
        /// Health check endpoint
        /// </summary>
        /// <returns>Service health status</returns>
        /// <response code="200">Service is healthy</response>
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
                return StatusCode(503, new { status = "unhealthy", error = ex.Message });
            }
        }
    }
}
