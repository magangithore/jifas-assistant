using Jifas.Assistant.Services;
using jifas_assistant.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Jifas.Assistant.Tests;

/// <summary>
/// Unit tests untuk fix: observability SaveHistory (sukses/gagal metrics).
/// </summary>
public class ChatHistoryServiceTests
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

    private sealed class RecordingMonitoringService : IMonitoringService
    {
        public int SuccessCount { get; private set; }
        public int FailureCount { get; private set; }
        public List<(bool Success, string? SessionId)> Calls { get; } = new();

        public void RecordHistorySave(bool success, string? sessionId)
        {
            if (success) SuccessCount++;
            else FailureCount++;
            Calls.Add((success, sessionId));
        }

        // Stub — unused
        public Task RecordAsync(AiCallMetrics m) => Task.CompletedTask;
        public Task<MonitoringStats> GetStatsAsync(int lastMinutes = 60) =>
            Task.FromResult(new MonitoringStats { TotalCalls = 0, ErrorCalls = 0 });
        public Task<List<AiUsageLog>> GetRecentLogsAsync(int count = 100) => Task.FromResult(new List<AiUsageLog>());
        public Task<List<TimeSeriesPoint>> GetTimeSeriesAsync(int lastMinutes = 60) => Task.FromResult(new List<TimeSeriesPoint>());
        public Task<QualityMonitoringStats> GetQualityStatsAsync(int lastMinutes = 60, int slowThresholdMs = 30000) =>
            Task.FromResult(new QualityMonitoringStats { TotalResponses = 0 });
    }

    // =====================================================================
    // BAGIAN 2: SaveHistory sukses -> metric sukses naik
    // =====================================================================

    [Fact]
    public async Task SaveChatAsync_Success_RecordsSuccessMetric()
    {
        var monitoring = new RecordingMonitoringService();
        var factory = new MockDbContextFactory();
        var service = new ChatHistoryService(factory, new NullLoggerService(), monitoring);

        await service.SaveChatAsync(new ChatHistory
        {
            SessionId = "session-success",
            UserId = "user-001",
            UserMessage = "Apa itu JIFAS?",
            AiResponse = "JIFAS adalah sistem...",
            ResponseTimeMs = 500,
            Success = true
        });

        Assert.Equal(1, monitoring.SuccessCount);
        Assert.Equal(0, monitoring.FailureCount);
    }

    [Fact]
    public async Task SaveChatAsync_MultipleSuccess_IncrementCounter()
    {
        var monitoring = new RecordingMonitoringService();
        var factory = new MockDbContextFactory();
        var service = new ChatHistoryService(factory, new NullLoggerService(), monitoring);

        for (int i = 0; i < 5; i++)
        {
            await service.SaveChatAsync(new ChatHistory
            {
                SessionId = $"chs-s-{i}", UserId = "u", UserMessage = "q",
                AiResponse = "a", ResponseTimeMs = 100, Success = true
            });
        }

        Assert.Equal(5, monitoring.SuccessCount);
    }

    // =====================================================================
    // BAGIAN 2: SaveHistory gagal -> metric gagal naik + tidak melempar
    // =====================================================================

    [Fact]
    public async Task SaveChatAsync_DbFailure_RecordsFailureMetric()
    {
        // Fake factory yang melempar exception
        var fakeFactory = new FakeFailingDbContextFactory("DB connection failed");
        var monitoring = new RecordingMonitoringService();
        var service = new ChatHistoryService(fakeFactory, new NullLoggerService(), monitoring);

        // JANGAN melempar — catch di dalam
        var exception = await Record.ExceptionAsync(() =>
            service.SaveChatAsync(new ChatHistory
            {
                SessionId = "session-fail",
                UserId = "user-001",
                UserMessage = "test",
                AiResponse = "test",
                ResponseTimeMs = 0,
                Success = false
            }));

        Assert.Null(exception); // Tidak dilempar ke caller
        Assert.Equal(0, monitoring.SuccessCount);
        Assert.Equal(1, monitoring.FailureCount);
    }

    [Fact]
    public async Task SaveChatAsync_DbFailure_SessionIdTercatat()
    {
        var fakeFactory = new FakeFailingDbContextFactory("Connection refused");
        var monitoring = new RecordingMonitoringService();
        var service = new ChatHistoryService(fakeFactory, new NullLoggerService(), monitoring);

        await service.SaveChatAsync(new ChatHistory
        {
            SessionId = "session-x-fail",
            UserId = "user-001",
            UserMessage = "test",
            AiResponse = "test",
            ResponseTimeMs = 0,
            Success = false
        });

        var lastCall = monitoring.Calls.Last();
        Assert.False(lastCall.Success);
        Assert.Equal("session-x-fail", lastCall.SessionId);
    }

    [Fact]
    public async Task SaveChatAsync_NullHistory_LogsWarning_TidakLematparDanTidakRecordMetric()
    {
        var monitoring = new RecordingMonitoringService();
        var factory = new MockDbContextFactory();
        var service = new ChatHistoryService(factory, new NullLoggerService(), monitoring);

        var exception = await Record.ExceptionAsync(() =>
            service.SaveChatAsync(null!));

        Assert.Null(exception);
        Assert.Equal(0, monitoring.SuccessCount);
        Assert.Equal(0, monitoring.FailureCount);
    }

    // =====================================================================
    // BAGIAN 2: MonitoringService static counters (test langsung RecordHistorySave)
    // =====================================================================

    [Fact]
    public void RecordHistorySave_RecordsToStaticCounters()
    {
        // Catat state awal
        var beforeSuccess = MonitoringService.HistorySaveSuccessCount;
        var beforeFailure = MonitoringService.HistorySaveFailureCount;

        // MonitoringService.RecordHistorySave() increments static counters.
        // Test ini memverifikasi bahwa MonitoringService.RecordHistorySave
        // secara langsung memodifikasi static counters HistorySaveSuccessCount/FailureCount.
        var monitoring = new MonitoringService(new MockDbContextFactory(), TestHelpers.CreateFakeHubContext(), new NullLoggerService());

        monitoring.RecordHistorySave(success: true, sessionId: "s1");
        monitoring.RecordHistorySave(success: false, sessionId: "s2");
        monitoring.RecordHistorySave(success: true, sessionId: "s3");

        Assert.Equal(beforeSuccess + 2, MonitoringService.HistorySaveSuccessCount);
        Assert.Equal(beforeFailure + 1, MonitoringService.HistorySaveFailureCount);
    }

    // --- Mock factory — bypass real DbContext, SaveChangesAsync always succeeds ---
    private sealed class MockDbContextFactory : IDbContextFactory<JIFAS_AssistantContext>
    {
        public JIFAS_AssistantContext CreateDbContext() => new MockJifasContext();
        public Task<JIFAS_AssistantContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult<JIFAS_AssistantContext>(new MockJifasContext());
    }

    // Mock context that bypasses real DB — ChatHistories initialized properly
    private sealed class MockJifasContext : JIFAS_AssistantContext
    {
        public MockJifasContext() : base() { }

        public override int SaveChanges() => 1;
        public override Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);

        // Override DbSet to return initialized set (EF Core needs this)
        public override DbSet<ChatHistory> ChatHistories => new MockDbSet<ChatHistory>();
    }

    // Minimal DbSet mock for testing — only implements what SaveChatAsync needs
    private class MockDbSet<T> : DbSet<T> where T : class
    {
        private readonly HashSet<T> _data = new();
        public override EntityEntry<T> Add(T entity)
        {
            _data.Add(entity);
            return null!; // Return value not used by SaveChatAsync
        }
        public override void AddRange(IEnumerable<T> entities)
        {
            foreach (var e in entities) _data.Add(e);
        }
        // Required abstract property
        public override Microsoft.EntityFrameworkCore.Metadata.IEntityType EntityType =>
            null!;
    }

    // --- Factory yang melempar exception ---
    private sealed class FakeFailingDbContextFactory : IDbContextFactory<JIFAS_AssistantContext>
    {
        private readonly Exception _ex;
        public FakeFailingDbContextFactory(string msg) => _ex = new InvalidOperationException(msg);
        public JIFAS_AssistantContext CreateDbContext() => throw _ex;
        public Task<JIFAS_AssistantContext> CreateDbContextAsync(CancellationToken ct = default) =>
            throw _ex;
    }
}
