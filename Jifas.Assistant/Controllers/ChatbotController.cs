using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using Jifas.Assistant.Data;
using Jifas.Assistant.Models;
using Jifas.Assistant.Services;

namespace Jifas.Assistant.Controllers
{
    /// <summary>
    /// JIFAS AI Assistant Chat API Controller
    /// Handles chat conversations and AI responses
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class ChatbotController : ControllerBase
    {
        private readonly IChatService _chatService;
        private readonly IHealthCheckService _healthCheckService;
        private readonly ITicketService _ticketService;
        private readonly IOutOfScopeDetector _outOfScopeDetector;
        private readonly ISuggestionService _suggestionService;
        private readonly ILoggerService _logger;

        public ChatbotController(
            IChatService chatService,
            IHealthCheckService healthCheckService,
            ITicketService ticketService,
            IOutOfScopeDetector outOfScopeDetector,
            ISuggestionService suggestionService,
            ILoggerService logger)
        {
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
            _ticketService = ticketService ?? throw new ArgumentNullException(nameof(ticketService));
            _outOfScopeDetector = outOfScopeDetector ?? throw new ArgumentNullException(nameof(outOfScopeDetector));
            _suggestionService = suggestionService ?? throw new ArgumentNullException(nameof(suggestionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Health check endpoint
        /// </summary>
        [HttpGet("health")]
        public async Task<IActionResult> Health()
        {
            try
            {
                var health = await _healthCheckService.GetHealthStatusAsync();
                return Ok(health);
            }
            catch (Exception ex)
            {
                _logger.LogError("[ChatbotController] Health check error: {0}", ex, ex.Message);
                return StatusCode(500, new { status = "unhealthy", error = ex.Message });
            }
        }

        /// <summary>
        /// Process a chat message
        /// </summary>
        [HttpPost("conversation")]
        public async Task<IActionResult> Conversation([FromBody] ChatRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Message))
                {
                    return BadRequest(new { success = false, message = "Message is required" });
                }

                _logger.LogInformation("[ChatbotController] Processing conversation for session: {0}", request.SessionId);

                // Check if query is out of scope
                var scopeCheck = await _outOfScopeDetector.CheckScopeAsync(request.Message);

                var response = await _chatService.ProcessMessageAsync(request);

                // Generate suggestions
                if (response.Success)
                {
                    try
                    {
                        response.Suggestions = await _suggestionService.GenerateSuggestionsAsync(
                            request.Message,
                            response.Message);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("[ChatbotController] Failed to generate suggestions: {0}", ex.Message);
                        // Continue without suggestions
                    }
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError("[ChatbotController] Error processing conversation: {0}", ex, ex.Message);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error processing your request. Please try again.",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Create a support ticket
        /// </summary>
        [HttpPost("ticket")]
        public async Task<IActionResult> CreateTicket([FromBody] CreateTicketRequest request)
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new { success = false, message = "Request is required" });
                }

                _logger.LogInformation("[ChatbotController] Creating ticket for user: {0}", request.UserId);

                var result = await _ticketService.CreateTicketAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError("[ChatbotController] Error creating ticket: {0}", ex, ex.Message);
                return StatusCode(500, new { success = false, message = "Error creating ticket" });
            }
        }

        /// <summary>
        /// Get ticket status
        /// </summary>
        [HttpGet("ticket/{ticketNumber}")]
        public async Task<IActionResult> GetTicket(string ticketNumber)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ticketNumber))
                {
                    return BadRequest(new { success = false, message = "Ticket number is required" });
                }

                _logger.LogInformation("[ChatbotController] Retrieving ticket: {0}", ticketNumber);

                var result = await _ticketService.GetTicketAsync(ticketNumber);
                
                if (!result.Success)
                    return NotFound(result);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError("[ChatbotController] Error retrieving ticket: {0}", ex, ex.Message);
                return StatusCode(500, new { success = false, message = "Error retrieving ticket" });
            }
        }
    }
}
