using System;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Interface for application logging
    /// Abstraction for logging provider (Serilog, NLog, etc.)
    /// Supports structured logging with correlation tracking
    /// </summary>
    public interface ILoggerService
    {
        void LogInformation(string message, params object[] args);
        void LogWarning(string message, params object[] args);
        void LogError(string message, Exception ex = null, params object[] args);
        void LogDebug(string message, params object[] args);

        /// <summary>
        /// Log with correlation ID for request tracing through the system
        /// </summary>
        void LogInformationWithCorrelation(string correlationId, string message, params object[] args);
        
        /// <summary>
        /// Log warning with correlation ID for tracking
        /// </summary>
        void LogWarningWithCorrelation(string correlationId, string message, params object[] args);
        
        /// <summary>
        /// Log error with correlation ID for request tracking
        /// </summary>
        void LogErrorWithCorrelation(string correlationId, string message, Exception ex = null, params object[] args);

        /// <summary>
        /// Log audit trail event (user action tracking)
        /// Includes user, action, timestamp, and any relevant context
        /// </summary>
        void LogAudit(string userId, string action, string details, string correlationId = null);

        /// <summary>
        /// Log performance metric
        /// Tracks timing information for monitoring and optimization
        /// </summary>
        void LogPerformance(string operation, long milliseconds, string correlationId = null);
    }
}
