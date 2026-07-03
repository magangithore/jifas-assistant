using System.Reflection;
using Jifas.Assistant.Models;
using Jifas.Assistant.Services;
using jifas_assistant.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Jifas.Assistant.Tests;

/// <summary>
/// Unit test untuk authorization SelectedDocumentId.
/// Aturan: dokumen harus exists + IsActive (KB tidak punya kolom ownership company/role).
/// Testing dilakukan langsung pada method static ValidateSelectedDocumentIdAsync.
/// </summary>
public class SelectedDocumentIdAuthorizationTests
{
    private sealed class NullLoggerService : ILoggerService
    {
        public void LogInformation(string message, params object[] args) { }
        public void LogWarning(string message, params object[] args) { }
        public void LogError(string message, Exception? ex = null, params object[] args) { }
        public void LogDebug(string message, params object[] args) { }
        public void LogInformationWithCorrelation(string correlationId, string message, params object[] args) { }
        public void LogWarningWithCorrelation(string correlationId, string message, params object[] args) { }
        public void LogErrorWithCorrelation(string correlationId, string message, Exception? ex = null, params object[] args) { }
        public void LogAudit(string userId, string action, string details, string? correlationId = null) { }
        public void LogPerformance(string operation, long milliseconds, string? correlationId = null) { }
    }

    // =====================================================================
    // TEST 1: null/empty -> authorization ditolak
    // =====================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NullAtauKosong_AuthorizationDitolak(string? docId)
    {
        var factory = new SeededDbContextFactory();
        var logger = new NullLoggerService();

        var result = await ChatService.ValidateSelectedDocumentIdAsync(
            factory, docId, "user-001", "KI", logger);

        Assert.False(result);
    }

    // =====================================================================
    // TEST 2: format tidak valid (non-integer) -> authorization ditolak
    // =====================================================================

    [Theory]
    [InlineData("abc")]
    [InlineData("12.34")]
    [InlineData("-5")]
    [InlineData("1;DROP TABLE")]
    [InlineData("1 OR 1=1")]
    public async Task FormatTidakValid_AuthorizationDitolak(string docId)
    {
        var factory = new SeededDbContextFactory();
        var logger = new NullLoggerService();

        var result = await ChatService.ValidateSelectedDocumentIdAsync(
            factory, docId, "user-001", "KI", logger);

        Assert.False(result);
    }

    // =====================================================================
    // TEST 3: dokumen ADA dan aktif -> authorization DITERIMA
    // =====================================================================

    [Fact]
    public async Task DokumenAktif_AuthorizationDiterima()
    {
        // Factory implements IDisposable — dispose factory, bukan context
        using var factory = new UniqueSeededDbContextFactory();
        var logger = new NullLoggerService();

        var result = await ChatService.ValidateSelectedDocumentIdAsync(factory, "1", "user-001", "KI", logger);
        Assert.True(result, "Dokumen 1 aktif, authorization harus diterima");

        result = await ChatService.ValidateSelectedDocumentIdAsync(factory, "2", "user-001", "KI", logger);
        Assert.True(result, "Dokumen 2 aktif, authorization harus diterima");

        result = await ChatService.ValidateSelectedDocumentIdAsync(factory, "3", "user-001", "KI", logger);
        Assert.True(result, "Dokumen 3 aktif, authorization harus diterima");
    }

    // =====================================================================
    // TEST 4: dokumen ADA tapi TIDAK aktif -> authorization ditolak
    // =====================================================================

    [Fact]
    public async Task DokumenTidakAktif_AuthorizationDitolak()
    {
        var factory = new SeededDbContextFactory();
        var logger = new NullLoggerService();

        // Dokumen 4 ada tapi IsActive = false
        var result = await ChatService.ValidateSelectedDocumentIdAsync(
            factory, "4", "user-002", "FINA", logger);

        Assert.False(result);
    }

    // =====================================================================
    // TEST 5: dokumen tidak ada di DB -> authorization ditolak
    // (tidak bisa bedakan "tidak ada" vs "tidak berhak" -> sama)
    // =====================================================================

    [Theory]
    [InlineData("999")]
    [InlineData("0")]
    [InlineData("100")]
    public async Task DokumenTidakAda_AuthorizationDitolak(string docId)
    {
        var factory = new SeededDbContextFactory();
        var logger = new NullLoggerService();

        var result = await ChatService.ValidateSelectedDocumentIdAsync(
            factory, docId, "user-002", "FINA", logger);

        Assert.False(result);
    }

    // =====================================================================
    // TEST 6: DB error -> fail-open: authorization ditolak (aman, tidak throw)
    // =====================================================================

    [Fact]
    public async Task DbError_FailOpen_TidakLematpar()
    {
        var factory = new DbErrorFactory();
        var logger = new NullLoggerService();

        var exception = await Record.ExceptionAsync(() =>
            ChatService.ValidateSelectedDocumentIdAsync(
                factory, "5", "user-001", "KI", logger));

        Assert.Null(exception);
    }

    // =====================================================================
    // FACTORY & CONTEXT
    // Seed sekali per test session via static lock + shared InMemory DB name.
    // =====================================================================

    private sealed class SeededDbContextFactory : IDbContextFactory<JIFAS_AssistantContext>
    {
        // Nama statis: semua instance factory share 1 InMemory DB
        private static readonly string _dbName = "SelectedDocAuthTestDb";
        private static readonly DbContextOptions<JIFAS_AssistantContext> _options;
        private static bool _seeded;

        static SeededDbContextFactory()
        {
            _options = new DbContextOptionsBuilder<JIFAS_AssistantContext>()
                .UseInMemoryDatabase(databaseName: _dbName)
                .Options;
        }

        public static JIFAS_AssistantContext CreateDiagContext() => CreateCore();
        public JIFAS_AssistantContext CreateDbContext() => CreateCore();
        public Task<JIFAS_AssistantContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(CreateCore());

        private static JIFAS_AssistantContext CreateCore()
        {
            var ctx = new JIFAS_AssistantContext(_options);
            if (!_seeded)
            {
                ctx.KnowledgeBaseDocuments.AddRange(
                    new KnowledgeBaseDocuments { Id = 1, IsActive = true },
                    new KnowledgeBaseDocuments { Id = 2, IsActive = true },
                    new KnowledgeBaseDocuments { Id = 3, IsActive = true },
                    new KnowledgeBaseDocuments { Id = 4, IsActive = false });
                ctx.SaveChanges();
                _seeded = true;
            }
            return ctx;
        }
    }

    /// <summary>
    /// Factory dengan context baru per call. Semua instance share InMemory DB singleton per name.
    /// Data yang di-seed di konteks pertama persist ke semua konteks berikutnya (InMemory singleton).
    /// </summary>
    private sealed class UniqueSeededDbContextFactory : IDbContextFactory<JIFAS_AssistantContext>, IDisposable
    {
        private readonly string _dbName;
        private readonly HashSet<int> _seededIds = new();
        private readonly object _seedLock = new();
        private bool _disposed;

        public UniqueSeededDbContextFactory()
        {
            _dbName = $"DocAuth_{Guid.NewGuid():N}";
        }

        public JIFAS_AssistantContext CreateDbContext() => CreateCore();
        public Task<JIFAS_AssistantContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(CreateCore());

        private JIFAS_AssistantContext CreateCore()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UniqueSeededDbContextFactory));

            var options = new DbContextOptionsBuilder<JIFAS_AssistantContext>()
                .UseInMemoryDatabase(databaseName: _dbName)
                .Options;
            var ctx = new InMemoryAwareContext(options);

            lock (_seedLock)
            {
                if (_seededIds.Count == 0)
                {
                    ctx.KnowledgeBaseDocuments.AddRange(
                        new KnowledgeBaseDocuments { Id = 1, Title = "Doc1", Content = "C1", IsActive = true },
                        new KnowledgeBaseDocuments { Id = 2, Title = "Doc2", Content = "C2", IsActive = true },
                        new KnowledgeBaseDocuments { Id = 3, Title = "Doc3", Content = "C3", IsActive = true },
                        new KnowledgeBaseDocuments { Id = 4, Title = "Doc4", Content = "C4", IsActive = false });
                    ctx.SaveChanges();
                    _seededIds.Add(1); _seededIds.Add(2); _seededIds.Add(3); _seededIds.Add(4);
                }
            }
            return ctx;
        }

        public void Dispose() => _disposed = true;
    }

    /// <summary>
    /// Context yang mengabaikan property Vector (pgvector) karena tidak didukung InMemory.
    /// </summary>
    private sealed class InMemoryAwareContext : JIFAS_AssistantContext
    {
        public InMemoryAwareContext(DbContextOptions<JIFAS_AssistantContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // Ignore Vector property karena pgvector tidak didukung InMemory
            modelBuilder.Entity<KnowledgeBaseChunks>()
                .Ignore(c => c.EmbeddingVector);
        }
    }

    private sealed class DbErrorFactory : IDbContextFactory<JIFAS_AssistantContext>
    {
        public JIFAS_AssistantContext CreateDbContext() =>
            throw new InvalidOperationException("Simulated DB connection error");
        public Task<JIFAS_AssistantContext> CreateDbContextAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Simulated DB connection error");
    }
}
