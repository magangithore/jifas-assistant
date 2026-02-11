using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Fallback file-based logger implementation
    /// Provides basic structured logging to file with daily rotation
    /// Thread-safe logging with graceful fallback mechanisms
    /// </summary>
    public class FileLoggerService : ILoggerService
    {
        private readonly string _logFilePath;
        private readonly string _minLevelConfig;
        private static readonly object _lockObject = new object();
        private readonly IConfiguration _configuration;

        public FileLoggerService(IConfiguration configuration)
        {
            _configuration = configuration;

            // Try to get log path from configuration first
            var configPath = _configuration["Logging:LogFilePath"];
            
            if (!string.IsNullOrEmpty(configPath))
            {
                _logFilePath = configPath;
            }
            else
            {
                // Use LocalApplicationData for guaranteed write permissions (cross-platform)
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "JIFAS", "Assistant", "Logs");
                _logFilePath = Path.Combine(appDataPath, "jifas.log");
            }
            
            _minLevelConfig = _configuration["Logging:MinLevel"] ?? "Information";

            // Ensure log directory exists
            try
            {
                var logDir = Path.GetDirectoryName(_logFilePath);
                if (!Directory.Exists(logDir) && !string.IsNullOrEmpty(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileLoggerService] Warning: Could not create log directory: {ex.Message}");
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
            var fullMessage = new StringBuilder(message);
            
            if (ex != null)
            {
                fullMessage.AppendLine();
                fullMessage.Append($"Exception: {ex.GetType().Name}: {ex.Message}");
                fullMessage.AppendLine();
                fullMessage.Append($"StackTrace: {ex.StackTrace}");
                
                if (ex.InnerException != null)
                {
                    fullMessage.AppendLine();
                    fullMessage.Append($"InnerException: {ex.InnerException.Message}");
                }
            }
            
            WriteLog("ERROR", fullMessage.ToString(), args);
        }

        public void LogDebug(string message, params object[] args)
        {
            // Only log debug if enabled in config
            if (_minLevelConfig.Equals("Debug", StringComparison.OrdinalIgnoreCase))
            {
                WriteLog("DEBUG", message, args);
            }
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
                        EnsureLogDirectoryExists(dateBasedPath);
                        File.AppendAllText(dateBasedPath, logEntry + Environment.NewLine, Encoding.UTF8);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Fallback: Use temp folder as last resort
                        var tempPath = Path.Combine(Path.GetTempPath(), "JIFAS_Assistant_Logs");
                        EnsureLogDirectoryExists(tempPath);
                        
                        var tempLogPath = Path.Combine(tempPath, $"jifas-{DateTime.Now:yyyy-MM-dd}.log");
                        File.AppendAllText(tempLogPath, logEntry + Environment.NewLine, Encoding.UTF8);
                    }
                    catch (Exception writeEx)
                    {
                        // Last resort: write to console
                        Console.WriteLine($"[FileLoggerService] Failed to write log: {writeEx.Message}");
                        Console.WriteLine(logEntry);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileLoggerService] Critical error in WriteLog: {ex.Message}");
            }
        }

        private void EnsureLogDirectoryExists(string filePath)
        {
            var logDir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
        }
    }
}
