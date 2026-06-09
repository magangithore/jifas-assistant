using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace jifas_assistant.DAL.Models;

/// <summary>
/// Audit trail untuk setiap perubahan kandidat learning.
/// </summary>
[Index(nameof(CandidateId), nameof(CreatedAt), Name = "IX_LearningCandidateAuditLogs_CandidateId_CreatedAt")]
public class LearningCandidateAuditLog
{
    [Key]
    public int Id { get; set; }

    public int CandidateId { get; set; }

    [StringLength(80)]
    public string Action { get; set; } = string.Empty;

    [StringLength(100)]
    public string Actor { get; set; } = string.Empty;

    [StringLength(80)]
    public string OldStatus { get; set; } = string.Empty;

    [StringLength(80)]
    public string NewStatus { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
