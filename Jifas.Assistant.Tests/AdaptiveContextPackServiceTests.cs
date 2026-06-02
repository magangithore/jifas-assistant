using Jifas.Assistant.Models;
using Jifas.Assistant.Services;

namespace Jifas.Assistant.Tests;

public class AdaptiveContextPackServiceTests
{
    [Fact]
    public async Task BuildAsync_IncludesIntentEvidenceAndGroundingRules()
    {
        var service = new AdaptiveContextPackService(
            new StubUserMemoryService("=== PROFIL USER ===\nLevel Pengalaman JIFAS: Intermediate"),
            new NullLoggerService());

        var request = new ChatRequest
        {
            UserId = "user-1",
            Context = new RequestContext
            {
                ActiveModule = "Invoice",
                CurrentPage = "/invoice/create"
            }
        };

        var intent = new IntentResult
        {
            Intent = IntentType.HowTo,
            Confidence = 0.9
        };

        var expanded = new ExpandedQuery
        {
            Keywords = new List<string> { "invoice", "create" },
            Synonyms = new List<string> { "tagihan" }
        };

        var results = new List<KnowledgeBaseResult>
        {
            new()
            {
                Title = "Create Invoice",
                Category = "Invoice",
                Score = 0.82,
                Content = "Create invoice instructions"
            }
        };

        var pack = await service.BuildAsync(
            request,
            "Bagaimana cara buat invoice?",
            intent,
            expanded,
            results,
            conversationContext: "User sebelumnya membahas invoice.",
            isFollowUp: true,
            activePageContext: "PAGE:/invoice/create|MODULE:Invoice",
            confidenceScore: 0.76);

        Assert.Contains("=== CONTEXT PACK ===", pack.FormattedContext);
        Assert.Contains("Intent: HowTo", pack.FormattedContext);
        Assert.Contains("Topik utama: Invoice", pack.FormattedContext);
        Assert.Contains("Create Invoice", pack.FormattedContext);
        Assert.Contains("Jangan tampilkan proses analisis internal.", pack.FormattedContext);
        Assert.Equal("Step-by-step operator guidance", pack.AnswerMode);
    }

    private sealed class StubUserMemoryService : IUserMemoryService
    {
        private readonly string _context;

        public StubUserMemoryService(string context)
        {
            _context = context;
        }

        public Task<UserProfile> GetUserProfileAsync(string userId) =>
            Task.FromResult(new UserProfile { UserId = userId, IsNewUser = false });

        public Task UpdateMemoryAsync(
            string userId,
            string userMessage,
            string aiResponse,
            IntentType intent,
            double confidenceScore,
            string currentModule = null,
            string userRole = null,
            string sessionId = null) => Task.CompletedTask;

        public Task<string> BuildUserContextForPromptAsync(string userId) =>
            Task.FromResult(_context);

        public Task ExtractAndPersistPatternsAsync(
            string userId,
            string userMessage,
            string aiResponse,
            string sessionId = null) => Task.CompletedTask;
    }

    private sealed class NullLoggerService : ILoggerService
    {
        public void LogInformation(string message, params object[] args) { }
        public void LogWarning(string message, params object[] args) { }
        public void LogError(string message, Exception ex = null, params object[] args) { }
        public void LogDebug(string message, params object[] args) { }
        public void LogInformationWithCorrelation(string correlationId, string message, params object[] args) { }
        public void LogWarningWithCorrelation(string correlationId, string message, params object[] args) { }
        public void LogErrorWithCorrelation(string correlationId, string message, Exception ex = null, params object[] args) { }
        public void LogAudit(string userId, string action, string details, string correlationId = null) { }
        public void LogPerformance(string operation, long milliseconds, string correlationId = null) { }
    }
}
