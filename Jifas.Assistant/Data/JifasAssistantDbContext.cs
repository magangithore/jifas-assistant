using Microsoft.EntityFrameworkCore;
using Jifas.Assistant.Data.Models;

namespace Jifas.Assistant.Data
{
    /// <summary>
    /// Entity Framework Core DbContext for JIFAS AI Assistant
    /// </summary>
    public class JifasAssistantDbContext : DbContext
    {
        public JifasAssistantDbContext(DbContextOptions<JifasAssistantDbContext> options)
            : base(options)
        {
        }

        public DbSet<Chat> Chats { get; set; }
        public DbSet<KnowledgeBaseDocument> KnowledgeBaseDocuments { get; set; }
        public DbSet<UserFeedback> UserFeedbacks { get; set; }
        public DbSet<Metric> Metrics { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Chat entity
            modelBuilder.Entity<Chat>()
                .HasKey(x => x.Id);

            modelBuilder.Entity<Chat>()
                .Property(x => x.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            // Configure KnowledgeBaseDocument entity
            modelBuilder.Entity<KnowledgeBaseDocument>()
                .HasKey(x => x.Id);

            modelBuilder.Entity<KnowledgeBaseDocument>()
                .Property(x => x.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            modelBuilder.Entity<KnowledgeBaseDocument>()
                .HasIndex(x => x.Category);

            modelBuilder.Entity<KnowledgeBaseDocument>()
                .HasIndex(x => x.Title);

            // Configure UserFeedback entity
            modelBuilder.Entity<UserFeedback>()
                .HasKey(x => x.Id);

            modelBuilder.Entity<UserFeedback>()
                .Property(x => x.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            modelBuilder.Entity<UserFeedback>()
                .HasOne(x => x.Chat)
                .WithMany()
                .HasForeignKey(x => x.ChatId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure Metric entity
            modelBuilder.Entity<Metric>()
                .HasKey(x => x.Id);

            modelBuilder.Entity<Metric>()
                .Property(x => x.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            modelBuilder.Entity<Metric>()
                .HasIndex(x => x.MetricType);

            modelBuilder.Entity<Metric>()
                .HasIndex(x => x.Category);
        }
    }
}
