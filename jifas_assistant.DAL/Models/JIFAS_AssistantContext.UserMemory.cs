using Microsoft.EntityFrameworkCore;

namespace jifas_assistant.DAL.Models;

/// <summary>
/// Partial extension of JIFAS_AssistantContext untuk menambahkan UserMemory
/// tanpa mengubah file auto-generated.
/// </summary>
public partial class JIFAS_AssistantContext
{
    public virtual DbSet<UserMemory> UserMemories { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserMemory>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.UserId)
                  .IsUnique()
                  .HasDatabaseName("IX_UserMemory_UserId");

            entity.Property(e => e.ExpertiseLevel).HasDefaultValue("Beginner");
            entity.Property(e => e.PreferredLanguage).HasDefaultValue("id");
            entity.Property(e => e.TotalSessions).HasDefaultValue(0);
            entity.Property(e => e.TotalQuestions).HasDefaultValue(0);
            entity.Property(e => e.HowToCount).HasDefaultValue(0);
            entity.Property(e => e.TroubleshootingCount).HasDefaultValue(0);
            entity.Property(e => e.AverageConfidenceReceived).HasDefaultValue(0.0);
            entity.Property(e => e.FirstSeenAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.LastSeenAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<AiUsageLog>(entity =>
        {
            entity.ToTable("AiUsageLogs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.Model).HasDefaultValue("qwen3:8b");
        });

        modelBuilder.Entity<LearningCandidate>(entity =>
        {
            entity.ToTable("LearningCandidates");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasDefaultValue("NeedsEdit");
            entity.Property(e => e.Category).HasDefaultValue("AI Learning");
            entity.Property(e => e.Tags).HasDefaultValue("ai-learning,approved,reviewed");
            entity.Property(e => e.Frequency).HasDefaultValue(1);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.LastSeenAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => e.QuestionHash).HasDatabaseName("IX_LearningCandidates_QuestionHash");
            entity.HasIndex(e => new { e.Status, e.UpdatedAt }).HasDatabaseName("IX_LearningCandidates_Status_UpdatedAt");
            entity.HasIndex(e => e.SourceChatHistoryId).HasDatabaseName("IX_LearningCandidates_SourceChatHistoryId");
        });

        modelBuilder.Entity<LearningCandidateAuditLog>(entity =>
        {
            entity.ToTable("LearningCandidateAuditLogs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => new { e.CandidateId, e.CreatedAt }).HasDatabaseName("IX_LearningCandidateAuditLogs_CandidateId_CreatedAt");
        });
    }
}
