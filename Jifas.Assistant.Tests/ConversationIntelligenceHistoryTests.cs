using Jifas.Assistant.Services;
using jifas_assistant.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Jifas.Assistant.Tests;

/// <summary>
/// Unit tests untuk fix: consolidate GetSessionHistoryAsync call.
/// </summary>
public class ConversationIntelligenceHistoryTests
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

    private static JIFAS_AssistantContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<JIFAS_AssistantContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new JIFAS_AssistantContext(options);
    }

    private sealed class FakeCacheService : ICacheService
    {
        public T? Get<T>(string key) => default;
        public void Set<T>(string key, T value, int minutes) { }
        public void Remove(string key) { }
        public bool Exists(string key) => false;
        public void Clear() { }
    }

    private static List<ChatHistory> CreateSampleHistory(int count, string sessionId = "test-session")
    {
        var list = new List<ChatHistory>();
        for (int i = 1; i <= count; i++)
        {
            list.Add(new ChatHistory
            {
                Id = i,
                SessionId = sessionId,
                UserId = "user-001",
                UserMessage = $"Pertanyaan #{i}",
                AiResponse = $"Jawaban #{i}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-(count - i)),
                ResponseTimeMs = 1000,
                Success = true
            });
        }
        return list;
    }

    // =====================================================================
    // BAGIAN 1: ComputeRunningSummary — tidak pakai async, terima List<ChatHistory>
    // =====================================================================

    [Fact]
    public void ComputeRunningSummary_Kosong_ReturnsEmpty()
    {
        var service = CreateSut();
        var result = service.ComputeRunningSummary(new List<ChatHistory>(), 15);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ComputeRunningSummary_SatuTurn_ReturnsEmpty()
    {
        var service = CreateSut();
        var result = service.ComputeRunningSummary(CreateSampleHistory(1), 15);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ComputeRunningSummary_Lebih15Turn_SemuaOlderMasukSummary()
    {
        var history = CreateSampleHistory(20);
        var service = CreateSut();
        // 20 turn: 5 older (untuk summary), 15 recent
        var result = service.ComputeRunningSummary(history, 15);
        Assert.NotNull(result);
        Assert.Contains("Pertanyaan #1", result);  // turn tertua masuk summary
        Assert.Contains("Pertanyaan #5", result);  // turn ke-5 masuk (older)
        Assert.DoesNotContain("Pertanyaan #16", result); // recent tidak masuk summary
    }

    [Fact]
    public void ComputeRunningSummary_SessionGreeting_Diabaikan()
    {
        var history = CreateSampleHistory(20);
        history.Insert(0, new ChatHistory
        {
            Id = 0, SessionId = "test", UserId = "user",
            UserMessage = "[Session Greeting]",
            AiResponse = "Selamat datang!",
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        });
        var service = CreateSut();
        var result = service.ComputeRunningSummary(history, 15);
        Assert.NotNull(result);
        Assert.DoesNotContain("[Session Greeting]", result);
    }

    [Fact]
    public void ComputeRunningSummary_BanyakOlder_TopikTerdeteksi()
    {
        var history = new List<ChatHistory>
        {
            new() { Id=1, SessionId="s", UserId="u", UserMessage="Invoice saya kenapa?", AiResponse="Jawab", CreatedAt=DateTime.UtcNow.AddMinutes(-3) },
            new() { Id=2, SessionId="s", UserId="u", UserMessage="Payment gagal terus", AiResponse="Jawab", CreatedAt=DateTime.UtcNow.AddMinutes(-2) },
            new() { Id=3, SessionId="s", UserId="u", UserMessage="Terima kasih", AiResponse="Jawab", CreatedAt=DateTime.UtcNow.AddMinutes(-1) }
        };
        var service = CreateSut();
        // 3 turn - 1 recent = 2 older -> summary aktif
        var result = service.ComputeRunningSummary(history, 1);
        Assert.NotNull(result);
        Assert.Contains("RINGKASAN", result);
    }

    // =====================================================================
    // BAGIAN 1: BuildContextAsync hanya memanggil GetSessionHistoryAsync sekali
    // =====================================================================

    [Fact]
    public async Task BuildContextAsync_GetSessionHistoryAsync_DipanggilTepatSekali()
    {
        int callCount = 0;
        var fakeHistoryService = new FakeHistoryService(Returns: CreateSampleHistory(3), OnGet: () => callCount++);
        var cache = new FakeCacheService();

        var service = new ConversationIntelligenceService(new FakeDbContextFactory(), fakeHistoryService, cache, new NullLoggerService());

        await service.BuildContextAsync("session-001", "user-001", maxTurns: 15);

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task BuildContextAsync_RecentWindow15_TurnTerbaruDiRecentTurns()
    {
        var history = CreateSampleHistory(20);
        var fakeHistoryService = new FakeHistoryService(Returns: history);
        var cache = new FakeCacheService();

        var service = new ConversationIntelligenceService(new FakeDbContextFactory(), fakeHistoryService, cache, new NullLoggerService());

        var result = await service.BuildContextAsync("session-001", "user-001", maxTurns: 15);

        Assert.Equal(15, result.RecentTurns.Count);
        Assert.Equal(5, result.OlderTurnsCount);
    }

    [Fact]
    public async Task BuildContextAsync_Kosong_TidakCrash()
    {
        var fakeHistoryService = new FakeHistoryService(Returns: new List<ChatHistory>());
        var cache = new FakeCacheService();

        var service = new ConversationIntelligenceService(new FakeDbContextFactory(), fakeHistoryService, cache, new NullLoggerService());

        var result = await service.BuildContextAsync("session-001", "user-001", maxTurns: 15);

        Assert.Equal(0, result.RecentTurns.Count);
        Assert.Equal(string.Empty, result.RunningSummary);
    }

    // =====================================================================
    // Fake services
    // =====================================================================

    private sealed class FakeHistoryService : IChatHistoryService
    {
        private readonly List<ChatHistory> _returns;
        private readonly Action? _onGet;

        public FakeHistoryService(List<ChatHistory>? Returns = null, Action? OnGet = null)
        {
            _returns = Returns ?? new List<ChatHistory>();
            _onGet = OnGet;
        }

        public Task<List<ChatHistory>> GetSessionHistoryAsync(
            string sessionId, string? userId, int limit = 50, CancellationToken cancellationToken = default)
        {
            _onGet?.Invoke();
            return Task.FromResult(_returns);
        }

        public Task SaveChatAsync(ChatHistory chatHistory, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<List<ChatHistory>> GetUserHistoryAsync(string userId, int limit = 100, CancellationToken cancellationToken = default) =>
            Task.FromResult(_returns);
    }

    private sealed class FakeDbContextFactory : IDbContextFactory<JIFAS_AssistantContext>
    {
        // _dbFactory tidak dipakai oleh BuildContextAsync / ComputeRunningSummary
        // Factory ini tidak pernah diakses — throw jika ternyata dipanggil.
        public JIFAS_AssistantContext CreateDbContext() =>
            throw new NotSupportedException("FakeDbContextFactory tidak boleh diakses dalam test ini");
        public Task<JIFAS_AssistantContext> CreateDbContextAsync(CancellationToken ct = default) =>
            throw new NotSupportedException("FakeDbContextFactory tidak boleh diakses dalam test ini");
    }

    private static ConversationIntelligenceService CreateSut()
    {
        // _dbFactory tidak dipakai oleh BuildContextAsync / ComputeRunningSummary
        var history = new FakeHistoryService();
        var cache = new FakeCacheService();
        return new ConversationIntelligenceService(new FakeDbContextFactory(), history, cache, new NullLoggerService());
    }
}
