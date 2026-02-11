using System;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Interface for application logging
    /// Abstraction for logging provider (Serilog, NLog, etc.)
    /// </summary>
    public interface ILoggerService
    {
        void LogInformation(string message, params object[] args);
        void LogWarning(string message, params object[] args);
        void LogError(string message, Exception ex = null, params object[] args);
        void LogDebug(string message, params object[] args);
    }
}
