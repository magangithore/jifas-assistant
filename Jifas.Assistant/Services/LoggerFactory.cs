using System;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Service locator for logger instances - FOR LEGACY COMPATIBILITY ONLY
    /// 
    /// Modern .NET 10 approach: Use dependency injection instead.
    /// This is a fallback service locator for rare edge cases where DI is not available
    /// (e.g., static utility methods, global initialization code).
    /// 
    /// For normal application code, inject ILoggerService via constructor.
    /// </summary>
    public static class LoggerFactory
    {
        private static ILoggerService _instance;
        private static readonly object _lockObject = new object();
        private static bool _initialized = false;

        /// <summary>
        /// Get the logger instance
        /// 
        /// DEPRECATED: Prefer dependency injection instead.
        /// Throws InvalidOperationException if SetLogger has not been called during initialization.
        /// </summary>
        public static ILoggerService GetLogger()
        {
            if (_instance == null)
            {
                lock (_lockObject)
                {
                    if (_instance == null && !_initialized)
                    {
                        throw new InvalidOperationException(
                            "LoggerFactory has not been initialized. " +
                            "Call LoggerFactory.SetLogger() from Program.cs during DI setup, " +
                            "or better yet, use dependency injection to inject ILoggerService directly."
                        );
                    }
                }
            }

            return _instance;
        }

        /// <summary>
        /// Initialize the logger instance from the DI container
        /// 
        /// Call this once in Program.cs after building the service provider:
        /// 
        /// Example:
        /// var serviceProvider = services.BuildServiceProvider();
        /// LoggerFactory.SetLogger(serviceProvider.GetRequiredService<ILoggerService>());
        /// </summary>
        public static void SetLogger(ILoggerService logger)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            lock (_lockObject)
            {
                _instance = logger;
                _initialized = true;
                System.Diagnostics.Debug.WriteLine("[LoggerFactory] Logger initialized from DI container");
            }
        }
    }
}
