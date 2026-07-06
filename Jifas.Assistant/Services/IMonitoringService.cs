using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using jifas_assistant.DAL.Models;

namespace Jifas.Assistant.Services;

/// <summary>
/// Monitoring entry created for each Ollama API call.
/// </summary>
public record AiCallMetrics
{
    public string? UserId { get; init; }
    public string? SessionId { get; init; }
    public string? ActiveModule { get; init; }
    public string Model { get; init; } = string.Empty;
    public string CallType { get; init; } = "chat";

    // Token counts
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }

    // Timing (ms)
    // End-to-end: from ChatService stopwatch start to response ready (all routes)
    public long TotalDurationMs { get; init; }
    // Ollama API wall-clock: preprocessing + inference + postprocessing (0 for cache/non-LLM)
    public long AiDurationMs { get; init; }
    public long LoadDurationMs { get; init; }
    public long PromptEvalDurationMs { get; init; }
    public long EvalDurationMs { get; init; }

    // Quality
    public int PromptLengthChars { get; init; }
    public int ResponseLengthChars { get; init; }
    public double? ConfidenceScore { get; init; }
    public bool IsError { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Aggregated stats returned to the dashboard.
/// </summary>
public record MonitoringStats
{
    public long TotalCalls { get; init; }
    public long ErrorCalls { get; init; }
    public double ErrorRate { get; init; }
    public double AvgTotalDurationMs { get; init; }
    public double AvgAiDurationMs { get; init; }
    public double AvgPromptTokens { get; init; }
    public double AvgCompletionTokens { get; init; }
    public double AvgTokensPerSecond { get; init; }
    public long TotalPromptTokens { get; init; }
    public long TotalCompletionTokens { get; init; }
    public double AvgResponseLengthChars { get; init; }
    public DateTime? LastCallAt { get; init; }
    /// <summary>Total history saves berhasil (in-memory counter, reset saat app restart).</summary>
    public long HistorySaveSuccess { get; init; }
    /// <summary>Total history saves gagal (in-memory counter, reset saat app restart).</summary>
    public long HistorySaveFailure { get; init; }
}

public record QualityMonitoringStats
{
    public int TotalResponses { get; init; }
    public int SuccessfulResponses { get; init; }
    public int KnowledgeBaseResponses { get; init; }
    public int FallbackResponses { get; init; }
    public int LowConfidenceResponses { get; init; }
    public double SuccessRate { get; init; }
    public double KnowledgeBaseHitRate { get; init; }
    public double LowConfidenceRate { get; init; }
    public double AvgConfidenceScore { get; init; }
    public double AvgResponseTimeMs { get; init; }
    public long SlowResponses { get; init; }
    public DateTime? LastResponseAt { get; init; }
}

/// <summary>
/// Manages persistence and real-time broadcast of AI usage metrics.
/// </summary>
public interface IMonitoringService
{
    /// <summary>Persist metrics, broadcast to SignalR hub, update in-memory stats.</summary>
    Task RecordAsync(AiCallMetrics metrics);

    /// <summary>Aggregated stats for the last N minutes (default 60).</summary>
    Task<MonitoringStats> GetStatsAsync(int lastMinutes = 60);

    /// <summary>Recent log entries for the history table (newest first).</summary>
    Task<List<AiUsageLog>> GetRecentLogsAsync(int count = 100);

    /// <summary>Per-minute time series for the last N minutes.</summary>
    Task<List<TimeSeriesPoint>> GetTimeSeriesAsync(int lastMinutes = 60);

    /// <summary>Quality stats from persisted chat responses for the last N minutes.</summary>
    Task<QualityMonitoringStats> GetQualityStatsAsync(int lastMinutes = 60, int slowThresholdMs = 30000);

    /// <summary>
    /// Catat hasil save history (sukses atau gagal).
    /// Non-blocking — tidak melempar exception.
    /// </summary>
    void RecordHistorySave(bool success, string? sessionId);
}

public record TimeSeriesPoint(DateTime Minute, int Calls, double AvgDurationMs, double AvgAiDurationMs, int TotalTokens);
