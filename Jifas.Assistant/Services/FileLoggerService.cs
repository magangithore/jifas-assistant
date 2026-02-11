using System;
using System.Configuration;
using System.IO;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Fallback file-based logger implementation
    /// Used when Serilog is not available
    /// Provides basic structured logging to file
    /// </summary>
    public class FileLoggerService : ILoggerService
    {
        private readonly string _logFilePath;
        private readonly string _minLevelConfig;
        private static readonly object _lockObject = new object();

        public FileLoggerService()
        {
            // Try to use config path first, fallback to AppData if unavailable
            var configPath = ConfigurationManager.AppSettings["Logging:LogFilePath"];
            if (!string.IsNullOrEmpty(configPath))
            {
                _logFilePath = configPath;
            }
            else
            {
                // Use AppData\Local\JIFAS\Logs for guaranteed write permissions
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "JIFAS", "Chatbot", "Logs");
                _logFilePath = Path.Combine(appDataPath, "jifas.log");
            }
            
            _minLevelConfig = ConfigurationManager.AppSettings["Logging:MinLevel"] ?? "Information";

            // Ensure log directory exists
            try
            {
                var logDir = Path.GetDirectoryName(_logFilePath);
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileLoggerService] Warning: Could not create log directory: {ex.Message}");
            }

            try
            {
                LogInformation("=== JIFAS AI Assistant File Logging Service Initialized ===");
            }
            catch
            {
                // Silently fail if logging during init fails
            }
        }

        public void LogInformation(string message, params object[] args)
        {
            WriteLog("INFO", message, args);
        }

        public void LogWarning(string message, params object[] args)
        {
            WriteLog("WARN", message, args);
        }

        public void LogError(string message, Exception ex = null, params object[] args)
        {
            var fullMessage = message;
            if (ex != null)
            {
                fullMessage += Environment.NewLine + $"Exception: {ex.Message}" + Environment.NewLine + $"StackTrace: {ex.StackTrace}";
            }
            WriteLog("ERROR", fullMessage, args);
        }

        public void LogDebug(string message, params object[] args)
        {
            WriteLog("DEBUG", message, args);
        }

        private void WriteLog(string level, string message, params object[] args)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var formattedMessage = args.Length > 0 ? string.Format(message, args) : message;
                var logEntry = $"[{timestamp}] [{level}] {formattedMessage}";

                lock (_lockObject)
                {
                    // Rotate log file daily
                    var dateBasedPath = _logFilePath.Replace(".log", $"-{DateTime.Now:yyyy-MM-dd}.log");
                    
                    try
                    {
                        File.AppendAllText(dateBasedPath, logEntry + Environment.NewLine);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // If AppData path fails, try temp folder as last resort
                        var tempPath = Path.Combine(Path.GetTempPath(), "JIFAS_Chatbot_Logs");
                        if (!Directory.Exists(tempPath))
                        {
                            Directory.CreateDirectory(tempPath);
                        }
                        
                        var tempLogPath = Path.Combine(tempPath, $"jifas-{DateTime.Now:yyyy-MM-dd}.log");
                        File.AppendAllText(tempLogPath, logEntry + Environment.NewLine);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileLoggerService] Error writing log: {ex.Message}");
            }
        }
    }
}
