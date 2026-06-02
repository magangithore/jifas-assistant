using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using jifas_assistant.DAL.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Jifas.Assistant.Hubs;

namespace Jifas.Assistant.Services;

/// <summary>
/// Persists every AI call metric and broadcasts a real-time
/// event via SignalR so the monitoring dashboard updates instantly.
/// </summary>
public class MonitoringService : IMonitoringService
{
    private readonly JIFAS_AssistantContext _db;
    private readonly IHubContext<MonitoringHub> _hub;
    private readonly ILoggerService _logger;

    public MonitoringService(
        JIFAS_AssistantContext db,
        IHubContext<MonitoringHub> hub,
        ILoggerService logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    // ─── Record ─────────────────────────────────────────────────────────────

    public async Task RecordAsync(AiCallMetrics m)
    {
        try
        {
            var tokensPerSec = m.EvalDurationMs > 0
                ? m.CompletionTokens / (m.EvalDurationMs / 1000.0)
                : 0;

            var log = new AiUsageLog
            {
                UserId               = m.UserId,
                SessionId            = m.SessionId,
                ActiveModule         = m.ActiveModule,
                Model                = m.Model,
                CallType             = m.CallType,
                PromptTokens         = m.PromptTokens,
                CompletionTokens     = m.CompletionTokens,
                TotalTokens          = m.PromptTokens + m.CompletionTokens,
                TotalDurationMs      = m.TotalDurationMs,
                LoadDurationMs       = m.LoadDurationMs,
                PromptEvalDurationMs = m.PromptEvalDurationMs,
                EvalDurationMs       = m.EvalDurationMs,
                TokensPerSecond      = tokensPerSec,
                PromptLengthChars    = m.PromptLengthChars,
                ResponseLengthChars  = m.ResponseLengthChars,
                ConfidenceScore      = m.ConfidenceScore,
                IsError              = m.IsError,
                ErrorMessage         = m.ErrorMessage,
                CreatedAt            = m.CreatedAt
            };

            _db.AiUsageLogs.Add(log);
            await _db.SaveChangesAsync();

            // Broadcast to all connected dashboard clients
            await _hub.Clients.All.SendAsync("NewMetric", new
            {
                id                   = log.Id,
                userId               = log.UserId,
                activeModule         = log.ActiveModule,
                model                = log.Model,
                callType             = log.CallType,
                promptTokens         = log.PromptTokens,
                completionTokens     = log.CompletionTokens,
                totalTokens          = log.TotalTokens,
                totalDurationMs      = log.TotalDurationMs,
                loadDurationMs       = log.LoadDurationMs,
                promptEvalDurationMs = log.PromptEvalDurationMs,
                evalDurationMs       = log.EvalDurationMs,
                tokensPerSecond      = Math.Round(tokensPerSec, 2),
                responseLengthChars  = log.ResponseLengthChars,
                promptLengthChars    = log.PromptLengthChars,
                isError              = log.IsError,
                errorMessage         = log.ErrorMessage,
                // Always emit with 'Z' so browser JS parses as UTC and converts to WIB correctly
                createdAt            = DateTime.SpecifyKind(log.CreatedAt, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ")
            });

            _logger.LogDebug("[Monitoring] Recorded: {0}t prompt, {1}t completion, {2}ms total — {3:F1} t/s",
                log.PromptTokens, log.CompletionTokens, log.TotalDurationMs, tokensPerSec);
        }
        catch (Exception ex)
        {
            _logger.LogError("[Monitoring] Failed to record metrics: {0}", ex, new object[] { ex.Message });
        }
    }

    // ─── Stats ──────────────────────────────────────────────────────────────

    public async Task<MonitoringStats> GetStatsAsync(int lastMinutes = 60)
    {
        var since = DateTime.UtcNow.AddMinutes(-lastMinutes);
        var logs  = await _db.AiUsageLogs
            .Where(l => l.CreatedAt >= since)
            .ToListAsync();

        if (logs.Count == 0)
            return new MonitoringStats();

        var errors = logs.Count(l => l.IsError);
        return new MonitoringStats
        {
            TotalCalls              = logs.Count,
            ErrorCalls              = errors,
            ErrorRate               = Math.Round(errors / (double)logs.Count * 100, 1),
            AvgTotalDurationMs      = Math.Round(logs.Average(l => l.TotalDurationMs), 0),
            AvgPromptTokens         = Math.Round(logs.Average(l => l.PromptTokens), 1),
            AvgCompletionTokens     = Math.Round(logs.Average(l => l.CompletionTokens), 1),
            AvgTokensPerSecond      = Math.Round(logs.Where(l => l.TokensPerSecond > 0).DefaultIfEmpty().Average(l => l?.TokensPerSecond ?? 0), 2),
            TotalPromptTokens       = logs.Sum(l => (long)l.PromptTokens),
            TotalCompletionTokens   = logs.Sum(l => (long)l.CompletionTokens),
            AvgResponseLengthChars  = Math.Round(logs.Average(l => l.ResponseLengthChars), 0),
            LastCallAt              = logs.Max(l => l.CreatedAt)
        };
    }

    // ─── Recent logs ────────────────────────────────────────────────────────

    public async Task<List<AiUsageLog>> GetRecentLogsAsync(int count = 100)
    {
        return await _db.AiUsageLogs
            .OrderByDescending(l => l.CreatedAt)
            .Take(count)
            .ToListAsync();
    }

    // ─── Time series ────────────────────────────────────────────────────────

    public async Task<List<TimeSeriesPoint>> GetTimeSeriesAsync(int lastMinutes = 60)
    {
        var since = DateTime.UtcNow.AddMinutes(-lastMinutes);
        var logs  = await _db.AiUsageLogs
            .Where(l => l.CreatedAt >= since)
            .ToListAsync();

        return logs
            .GroupBy(l => new DateTime(l.CreatedAt.Year, l.CreatedAt.Month, l.CreatedAt.Day,
                                       l.CreatedAt.Hour, l.CreatedAt.Minute, 0, DateTimeKind.Utc))
            .OrderBy(g => g.Key)
            .Select(g => new TimeSeriesPoint(
                g.Key,
                g.Count(),
                Math.Round(g.Average(l => l.TotalDurationMs), 0),
                g.Sum(l => l.TotalTokens)
            ))
            .ToList();
    }

    public async Task<QualityMonitoringStats> GetQualityStatsAsync(int lastMinutes = 60, int slowThresholdMs = 30000)
    {
        var since = DateTime.UtcNow.AddMinutes(-lastMinutes);
        var chats = await _db.ChatHistories
            .AsNoTracking()
            .Where(c => c.CreatedAt >= since)
            .ToListAsync();

        if (chats.Count == 0)
            return new QualityMonitoringStats();

        var successful = chats.Count(c => c.Success);
        var kbHits = chats.Count(c => c.IsFromKnowledgeBase);
        var lowConfidence = chats.Count(c => (c.ConfidenceScore ?? 0) > 0 && c.ConfidenceScore <= 0.5);
        var confidenceSamples = chats
            .Where(c => c.ConfidenceScore.HasValue)
            .Select(c => c.ConfidenceScore!.Value)
            .ToList();

        return new QualityMonitoringStats
        {
            TotalResponses = chats.Count,
            SuccessfulResponses = successful,
            KnowledgeBaseResponses = kbHits,
            FallbackResponses = chats.Count - kbHits,
            LowConfidenceResponses = lowConfidence,
            SuccessRate = Math.Round(successful / (double)chats.Count * 100, 1),
            KnowledgeBaseHitRate = Math.Round(kbHits / (double)chats.Count * 100, 1),
            LowConfidenceRate = Math.Round(lowConfidence / (double)chats.Count * 100, 1),
            AvgConfidenceScore = confidenceSamples.Count == 0 ? 0 : Math.Round(confidenceSamples.Average(), 3),
            AvgResponseTimeMs = Math.Round(chats.Average(c => c.ResponseTimeMs), 0),
            SlowResponses = chats.LongCount(c => c.ResponseTimeMs >= slowThresholdMs),
            LastResponseAt = chats.Max(c => c.CreatedAt)
        };
    }
}
