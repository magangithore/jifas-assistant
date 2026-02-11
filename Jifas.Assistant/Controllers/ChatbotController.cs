using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Jifas.Assistant.Data;

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
        private readonly JifasAssistantDbContext _context;

        public ChatbotController(JifasAssistantDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Health check endpoint
        /// </summary>
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", service = "ChatbotService" });
        }

        /// <summary>
        /// Process a chat message
        /// NOTE: This endpoint is under migration to .NET 10
        /// </summary>
        [HttpPost("conversation")]
        public async Task<IActionResult> Conversation([FromBody] dynamic request)
        {
            return Ok(new 
            { 
                message = "Chat service is under maintenance",
                status = "upgrade_in_progress"
            });
        }
    }
}
