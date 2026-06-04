using System;
using System.Linq;
using System.Threading.Tasks;
using Jifas.Assistant.Services;
using jifas_assistant.DAL.Models;
using Microsoft.AspNetCore.Mvc;

namespace Jifas.Assistant.Controllers;

/// <summary>
/// REST API for the AI monitoring dashboard.
/// All DateTime values are explicitly marked as UTC (Kind=Utc) so JSON serializer
/// appends the 'Z' suffix and the browser parses them correctly in any timezone.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MonitoringController : ControllerBase
{
    private readonly IMonitoringService _monitoring;

    public MonitoringController(IMonitoringService monitoring)
    {
        _monitoring = monitoring;
    }

    /// <summary>Aggregated stats for the last N minutes (default 60).</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats([FromQuery] int minutes = 60)
    {
        var stats = await _monitoring.GetStatsAsync(minutes);
        // Force LastCallAt Kind = Utc so JSON output has 'Z'
        if (stats.LastCallAt.HasValue && stats.LastCallAt.Value.Kind == DateTimeKind.Unspecified)
        {
            stats = stats with { LastCallAt = DateTime.SpecifyKind(stats.LastCallAt.Value, DateTimeKind.Utc) };
        }
        return Ok(stats);
    }

    /// <summary>Recent AI call log entries (newest first).</summary>
    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs([FromQuery] int count = 100)
    {
        var logs = await _monitoring.GetRecentLogsAsync(count);
        return Ok(logs.Select(NormalizeLogDates));
    }

    /// <summary>Per-minute time series for the last N minutes.</summary>
    [HttpGet("timeseries")]
    public async Task<IActionResult> GetTimeSeries([FromQuery] int minutes = 60)
    {
        var series = await _monitoring.GetTimeSeriesAsync(minutes);
        return Ok(series.Select(p => p with
        {
            Minute = p.Minute.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(p.Minute, DateTimeKind.Utc)
                : p.Minute
        }));
    }

    /// <summary>Quality stats from persisted chat responses.</summary>
    [HttpGet("quality")]
    public async Task<IActionResult> GetQuality([FromQuery] int minutes = 60, [FromQuery] int slowThresholdMs = 30000)
    {
        var stats = await _monitoring.GetQualityStatsAsync(minutes, slowThresholdMs);
        if (stats.LastResponseAt.HasValue && stats.LastResponseAt.Value.Kind == DateTimeKind.Unspecified)
        {
            stats = stats with { LastResponseAt = DateTime.SpecifyKind(stats.LastResponseAt.Value, DateTimeKind.Utc) };
        }

        return Ok(stats);
    }

    /// <summary>Semua data dashboard dalam satu request agar initial load lebih ringan.</summary>
    [HttpGet("all")]
    public async Task<IActionResult> GetAll([FromQuery] int minutes = 60)
    {
        var statsRaw   = await _monitoring.GetStatsAsync(minutes);
        var logsRaw    = await _monitoring.GetRecentLogsAsync(50);
        var seriesRaw  = await _monitoring.GetTimeSeriesAsync(minutes);
        var qualityRaw = await _monitoring.GetQualityStatsAsync(minutes);

        // Ensure all dates have UTC Kind so JSON serializer emits 'Z'
        var stats = statsRaw.LastCallAt.HasValue && statsRaw.LastCallAt.Value.Kind == DateTimeKind.Unspecified
            ? statsRaw with { LastCallAt = DateTime.SpecifyKind(statsRaw.LastCallAt.Value, DateTimeKind.Utc) }
            : statsRaw;

        var logs = logsRaw.Select(NormalizeLogDates);

        var timeSeries = seriesRaw.Select(p => p with
        {
            Minute = p.Minute.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(p.Minute, DateTimeKind.Utc)
                : p.Minute
        });

        var quality = qualityRaw.LastResponseAt.HasValue && qualityRaw.LastResponseAt.Value.Kind == DateTimeKind.Unspecified
            ? qualityRaw with { LastResponseAt = DateTime.SpecifyKind(qualityRaw.LastResponseAt.Value, DateTimeKind.Utc) }
            : qualityRaw;

        return Ok(new { stats, logs, timeSeries, quality });
    }

    // Helper normalisasi tanggal agar browser selalu membaca waktu sebagai UTC.

    private static AiUsageLog NormalizeLogDates(AiUsageLog l)
    {
        if (l.CreatedAt.Kind == DateTimeKind.Unspecified)
            l.CreatedAt = DateTime.SpecifyKind(l.CreatedAt, DateTimeKind.Utc);
        return l;
    }
}
