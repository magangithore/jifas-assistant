using System;
using System.Threading;
using System.Threading.Tasks;
using Jifas.Assistant.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jifas.Assistant.Controllers;

/// <summary>
/// API admin untuk kurasi AI Learning sebelum knowledge dipublish ke KB resmi.
/// </summary>
[ApiController]
[Authorize(Policy = "KnowledgeBaseAdmin")]
[Route("api/learning")]
public class LearningController : ControllerBase
{
    private readonly IAiLearningService _learning;

    public LearningController(IAiLearningService learning)
    {
        _learning = learning;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken cancellationToken)
    {
        return Ok(await _learning.GetStatsAsync(cancellationToken));
    }

    [HttpGet("candidates")]
    public async Task<IActionResult> GetCandidates(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        var result = await _learning.GetCandidatesAsync(new LearningCandidateQuery
        {
            Status = status,
            Search = search,
            Page = page,
            PageSize = pageSize
        }, cancellationToken);

        return Ok(result);
    }

    [HttpGet("candidates/{id:int}")]
    public async Task<IActionResult> GetCandidate(int id, CancellationToken cancellationToken)
    {
        var candidate = await _learning.GetCandidateAsync(id, cancellationToken);
        return candidate == null ? NotFound() : Ok(candidate);
    }

    [HttpPut("candidates/{id:int}/edit")]
    public async Task<IActionResult> EditCandidate(int id, [FromBody] LearningCandidateEditRequest request, CancellationToken cancellationToken)
    {
        if (request == null)
            return BadRequest(new { error = "Payload edit wajib diisi." });

        var updated = await _learning.UpdateCandidateAsync(id, request, GetActor(), cancellationToken);
        return updated == null ? NotFound() : Ok(updated);
    }

    [HttpPost("candidates/{id:int}/ready")]
    public async Task<IActionResult> MarkReady(int id, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _learning.MarkReadyAsync(id, GetActor(), cancellationToken);
            return updated == null ? NotFound() : Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("candidates/{id:int}/archive")]
    public async Task<IActionResult> Archive(int id, [FromBody] LearningArchiveRequest? request, CancellationToken cancellationToken)
    {
        var updated = await _learning.ArchiveAsync(id, GetActor(), request?.Notes, cancellationToken);
        return updated == null ? NotFound() : Ok(updated);
    }

    [HttpPost("collect/run")]
    public async Task<IActionResult> RunCollector([FromBody] LearningCollectRequest? request, CancellationToken cancellationToken)
    {
        var result = await _learning.CollectCandidatesAsync(request?.ScanLimit ?? 250, cancellationToken);
        return Ok(result);
    }

    [HttpPost("publish/run")]
    public async Task<IActionResult> RunPublisher([FromBody] LearningPublishRequest? request, CancellationToken cancellationToken)
    {
        var result = await _learning.PublishReadyAsync(request?.Limit, GetActor(), cancellationToken);
        return Ok(result);
    }

    private string GetActor()
    {
        if (Request.Headers.TryGetValue("X-Admin-User", out var actor) && !string.IsNullOrWhiteSpace(actor))
            return actor.ToString();

        return User?.Identity?.Name ?? "admin";
    }
}

public sealed class LearningArchiveRequest
{
    public string? Notes { get; set; }
}

public sealed class LearningCollectRequest
{
    public int ScanLimit { get; set; } = 250;
}

public sealed class LearningPublishRequest
{
    public int? Limit { get; set; }
}
