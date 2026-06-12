using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace jifas_assistant.DAL.Models;

/// <summary>
/// Persists every AI generation call: token counts, timing, quality metrics.
/// Used by the real-time monitoring dashboard.
/// </summary>
[Index(nameof(CreatedAt), Name = "IX_AiUsageLog_CreatedAt")]
[Index(nameof(UserId), Name = "IX_AiUsageLog_UserId")]
[Index(nameof(Model), Name = "IX_AiUsageLog_Model")]
public class AiUsageLog
{
    [Key]
    public long Id { get; set; }

    // ── Identity ──────────────────────────────────────────────
    [StringLength(200)]
    public string? UserId { get; set; }

    [StringLength(200)]
    public string? SessionId { get; set; }

    [StringLength(100)]
    public string? ActiveModule { get; set; }

    // ── Model info ────────────────────────────────────────────
    [Required]
    [StringLength(100)]
    public string Model { get; set; } = string.Empty;

    [StringLength(50)]
    public string? CallType { get; set; }   // "chat" | "suggestions" | "scope_check"

    // ── Token counts (from Ollama eval_count fields) ──────────
    public int PromptTokens { get; set; }       // prompt_eval_count
    public int CompletionTokens { get; set; }   // eval_count
    public int TotalTokens { get; set; }        // sum

    // ── Timing (ms) ───────────────────────────────────────────
    public long TotalDurationMs { get; set; }   // total_duration / 1_000_000
    public long LoadDurationMs { get; set; }    // load_duration  / 1_000_000
    public long PromptEvalDurationMs { get; set; }  // prompt_eval_duration / 1_000_000
    public long EvalDurationMs { get; set; }    // eval_duration  / 1_000_000

    // ── Throughput ────────────────────────────────────────────
    [Column(TypeName = "float")]
    public double TokensPerSecond { get; set; }  // eval_count / (eval_duration_ns / 1e9)

    // ── Quality signals ───────────────────────────────────────
    public int ResponseLengthChars { get; set; }
    public int PromptLengthChars { get; set; }

    [Column(TypeName = "float")]
    public double? ConfidenceScore { get; set; }

    public bool IsError { get; set; }

    [StringLength(500)]
    public string? ErrorMessage { get; set; }

    // ── Timestamp ─────────────────────────────────────────────
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
