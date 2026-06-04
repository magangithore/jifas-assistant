using System;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Kontrak logging aplikasi.
    /// Service ini menjaga format log, correlation id, audit trail, dan performance metrics tetap konsisten.
    /// </summary>
    public interface ILoggerService
    {
        void LogInformation(string message, params object[] args);
        void LogWarning(string message, params object[] args);
        void LogError(string message, Exception? ex = null, params object[] args);
        void LogDebug(string message, params object[] args);

        /// <summary>
        /// Catat log informasi dengan correlation id untuk tracing request.
        /// </summary>
        void LogInformationWithCorrelation(string correlationId, string message, params object[] args);
        
        /// <summary>
        /// Catat warning dengan correlation id.
        /// </summary>
        void LogWarningWithCorrelation(string correlationId, string message, params object[] args);
        
        /// <summary>
        /// Catat error dengan correlation id.
        /// </summary>
        void LogErrorWithCorrelation(string correlationId, string message, Exception? ex = null, params object[] args);

        /// <summary>
        /// Catat audit trail untuk aksi user.
        /// </summary>
        void LogAudit(string userId, string action, string details, string? correlationId = null);

        /// <summary>
        /// Catat durasi operasi untuk monitoring performa.
        /// </summary>
        void LogPerformance(string operation, long milliseconds, string? correlationId = null);
    }
}
