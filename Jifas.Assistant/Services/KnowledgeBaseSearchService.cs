using System;
using System.Collections.Generic;
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
        Task<List<KnowledgeBaseChunkDto>> SearchByKeywordAsync(string query, int topK = 5);
        Task<List<KnowledgeBaseChunkDto>> SearchBySemanticAsync(float[] embedding, int topK = 5);
        Task<List<KnowledgeBaseChunkDto>> SearchAsync(string query, float[] embedding = null, int topK = 5);
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

        public async Task<List<KnowledgeBaseChunkDto>> SearchByKeywordAsync(string query, int topK = 5)
        {
            try
            {
                _logger.LogInformation($"[KnowledgeBaseSearchService] Keyword search: {query}");

                var keywords = query.ToLower().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                var chunks = await _db.KnowledgeBaseChunks
                    .Include(c => c.Document)
                    .Where(c => c.Document != null && c.Document.IsActive == true)
                    .ToListAsync();

                var results = chunks
                    .Where(c => keywords.Any(k => c.Content.ToLower().Contains(k)))
                    .Select(c => new KnowledgeBaseChunkDto
                    {
                        Id = c.Id,
                        DocumentId = c.DocumentId,
                        Title = c.Document?.Title ?? "Unknown",
                        Content = c.Content,
                        Category = c.Document?.Category ?? "General",
                        ChunkIndex = c.ChunkIndex,
                        RelevanceScore = CalculateKeywordRelevance(c.Content, keywords)
                    })
                    .OrderByDescending(x => x.RelevanceScore)
                    .Take(topK)
                    .ToList();

                _logger.LogInformation($"[KnowledgeBaseSearchService] Found {results.Count} keyword matches");
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[KnowledgeBaseSearchService] Keyword search error: {ex.Message}");
                return new List<KnowledgeBaseChunkDto>();
            }
        }

        public async Task<List<KnowledgeBaseChunkDto>> SearchBySemanticAsync(float[] embedding, int topK = 5)
        {
            try
            {
                _logger.LogInformation($"[KnowledgeBaseSearchService] Semantic search with {embedding?.Length ?? 0}-dim embedding");

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
                            return new { Chunk = c, Similarity = similarity };
                        }
                        catch
                        {
                            return null;
                        }
                    })
                    .Where(x => x != null)
                    .Select(x => new KnowledgeBaseChunkDto
                    {
                        Id = x.Chunk.Id,
                        DocumentId = x.Chunk.DocumentId,
                        Title = x.Chunk.Document?.Title ?? "Unknown",
                        Content = x.Chunk.Content,
                        Category = x.Chunk.Document?.Category ?? "General",
                        ChunkIndex = x.Chunk.ChunkIndex,
                        RelevanceScore = x.Similarity
                    })
                    .OrderByDescending(x => x.RelevanceScore)
                    .Take(topK)
                    .ToList();

                _logger.LogInformation($"[KnowledgeBaseSearchService] Found {results.Count} semantic matches");
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[KnowledgeBaseSearchService] Semantic search error: {ex.Message}");
                return new List<KnowledgeBaseChunkDto>();
            }
        }

        public async Task<List<KnowledgeBaseChunkDto>> SearchAsync(string query, float[] embedding = null, int topK = 5)
        {
            try
            {
                var keywordResults = await SearchByKeywordAsync(query, topK);

                if (embedding != null && embedding.Length > 0)
                {
                    var semanticResults = await SearchBySemanticAsync(embedding, topK);

                    // Merge results (hybrid)
                    var merged = new Dictionary<int, KnowledgeBaseChunkDto>();

                    foreach (var result in keywordResults)
                    {
                        merged[result.Id] = result;
                    }

                    foreach (var result in semanticResults)
                    {
                        if (merged.ContainsKey(result.Id))
                        {
                            // Average the scores
                            merged[result.Id].RelevanceScore = (merged[result.Id].RelevanceScore + result.RelevanceScore) / 2;
                        }
                        else
                        {
                            merged[result.Id] = result;
                        }
                    }

                    return merged.Values
                        .OrderByDescending(x => x.RelevanceScore)
                        .Take(topK)
                        .ToList();
                }

                return keywordResults;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[KnowledgeBaseSearchService] Hybrid search error: {ex.Message}");
                return new List<KnowledgeBaseChunkDto>();
            }
        }

        private double CalculateKeywordRelevance(string content, string[] keywords)
        {
            var score = 0.0;
            var contentLower = content.ToLower();

            foreach (var keyword in keywords)
            {
                // Count occurrences
                var count = (contentLower.Length - contentLower.Replace(keyword, "").Length) / keyword.Length;
                score += Math.Min(count * 0.1, 1.0); // Cap at 1.0 per keyword
            }

            return Math.Min(score, 1.0); // Normalize to [0, 1]
        }

        private double CosineSimilarity(float[] vec1, float[] vec2)
        {
            if (vec1.Length != vec2.Length)
                return 0;

            double dotProduct = 0;
            for (int i = 0; i < vec1.Length; i++)
            {
                dotProduct += vec1[i] * vec2[i];
            }

            double magnitude1 = Math.Sqrt(vec1.Sum(x => x * x));
            double magnitude2 = Math.Sqrt(vec2.Sum(x => x * x));

            if (magnitude1 == 0 || magnitude2 == 0)
                return 0;

            return dotProduct / (magnitude1 * magnitude2);
        }
    }
}
