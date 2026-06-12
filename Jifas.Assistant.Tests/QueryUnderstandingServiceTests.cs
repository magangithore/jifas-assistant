using Jifas.Assistant.Services;

namespace Jifas.Assistant.Tests;

public class QueryUnderstandingServiceTests
{
    [Fact]
    public async Task IsInJifasScopeAsync_KeepsHistoryApprovalInvoiceInScope()
    {
        var service = new QueryUnderstandingService(new NoopLogger(), new TestCache());

        var inScope = await service.IsInJifasScopeAsync("History approval invoice ada di mana?");

        Assert.True(inScope);
    }

    [Fact]
    public async Task IsInJifasScopeAsync_BlocksWholeWordStoryOnly()
    {
        var service = new QueryUnderstandingService(new NoopLogger(), new TestCache());

        var outOfScope = await service.IsInJifasScopeAsync("Buat story fiksi pendek dong");
        var inScope = await service.IsInJifasScopeAsync("History approval payment JIFAS");

        Assert.False(outOfScope);
        Assert.True(inScope);
    }

    private sealed class TestCache : ICacheService
    {
        private readonly Dictionary<string, object> _store = new();

        public T Get<T>(string key) => _store.TryGetValue(key, out var value) && value is T typed ? typed : default!;

        public void Set<T>(string key, T value, int durationMinutes)
        {
            if (value != null)
                _store[key] = value;
        }

        public void Remove(string key) => _store.Remove(key);

        public void Clear() => _store.Clear();

        public bool Exists(string key) => _store.ContainsKey(key);
    }

    private sealed class NoopLogger : ILoggerService
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
}
