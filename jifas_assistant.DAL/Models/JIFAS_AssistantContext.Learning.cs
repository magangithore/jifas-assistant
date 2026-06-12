using Microsoft.EntityFrameworkCore;

namespace jifas_assistant.DAL.Models;

public partial class JIFAS_AssistantContext
{
    public virtual DbSet<LearningCandidate> LearningCandidates { get; set; } = null!;

    public virtual DbSet<LearningCandidateAuditLog> LearningCandidateAuditLogs { get; set; } = null!;
}
