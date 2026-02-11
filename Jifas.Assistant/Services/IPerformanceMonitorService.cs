using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Interface for performance monitoring and metrics collection
    /// </summary>
    public interface IPerformanceMonitorService
    {
        /// <summary>
        /// Start tracking an operation's performance
        /// </summary>
        void StartOperation(string operationName);

        /// <summary>
        /// End tracking and record the duration
        /// </summary>
        void EndOperation(string operationName);

        /// <summary>
        /// Get average duration for an operation
        /// </summary>
        double GetAverageDuration(string operationName);

        /// <summary>
        /// Get total count of operation executions
        /// </summary>
        int GetOperationCount(string operationName);

        /// <summary>
        /// Get all performance metrics
        /// </summary>
        Dictionary<string, Dictionary<string, object>> GetAllMetrics();

        /// <summary>
        /// Clear all recorded metrics
        /// </summary>
        void ClearMetrics();

        /// <summary>
        /// Get slow operations (above threshold)
        /// </summary>
        Dictionary<string, object> GetSlowOperations(double thresholdMs = 1000);
    }
}
