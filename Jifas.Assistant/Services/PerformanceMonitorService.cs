using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Service for monitoring application performance and operation metrics
    /// </summary>
    public class PerformanceMonitorService : IPerformanceMonitorService
    {
        private readonly ILoggerService _logger;
        private readonly Dictionary<string, OperationMetric> _metrics = new Dictionary<string, OperationMetric>();
        private readonly Dictionary<string, Stopwatch> _activeOperations = new Dictionary<string, Stopwatch>();
        private static readonly object _lockObject = new object();

        private class OperationMetric
        {
            public string OperationName { get; set; }
            public int Count { get; set; } = 0;
            public double TotalDurationMs { get; set; } = 0;
            public double MinDurationMs { get; set; } = double.MaxValue;
            public double MaxDurationMs { get; set; } = 0;
            public double AverageDurationMs { get; set; } = 0;
            public DateTime LastExecutedAt { get; set; }

            public void RecordExecution(double durationMs)
            {
                Count++;
                TotalDurationMs += durationMs;
                MinDurationMs = Math.Min(MinDurationMs, durationMs);
                MaxDurationMs = Math.Max(MaxDurationMs, durationMs);
                AverageDurationMs = TotalDurationMs / Count;
                LastExecutedAt = DateTime.UtcNow;
            }
        }

        public PerformanceMonitorService()
        {
            _logger = LoggerFactory.GetLogger();
        }

        public PerformanceMonitorService(ILoggerService logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Start tracking an operation
        /// </summary>
        public void StartOperation(string operationName)
        {
            try
            {
                lock (_lockObject)
                {
                    if (_activeOperations.ContainsKey(operationName))
                    {
                        _logger.LogWarning("[PerformanceMonitor] Operation {0} already running", operationName);
                        return;
                    }

                    var stopwatch = Stopwatch.StartNew();
                    _activeOperations[operationName] = stopwatch;
                    _logger.LogDebug("[PerformanceMonitor] Started operation: {0}", operationName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[PerformanceMonitor] Error starting operation", ex);
            }
        }

        /// <summary>
        /// End tracking and record operation duration
        /// </summary>
        public void EndOperation(string operationName)
        {
            try
            {
                lock (_lockObject)
                {
                    if (!_activeOperations.ContainsKey(operationName))
                    {
                        _logger.LogWarning("[PerformanceMonitor] Operation {0} not found in active operations", operationName);
                        return;
                    }

                    var stopwatch = _activeOperations[operationName];
                    stopwatch.Stop();
                    double durationMs = stopwatch.Elapsed.TotalMilliseconds;

                    // Record metric
                    if (!_metrics.ContainsKey(operationName))
                    {
                        _metrics[operationName] = new OperationMetric { OperationName = operationName };
                    }

                    _metrics[operationName].RecordExecution(durationMs);

                    // Log if slow (> 1 second)
                    if (durationMs > 1000)
                    {
                        _logger.LogWarning("[PerformanceMonitor] SLOW operation: {0} took {1:F2}ms", 
                            operationName, durationMs);
                    }
                    else
                    {
                        _logger.LogDebug("[PerformanceMonitor] Completed operation: {0} in {1:F2}ms", 
                            operationName, durationMs);
                    }

                    _activeOperations.Remove(operationName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[PerformanceMonitor] Error ending operation", ex);
            }
        }

        /// <summary>
        /// Get average duration for an operation
        /// </summary>
        public double GetAverageDuration(string operationName)
        {
            lock (_lockObject)
            {
                if (_metrics.ContainsKey(operationName))
                {
                    return _metrics[operationName].AverageDurationMs;
                }
                return 0;
            }
        }

        /// <summary>
        /// Get operation execution count
        /// </summary>
        public int GetOperationCount(string operationName)
        {
            lock (_lockObject)
            {
                if (_metrics.ContainsKey(operationName))
                {
                    return _metrics[operationName].Count;
                }
                return 0;
            }
        }

        /// <summary>
        /// Get all performance metrics
        /// </summary>
        public Dictionary<string, Dictionary<string, object>> GetAllMetrics()
        {
            try
            {
                lock (_lockObject)
                {
                    var result = new Dictionary<string, Dictionary<string, object>>();

                    foreach (var metric in _metrics.Values)
                    {
                        result[metric.OperationName] = new Dictionary<string, object>
                        {
                            { "count", metric.Count },
                            { "total_duration_ms", Math.Round(metric.TotalDurationMs, 2) },
                            { "average_duration_ms", Math.Round(metric.AverageDurationMs, 2) },
                            { "min_duration_ms", Math.Round(metric.MinDurationMs, 2) },
                            { "max_duration_ms", Math.Round(metric.MaxDurationMs, 2) },
                            { "last_executed_at", metric.LastExecutedAt }
                        };
                    }

                    _logger.LogInformation("[PerformanceMonitor] Retrieved metrics for {0} operations", result.Count);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[PerformanceMonitor] Error getting metrics", ex);
                return new Dictionary<string, Dictionary<string, object>>();
            }
        }

        /// <summary>
        /// Clear all metrics
        /// </summary>
        public void ClearMetrics()
        {
            try
            {
                lock (_lockObject)
                {
                    _metrics.Clear();
                    _logger.LogInformation("[PerformanceMonitor] Cleared all metrics");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[PerformanceMonitor] Error clearing metrics", ex);
            }
        }

        /// <summary>
        /// Get operations that exceeded performance threshold
        /// </summary>
        public Dictionary<string, object> GetSlowOperations(double thresholdMs = 1000)
        {
            try
            {
                lock (_lockObject)
                {
                    var slowOps = new Dictionary<string, object>();

                    foreach (var metric in _metrics.Values.Where(m => m.AverageDurationMs > thresholdMs))
                    {
                        slowOps[metric.OperationName] = new
                        {
                            average_duration_ms = Math.Round(metric.AverageDurationMs, 2),
                            max_duration_ms = Math.Round(metric.MaxDurationMs, 2),
                            count = metric.Count,
                            exceeded_threshold = Math.Round(metric.AverageDurationMs - thresholdMs, 2)
                        };
                    }

                    if (slowOps.Count > 0)
                    {
                        _logger.LogWarning("[PerformanceMonitor] Found {0} slow operations exceeding {1}ms threshold", 
                            slowOps.Count, thresholdMs);
                    }

                    return slowOps;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[PerformanceMonitor] Error getting slow operations", ex);
                return new Dictionary<string, object>();
            }
        }
    }
}
