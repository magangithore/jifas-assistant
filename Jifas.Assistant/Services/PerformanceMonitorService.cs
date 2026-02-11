using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Service for monitoring application performance and operation metrics
    /// 
    /// Tracks operation execution times, counts, and identifies performance bottlenecks.
    /// Thread-safe with Stopwatch-based timing and configurable thresholds.
    /// Compatible with .NET 10 and uses proper dependency injection.
    /// </summary>
    public class PerformanceMonitorService : IPerformanceMonitorService
    {
        private readonly ILoggerService _logger;
        private readonly IConfiguration _configuration;
        private readonly Dictionary<string, OperationMetric> _metrics = new Dictionary<string, OperationMetric>();
        private readonly Dictionary<string, Stopwatch> _activeOperations = new Dictionary<string, Stopwatch>();
        private static readonly object _lockObject = new object();

        // Default performance thresholds (milliseconds)
        private readonly double _slowOperationThresholdMs;

        /// <summary>
        /// Internal class for tracking operation metrics
        /// </summary>
        private class OperationMetric
        {
            public string OperationName { get; set; }
            public int Count { get; set; } = 0;
            public double TotalDurationMs { get; set; } = 0;
            public double MinDurationMs { get; set; } = double.MaxValue;
            public double MaxDurationMs { get; set; } = 0;
            public double AverageDurationMs { get; set; } = 0;
            public DateTime LastExecutedAt { get; set; }

            /// <summary>
            /// Record a single operation execution
            /// </summary>
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

        /// <summary>
        /// Initialize performance monitor with dependency injection
        /// </summary>
        public PerformanceMonitorService(
            ILoggerService logger,
            IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            // Read threshold from configuration (default: 1000ms)
            _slowOperationThresholdMs = _configuration.GetValue("Performance:SlowOperationThresholdMs", 1000.0);

            _logger.LogInformation("[PerformanceMonitor] Initialized with slow operation threshold: {0}ms", 
                _slowOperationThresholdMs);
        }

        /// <summary>
        /// Start tracking an operation by name
        /// Records the start time using Stopwatch for precise timing
        /// </summary>
        public void StartOperation(string operationName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(operationName))
                {
                    _logger.LogWarning("[PerformanceMonitor] Empty operation name provided");
                    return;
                }

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
                _logger.LogError("[PerformanceMonitor] Error starting operation {0}: {1}", ex, operationName, ex.Message);
            }
        }

        /// <summary>
        /// End tracking an operation and record its duration
        /// Automatically logs if operation exceeded slow threshold
        /// </summary>
        public void EndOperation(string operationName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(operationName))
                {
                    _logger.LogWarning("[PerformanceMonitor] Empty operation name provided");
                    return;
                }

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

                    // Log if slow
                    if (durationMs > _slowOperationThresholdMs)
                    {
                        _logger.LogWarning("[PerformanceMonitor] SLOW operation: {0} took {1:F2}ms (threshold: {2}ms)", 
                            operationName, durationMs, _slowOperationThresholdMs);
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
                _logger.LogError("[PerformanceMonitor] Error ending operation {0}: {1}", ex, operationName, ex.Message);
            }
        }

        /// <summary>
        /// Get average execution duration for an operation
        /// Returns 0 if operation not found or no executions recorded
        /// </summary>
        public double GetAverageDuration(string operationName)
        {
            if (string.IsNullOrWhiteSpace(operationName))
                return 0;

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
        /// Get total execution count for an operation
        /// Returns 0 if operation not found
        /// </summary>
        public int GetOperationCount(string operationName)
        {
            if (string.IsNullOrWhiteSpace(operationName))
                return 0;

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
        /// Get all performance metrics for all tracked operations
        /// Returns dictionary mapping operation name to detailed metrics
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

                    _logger.LogDebug("[PerformanceMonitor] Retrieved metrics for {0} operations", result.Count);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[PerformanceMonitor] Error getting metrics: {0}", ex, ex.Message);
                return new Dictionary<string, Dictionary<string, object>>();
            }
        }

        /// <summary>
        /// Clear all accumulated metrics and start fresh
        /// </summary>
        public void ClearMetrics()
        {
            try
            {
                lock (_lockObject)
                {
                    int metricsCount = _metrics.Count;
                    _metrics.Clear();
                    _logger.LogInformation("[PerformanceMonitor] Cleared {0} performance metrics", metricsCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[PerformanceMonitor] Error clearing metrics: {0}", ex, ex.Message);
            }
        }

        /// <summary>
        /// Get operations that exceeded the performance threshold
        /// Threshold default is 1000ms, but configurable
        /// </summary>
        public Dictionary<string, object> GetSlowOperations(double thresholdMs = 1000)
        {
            try
            {
                if (thresholdMs <= 0)
                {
                    _logger.LogWarning("[PerformanceMonitor] Invalid threshold provided: {0}ms", thresholdMs);
                    thresholdMs = 1000;
                }

                lock (_lockObject)
                {
                    var slowOps = new Dictionary<string, object>();

                    foreach (var metric in _metrics.Values.Where(m => m.AverageDurationMs > thresholdMs))
                    {
                        slowOps[metric.OperationName] = new
                        {
                            average_duration_ms = Math.Round(metric.AverageDurationMs, 2),
                            max_duration_ms = Math.Round(metric.MaxDurationMs, 2),
                            min_duration_ms = Math.Round(metric.MinDurationMs, 2),
                            count = metric.Count,
                            exceeded_threshold_by_ms = Math.Round(metric.AverageDurationMs - thresholdMs, 2)
                        };
                    }

                    if (slowOps.Count > 0)
                    {
                        _logger.LogWarning("[PerformanceMonitor] Found {0} slow operations exceeding {1}ms threshold", 
                            slowOps.Count, thresholdMs);
                    }
                    else
                    {
                        _logger.LogDebug("[PerformanceMonitor] No slow operations exceeding {0}ms threshold", thresholdMs);
                    }

                    return slowOps;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[PerformanceMonitor] Error getting slow operations: {0}", ex, ex.Message);
                return new Dictionary<string, object>();
            }
        }
    }
}
