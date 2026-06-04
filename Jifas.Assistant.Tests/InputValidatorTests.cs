using Jifas.Assistant.Models;
using Jifas.Assistant.Services;
using Jifas.Assistant.Utilities;

namespace Jifas.Assistant.Tests;

public class InputValidatorTests
{
    [Fact]
    public void ValidateChatRequest_AllowsConfiguredMaximumLength()
    {
        var validator = new InputValidator(new NullLoggerService());
        var request = new ChatRequest { Message = new string('a', ValidationConstants.MAX_MESSAGE_LENGTH) };

        var result = validator.ValidateChatRequest(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateChatRequest_RejectsSqlInjectionPattern()
    {
        var validator = new InputValidator(new NullLoggerService());
        var request = new ChatRequest { Message = "' OR '1'='1" };

        var result = validator.ValidateChatRequest(request);

        Assert.False(result.IsValid);
    }

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
}
