using System;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Factory for creating logger instances
    /// Handles logger initialization and lifecycle
    /// </summary>
    public static class LoggerFactory
    {
        private static ILoggerService _instance;
        private static readonly object _lockObject = new object();

        /// <summary>
        /// Get or create the singleton logger instance
        /// </summary>
        public static ILoggerService GetLogger()
        {
            if (_instance == null)
            {
                lock (_lockObject)
                {
                    if (_instance == null)
                    {
                        try
                        {
                            _instance = new FileLoggerService();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[LoggerFactory] Failed to initialize FileLogger: {ex.Message}");
                            throw;
                        }
                    }
                }
            }

            return _instance;
        }
    }
}
