using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Jifas.Assistant.Services;
using Jifas.Assistant.Models;

namespace Jifas.Assistant.Controllers
{
    /// <summary>
    /// JIFAS AI Assistant API Controller
    /// Handles chat conversations and ticket creation
    /// </summary>
    [ApiController]
    [Route("api/chatbot")]
    public class ChatbotController : ControllerBase
    {
        private readonly IChatService _chatService;
        private readonly ITicketService _ticketService;
        private readonly IHealthCheckService _healthCheckService;

        public ChatbotController(
            IChatService chatService,
            ITicketService ticketService,
            IHealthCheckService healthCheckService)
        {
            _chatService = chatService;
            _ticketService = ticketService;
            _healthCheckService = healthCheckService;
        }


        /// <summary>
        /// Process a chat message and return AI response with suggestions
        /// </summary>
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
        /// Create a support ticket
        /// </summary>
        [HttpPost("ticket")]
        public async Task<IActionResult> CreateTicket([FromBody] CreateTicketRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest("Title is required");
            }

            try
            {
                var result = await _ticketService.CreateTicketAsync(request);

                if (result.Success)
                    return Ok(result);
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
        /// Check API health status
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
        /// Test endpoint
        /// </summary>
        [HttpGet("test")]
        public IActionResult Test()
        {
            return Ok(new
            {
                message = "JIFAS AI Assistant API is running",
                version = "1.0.0",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }
    }
}
