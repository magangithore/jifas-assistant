using Jifas.Assistant.Models;
using Jifas.Assistant.Services;

namespace Jifas.Assistant.Tests;

public class AssistantCommandServiceTests
{
    [Fact]
    public void TryHandleCommand_ReturnsNull_ForNormalMessage()
    {
        var service = new AssistantCommandService();

        var result = service.TryHandleCommand(
            "Apa itu JIFAS?",
            new ChatRequest { Message = "Apa itu JIFAS?" },
            "session-1",
            "corr-1");

        Assert.Null(result);
    }

    [Fact]
    public void TryHandleCommand_ReturnsHelp_ForHelpCommand()
    {
        var service = new AssistantCommandService();

        var result = service.TryHandleCommand(
            "/help",
            new ChatRequest { Message = "/help" },
            "session-1",
            "corr-1");

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("Command Help", result.Source);
        Assert.Contains("/ticket", result.Message);
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public void TryHandleCommand_ReturnsContext_FromRequestMetadata()
    {
        var service = new AssistantCommandService();
        var request = new ChatRequest
        {
            Message = "/context",
            UserCompCode = "KI",
            CurrentModule = "Invoice",
            Context = new RequestContext
            {
                CurrentPage = "/Invoice/Approval",
                PageTitle = "Invoice Approval",
                DocumentType = "Invoice",
                DocumentStatus = "Need Head Approval"
            }
        };

        var result = service.TryHandleCommand("/context", request, "session-1", "corr-1");

        Assert.NotNull(result);
        Assert.Contains("Invoice Approval", result!.Message);
        Assert.Contains("Need Head Approval", result.Message);
    }

    [Fact]
    public void GetCapabilities_ReturnsStableCapabilityList()
    {
        var service = new AssistantCommandService();

        var capabilities = service.GetCapabilities();
        var commands = service.GetSupportedCommands();

        Assert.Contains(capabilities, c => c.Id == "kb-rag");
        Assert.Contains(capabilities, c => c.Id == "ticket-flow");
        Assert.Contains("/help", commands);
        Assert.Contains("/status", commands);
    }
}
