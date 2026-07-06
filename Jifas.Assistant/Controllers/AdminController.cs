using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jifas.Assistant.Controllers
{
    /// <summary>
    /// Admin dashboard controller untuk monitoring dan management.
    /// Protected dengan cookie authentication + KnowledgeBaseAdmin policy.
    /// </summary>
    [Authorize(Policy = "KnowledgeBaseAdmin")]
    [Route("admin")]
    public class AdminController : Controller
    {
        /// <summary>
        /// Dashboard monitoring utama - menampilkan realtime metrics, charts, dan logs.
        /// </summary>
        [HttpGet("monitoring")]
        public IActionResult Monitoring()
        {
            return View();
        }

        /// <summary>
        /// Dashboard AI Learning - manage kandidat, approval, dan publish knowledge.
        /// </summary>
        [HttpGet("learning")]
        public IActionResult Learning()
        {
            return View();
        }
    }
}
