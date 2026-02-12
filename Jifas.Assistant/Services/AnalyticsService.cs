using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using jifas_assistant.DAL.Models;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Analytics service for Knowledge Base performance metrics
    /// Provides insights into doc usage, popular queries, and system health
    /// </summary>
    public interface IAnalyticsService
    {
        Task<List<DocumentPerformance>> GetDocumentPerformanceAsync();
        Task<List<QueryStatistic>> GetPopularQueriesAsync(int days = 30);
        Task<SystemHealthMetrics> GetSystemHealthAsync();
        Task<List<string>> GetRecommendationsAsync();
    }

    public class DocumentPerformance
    {
        public int DocumentId { get; set; }
        public string Title { get; set; }
        public int HitCount { get; set; }
        public double AverageConfidence { get; set; }
        public DateTime? LastAccessedAt { get; set; }
        public int DaysOld { get; set; }
        public double SuccessRate { get; set; }
        public string Status { get; set; } // HEALTHY, NEEDS_REVIEW, STALE
        public string Trend { get; set; } // INCREASING, STABLE, DECREASING
    }

    public class QueryStatistic
    {
        public string Query { get; set; }
        public int Frequency { get; set; }
        public double SuccessRate { get; set; }
        public double AverageConfidence { get; set; }
        public string TopDocument { get; set; }
    }

    public class SystemHealthMetrics
    {
        public double HealthScore { get; set; } // 0-1
        public int TotalConversations { get; set; }
        public double KbHitRate { get; set; } // % of queries that found KB results
        public double AverageConfidence { get; set; }
        public int DocumentCount { get; set; }
        public int ChunkCount { get; set; }
        public string HealthStatus { get; set; } // EXCELLENT, GOOD, FAIR, NEEDS_ATTENTION
        public DateTime LastUpdatedAt { get; set; }
        public List<string> Recommendations { get; set; } = new List<string>();
    }

    public class AnalyticsService : IAnalyticsService
    {
        private readonly JIFAS_AssistantContext _db;
        private readonly ILoggerService _logger;

        public AnalyticsService(JIFAS_AssistantContext db, ILoggerService logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<List<DocumentPerformance>> GetDocumentPerformanceAsync()
        {
            try
            {
                var result = new List<DocumentPerformance>();
                var now = DateTime.Now;

                // Get all active documents
                var documents = await _db.KnowledgeBaseDocuments
                    .Where(d => d.IsActive == true)
                    .ToListAsync();

                // Get all chats to analyze KB usage
                var chats = await _db.Chats.ToListAsync();

                foreach (var doc in documents)
                {
                    // Chats model menggunakan IsOutOfScope dan Confidence
                    var relatedChats = chats
                        .Where(c => !string.IsNullOrEmpty(c.RelatedDocumentIds) && c.RelatedDocumentIds.Contains(doc.Id.ToString()))
                        .ToList();

                    var hitCount = relatedChats.Count;
                    var avgConfidence = relatedChats.Count > 0 
                        ? relatedChats.Average(c => c.Confidence ?? 0)
                        : 0;

                    var lastAccessed = relatedChats.Count > 0
                        ? relatedChats.OrderByDescending(c => c.CreatedAt).First().CreatedAt
                        : (DateTime?)null;

                    var daysOld = lastAccessed.HasValue 
                        ? (int)(now - lastAccessed.Value).TotalDays
                        : 999;

                    var successRate = relatedChats.Count > 0
                        ? (double)relatedChats.Count(c => (c.Confidence ?? 0) > 0.5) / relatedChats.Count
                        : 0;

                    var status = DetermineDocStatus(hitCount, avgConfidence, daysOld);
                    var trend = "STABLE";

                    result.Add(new DocumentPerformance
                    {
                        DocumentId = doc.Id,
                        Title = doc.Title,
                        HitCount = hitCount,
                        AverageConfidence = Math.Round(avgConfidence, 2),
                        LastAccessedAt = lastAccessed,
                        DaysOld = daysOld,
                        SuccessRate = Math.Round(successRate, 2),
                        Status = status,
                        Trend = trend
                    });
                }

                return result.OrderByDescending(x => x.HitCount).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AnalyticsService] Error in GetDocumentPerformance: {ex.Message}");
                return new List<DocumentPerformance>();
            }
        }

        public async Task<List<QueryStatistic>> GetPopularQueriesAsync(int days = 30)
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-days);

                // Get chats from last N days
                var recentChats = await _db.Chats
                    .Where(c => c.CreatedAt >= cutoffDate && !string.IsNullOrEmpty(c.Message))
                    .ToListAsync();

                // Group by message and calculate stats
                var queryStats = recentChats
                    .GroupBy(c => c.Message)
                    .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                    .Select(g => new QueryStatistic
                    {
                        Query = g.Key,
                        Frequency = g.Count(),
                        AverageConfidence = Math.Round(g.Average(c => c.Confidence ?? 0), 2),
                        SuccessRate = Math.Round((double)g.Count(c => (c.Confidence ?? 0) > 0.5) / g.Count(), 2),
                        TopDocument = "KB"
                    })
                    .OrderByDescending(x => x.Frequency)
                    .Take(20)
                    .ToList();

                _logger.LogInformation($"[AnalyticsService] Found {queryStats.Count} unique queries in last {days} days");

                return queryStats;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AnalyticsService] Error in GetPopularQueries: {ex.Message}");
                return new List<QueryStatistic>();
            }
        }

        public async Task<SystemHealthMetrics> GetSystemHealthAsync()
        {
            try
            {
                var conversations = await _db.Chats.ToListAsync();
                var documents = await _db.KnowledgeBaseDocuments.Where(d => d.IsActive == true).ToListAsync();
                var chunks = await _db.KnowledgeBaseChunks.Where(c => c.Embedding != null).ToListAsync();

                var totalConversations = conversations.Count;
                var kbHitCount = conversations.Count(c => c.IsOutOfScope != true);
                var avgConfidence = conversations.Count > 0
                    ? conversations.Average(c => c.Confidence ?? 0)
                    : 0;

                var kbHitRate = totalConversations > 0 ? (double)kbHitCount / totalConversations : 0;
                var confidenceScore = avgConfidence;
                var healthScore = (kbHitRate * 0.5) + (confidenceScore * 0.5);

                var healthStatus = DetermineHealthStatus(healthScore);
                var recommendations = await GetRecommendationsAsync();

                return new SystemHealthMetrics
                {
                    HealthScore = Math.Round(healthScore, 2),
                    TotalConversations = totalConversations,
                    KbHitRate = Math.Round(kbHitRate, 2),
                    AverageConfidence = Math.Round(avgConfidence, 2),
                    DocumentCount = documents.Count,
                    ChunkCount = chunks.Count,
                    HealthStatus = healthStatus,
                    LastUpdatedAt = DateTime.Now,
                    Recommendations = recommendations
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AnalyticsService] Error in GetSystemHealth: {ex.Message}");
                return new SystemHealthMetrics();
            }
        }

        public async Task<List<string>> GetRecommendationsAsync()
        {
            var recommendations = new List<string>();

            try
            {
                var docPerformance = await GetDocumentPerformanceAsync();

                // Underperforming docs
                var needsReview = docPerformance
                    .Where(d => d.AverageConfidence < 0.65 && d.HitCount > 0)
                    .ToList();

                foreach (var doc in needsReview)
                {
                    recommendations.Add(
                        $"Review doc '{doc.Title}' - low confidence ({doc.AverageConfidence:F0}%) despite {doc.HitCount} hits");
                }

                // Stale docs
                var staleDocs = docPerformance
                    .Where(d => d.DaysOld > 30 && d.HitCount > 0)
                    .ToList();

                foreach (var doc in staleDocs.Take(3))
                {
                    recommendations.Add(
                        $"Update doc '{doc.Title}' - last accessed {doc.DaysOld} days ago");
                }

                // Unused docs
                var unusedDocs = docPerformance.Where(d => d.HitCount == 0).ToList();
                if (unusedDocs.Count > 0)
                {
                    var titles = string.Join(", ", unusedDocs.Select(d => $"'{d.Title}'"));
                    recommendations.Add($"Consider archiving unused docs: {titles}");
                }

                return recommendations.Take(5).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AnalyticsService] Error in GetRecommendations: {ex.Message}");
                return new List<string> { "Unable to generate recommendations" };
            }
        }

        private string DetermineDocStatus(int hitCount, double avgConfidence, int daysOld)
        {
            if (hitCount == 0)
                return "UNUSED";
            if (daysOld > 60)
                return "STALE";
            if (avgConfidence < 0.60)
                return "NEEDS_REVIEW";
            if (avgConfidence >= 0.80 && hitCount >= 5)
                return "HEALTHY";
            return "ACTIVE";
        }

        private string DetermineHealthStatus(double healthScore)
        {
            if (healthScore >= 0.85)
                return "EXCELLENT";
            if (healthScore >= 0.70)
                return "GOOD";
            if (healthScore >= 0.50)
                return "FAIR";
            return "NEEDS_ATTENTION";
        }
    }
}
