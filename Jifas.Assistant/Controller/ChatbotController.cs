using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jifas.Assistant.Controller
{
    /// <summary>
    /// Controller untuk menangani endpoint chatbot
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class ChatbotController : ControllerBase
    {
        /// <summary>
        /// Mengambil pesan sapaan dari chatbot
        /// </summary>
        /// <returns>Pesan sapaan dari chatbot</returns>
        /// <response code="200">Berhasil mendapatkan pesan sapaan</response>
        [HttpGet("hello")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult Index()
        {
            return Ok("Hello from ChatbotController!");
        }
    }
}
