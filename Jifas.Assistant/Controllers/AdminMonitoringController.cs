using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jifas.Assistant.Controllers;

/// <summary>
/// Halaman dashboard monitoring yang dilindungi otentikasi admin.
/// Route: /admin/monitoring
/// </summary>
[Authorize(Policy = "KnowledgeBaseAdmin")]
public class AdminMonitoringController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
