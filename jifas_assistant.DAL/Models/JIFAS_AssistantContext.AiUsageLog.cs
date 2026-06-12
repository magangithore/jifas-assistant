using Microsoft.EntityFrameworkCore;

namespace jifas_assistant.DAL.Models;

public partial class JIFAS_AssistantContext
{
    public DbSet<AiUsageLog> AiUsageLogs { get; set; } = null!;
}
