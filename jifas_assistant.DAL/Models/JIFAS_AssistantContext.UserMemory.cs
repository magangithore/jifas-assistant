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
            entity.Property(e => e.FirstSeenAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.LastSeenAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(getutcdate())");
        });
    }
}
