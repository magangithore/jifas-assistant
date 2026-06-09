using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace jifas_assistant.DAL.Models;

/// <summary>
/// Kandidat knowledge yang lahir dari jawaban chatbot dan menunggu kurasi admin.
/// </summary>
[Index(nameof(QuestionHash), Name = "IX_LearningCandidates_QuestionHash")]
[Index(nameof(Status), nameof(UpdatedAt), Name = "IX_LearningCandidates_Status_UpdatedAt")]
[Index(nameof(SourceChatHistoryId), Name = "IX_LearningCandidates_SourceChatHistoryId")]
public class LearningCandidate
{
    [Key]
    public int Id { get; set; }

    public int? SourceChatHistoryId { get; set; }

    [Required]
    [StringLength(64)]
    public string QuestionHash { get; set; } = string.Empty;

    [Required]
    public string OriginalQuestion { get; set; } = string.Empty;

    [Required]
    public string OriginalAnswer { get; set; } = string.Empty;

    public string EditedQuestion { get; set; } = string.Empty;

    public string EditedAnswer { get; set; } = string.Empty;

    [StringLength(80)]
    public string Status { get; set; } = "NeedsEdit";

    [StringLength(100)]
    public string Category { get; set; } = "AI Learning";

    [StringLength(500)]
    public string Tags { get; set; } = "ai-learning,approved,reviewed";

    [StringLength(200)]
    public string Source { get; set; } = string.Empty;

    public string SourceDocumentIds { get; set; } = string.Empty;

    [Column(TypeName = "float")]
    public double? ConfidenceScore { get; set; }

    [Column(TypeName = "float")]
    public double? QualityScore { get; set; }

    [StringLength(1000)]
    public string CandidateReason { get; set; } = string.Empty;

    [StringLength(1000)]
    public string Flags { get; set; } = string.Empty;

    public bool ContainsSensitiveData { get; set; }

    [StringLength(500)]
    public string SensitiveReason { get; set; } = string.Empty;

    public int Frequency { get; set; } = 1;

    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public string ReviewNotes { get; set; } = string.Empty;

    [StringLength(100)]
    public string ReviewedBy { get; set; } = string.Empty;

    public DateTime? ReviewedAt { get; set; }

    public int? PublishedDocumentId { get; set; }

    public DateTime? PublishedAt { get; set; }

    [StringLength(100)]
    public string PublishedBy { get; set; } = string.Empty;

    public string PublishError { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
