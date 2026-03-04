using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using jifas_assistant.DAL.Models;

namespace Jifas.Assistant.Services
{
    public class KnowledgeBaseChunkDto
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string Category { get; set; }
        public int ChunkIndex { get; set; }
        public double RelevanceScore { get; set; }
    }

    public interface IKnowledgeBaseSearchService
    {
        Task<List<KnowledgeBaseChunkDto>> SearchByKeywordAsync(string query, int topK = 5, string correlationId = null);
        Task<List<KnowledgeBaseChunkDto>> SearchBySemanticAsync(float[] embedding, int topK = 5, string correlationId = null);
        Task<List<KnowledgeBaseChunkDto>> SearchAsync(string query, float[] embedding = null, int topK = 5, string correlationId = null);
    }

    public class KnowledgeBaseSearchService : IKnowledgeBaseSearchService
    {
        private readonly JIFAS_AssistantContext _db;
        private readonly ILoggerService _logger;

        public KnowledgeBaseSearchService(JIFAS_AssistantContext db, ILoggerService logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<List<KnowledgeBaseChunkDto>> SearchByKeywordAsync(string query, int topK = 5, string correlationId = null)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var logMsg = string.IsNullOrEmpty(correlationId) 
                    ? $"[KnowledgeBaseSearchService] Keyword search: {query}"
                    : $"[{correlationId}] Keyword search: {query}";
                _logger.LogInformation(logMsg);

                var keywords = query.ToLower().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (keywords.Length == 0)
                    return new List<KnowledgeBaseChunkDto>();

                var primaryKeyword = keywords.FirstOrDefault();
                if (string.IsNullOrEmpty(primaryKeyword))
                    return new List<KnowledgeBaseChunkDto>();

                var likePattern = $"%{primaryKeyword}%";

                var chunks = await _db.KnowledgeBaseChunks
                    .Include(c => c.Document)
                    .Where(c => c.Document != null && 
                                c.Document.IsActive == true &&
                                (EF.Functions.Like(c.Content, likePattern) ||
                                 EF.Functions.Like(c.Document.Title, likePattern) ||
                                 EF.Functions.Like(c.Document.Category, likePattern)))
                    .OrderByDescending(c => c.Document.Title)
                    .Take(topK * 3)
                    .ToListAsync();

                stopwatch.Stop();
                if (!string.IsNullOrEmpty(correlationId))
                {
                    _logger.LogPerformance("KBKeywordSearch", stopwatch.ElapsedMilliseconds, correlationId);
                }

                var results = chunks
                    .Select(c =>
                    {
                        var contentLower = c.Content.ToLower();
                        var titleLower = c.Document?.Title.ToLower() ?? "";
                        var categoryLower = c.Document?.Category.ToLower() ?? "";

                        var titleMatches = keywords.Count(k => titleLower.Contains(k));
                        var contentMatches = keywords.Count(k => contentLower.Contains(k));
                        var relevanceScore = (titleMatches * 2 + contentMatches) / (keywords.Length * 2.0);

                        return new KnowledgeBaseChunkDto
                        {
                            Id = c.Id,
                            DocumentId = c.DocumentId,
                            Title = c.Document?.Title,
                            Content = c.Content,
                            Category = c.Document?.Category,
                            ChunkIndex = c.ChunkIndex,
                            RelevanceScore = Math.Min(relevanceScore, 1.0)
                        };
                    })
                    .OrderByDescending(r => r.RelevanceScore)
                    .Take(topK)
                    .ToList();

                return results;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var logMsg = string.IsNullOrEmpty(correlationId)
                    ? $"[KnowledgeBaseSearchService] Keyword search error: {ex.Message}"
                    : $"[{correlationId}] Keyword search error: {ex.Message}";
                _logger.LogError(logMsg, ex);
                
                if (!string.IsNullOrEmpty(correlationId))
                {
                    _logger.LogPerformance("KBKeywordSearch_Error", stopwatch.ElapsedMilliseconds, correlationId);
                }
                return new List<KnowledgeBaseChunkDto>();
            }
        }

        public async Task<List<KnowledgeBaseChunkDto>> SearchBySemanticAsync(float[] embedding, int topK = 5, string correlationId = null)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var logMsg = string.IsNullOrEmpty(correlationId)
                    ? $"[KnowledgeBaseSearchService] Semantic search with {embedding?.Length ?? 0}-dim embedding"
                    : $"[{correlationId}] Semantic search with {embedding?.Length ?? 0}-dim embedding";
                _logger.LogInformation(logMsg);

                if (embedding == null || embedding.Length == 0)
                    return new List<KnowledgeBaseChunkDto>();

                var chunks = await _db.KnowledgeBaseChunks
                    .Include(c => c.Document)
                    .Where(c => c.Document != null && c.Document.IsActive == true && c.Embedding != null)
                    .ToListAsync();

                var results = chunks
                    .Select(c =>
                    {
                        try
                        {
                            var chunkEmbedding = JsonConvert.DeserializeObject<List<float>>(c.Embedding);
                            if (chunkEmbedding == null) return null;

                            var similarity = CosineSimilarity(embedding, chunkEmbedding.ToArray());

                            return new KnowledgeBaseChunkDto
                            {
                                Id = c.Id,
                                DocumentId = c.DocumentId,
                                Title = c.Document?.Title,
                                Content = c.Content,
                                Category = c.Document?.Category,
                                ChunkIndex = c.ChunkIndex,
                                RelevanceScore = similarity
                            };
                        }
                        catch
                        {
                            return null;
                        }
                    })
                    .Where(r => r != null)
                    .OrderByDescending(r => r.RelevanceScore)
                    .Take(topK)
                    .ToList();

                stopwatch.Stop();
                if (!string.IsNullOrEmpty(correlationId))
                {
                    _logger.LogPerformance("KBSemanticSearch", stopwatch.ElapsedMilliseconds, correlationId);
                }

                return results;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var logMsg = string.IsNullOrEmpty(correlationId)
                    ? $"[KnowledgeBaseSearchService] Semantic search error: {ex.Message}"
                    : $"[{correlationId}] Semantic search error: {ex.Message}";
                _logger.LogError(logMsg, ex);
                
                if (!string.IsNullOrEmpty(correlationId))
                {
                    _logger.LogPerformance("KBSemanticSearch_Error", stopwatch.ElapsedMilliseconds, correlationId);
                }
                return new List<KnowledgeBaseChunkDto>();
            }
        }

        public async Task<List<KnowledgeBaseChunkDto>> SearchAsync(string query, float[] embedding = null, int topK = 5, string correlationId = null)
        {
            var totalStopwatch = Stopwatch.StartNew();
            try
            {
                var logMsg = string.IsNullOrEmpty(correlationId)
                    ? $"[KnowledgeBaseSearchService] Hybrid search: {query}"
                    : $"[{correlationId}] Hybrid search: {query}";
                _logger.LogInformation(logMsg);

                // Hybrid search: keyword + semantic
                var keywordResults = await SearchByKeywordAsync(query, topK, correlationId);
                
                List<KnowledgeBaseChunkDto> semanticResults = null;
                if (embedding != null && embedding.Length > 0)
                {
                    semanticResults = await SearchBySemanticAsync(embedding, topK, correlationId);
                }

                // Merge results dengan deduplication
                var mergedResults = new Dictionary<int, KnowledgeBaseChunkDto>();
                
                foreach (var result in keywordResults)
                {
                    mergedResults[result.Id] = result;
                }

                if (semanticResults != null)
                {
                    foreach (var result in semanticResults)
                    {
                        if (mergedResults.ContainsKey(result.Id))
                        {
                            mergedResults[result.Id].RelevanceScore = 
                                (mergedResults[result.Id].RelevanceScore + result.RelevanceScore) / 2.0;
                        }
                        else
                        {
                            mergedResults[result.Id] = result;
                        }
                    }
                }

                var finalResults = mergedResults.Values
                    .OrderByDescending(r => r.RelevanceScore)
                    .Take(topK)
                    .ToList();

                totalStopwatch.Stop();
                if (!string.IsNullOrEmpty(correlationId))
                {
                    _logger.LogPerformance("KBHybridSearch", totalStopwatch.ElapsedMilliseconds, correlationId);
                }

                return finalResults;
            }
            catch (Exception ex)
            {
                totalStopwatch.Stop();
                var logMsg = string.IsNullOrEmpty(correlationId)
                    ? $"[KnowledgeBaseSearchService] Hybrid search error: {ex.Message}"
                    : $"[{correlationId}] Hybrid search error: {ex.Message}";
                _logger.LogError(logMsg, ex);
                
                if (!string.IsNullOrEmpty(correlationId))
                {
                    _logger.LogPerformance("KBHybridSearch_Error", totalStopwatch.ElapsedMilliseconds, correlationId);
                }
                return new List<KnowledgeBaseChunkDto>();
            }
        }

        private float CosineSimilarity(float[] a, float[] b)
        {
            if (a.Length != b.Length)
                return 0f;

            float dotProduct = 0f;
            float normA = 0f;
            float normB = 0f;

            for (int i = 0; i < a.Length; i++)
            {
                dotProduct += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }

            if (normA == 0 || normB == 0)
                return 0f;

            return dotProduct / (float)(Math.Sqrt(normA) * Math.Sqrt(normB));
        }
    }
}
