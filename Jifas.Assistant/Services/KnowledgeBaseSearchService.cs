using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using Jifas.Assistant.Utilities;
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
        private readonly ICacheService _cacheService;
        private const int KB_CACHE_MINUTES = 30;

        private static ConcurrentDictionary<int, float[]> _embeddingCache = new();
        private static ConcurrentDictionary<int, KnowledgeBaseChunkDto> _metadataCache = new();
        private static DateTime _lastEmbeddingCacheRefreshUtc = DateTime.MinValue;
        private static readonly TimeSpan EmbeddingCacheTtl = TimeSpan.FromMinutes(10);
        private static readonly SemaphoreSlim EmbeddingCacheLoadLock = new(1, 1);

        // Internal static caches exposed for diagnostics and the warmup service.
        internal static ConcurrentDictionary<int, float[]> EmbeddingCache => _embeddingCache;
        internal static ConcurrentDictionary<int, KnowledgeBaseChunkDto> MetadataCache => _metadataCache;

        internal static void ReplaceEmbeddingCache(
            IDictionary<int, float[]> embeddings,
            IDictionary<int, KnowledgeBaseChunkDto> metadata)
        {
            _embeddingCache = new ConcurrentDictionary<int, float[]>(embeddings);
            _metadataCache = new ConcurrentDictionary<int, KnowledgeBaseChunkDto>(metadata);
            _lastEmbeddingCacheRefreshUtc = DateTime.UtcNow;
        }

        public KnowledgeBaseSearchService(JIFAS_AssistantContext db, ILoggerService logger, ICacheService cacheService)
        {
            _db = db;
            _logger = logger;
            _cacheService = cacheService;
        }

        // Common stopwords that carry no domain meaning
        private static readonly HashSet<string> _stopwords = new(StringComparer.OrdinalIgnoreCase)
        {
            "apa","itu","ini","yang","dan","ke","di","dari","untuk","dengan","adalah","ada",
            "bisa","cara","bagaimana","siapakah","siapa","apakah","kenapa","mengapa","kapan",
            "dimana","tolong","mohon","bantu","saya","aku","kamu","kami","kita","mereka",
            "the","is","are","what","how","why","when","where","who","can","please","help"
        };

        public async Task<List<KnowledgeBaseChunkDto>> SearchByKeywordAsync(string query, int topK = 5, string correlationId = null)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var logMsg = string.IsNullOrEmpty(correlationId) 
                    ? $"[KnowledgeBaseSearchService] Keyword search: {query}"
                    : $"[{correlationId}] Keyword search: {query}";
                _logger.LogInformation(logMsg);

                var allKeywords = query.ToLower()
                    .Split(new[] { ' ', '?', '!', ',', '.', ';', ':' }, StringSplitOptions.RemoveEmptyEntries);

                // Prefer meaningful keywords (non-stopwords), fall back to all keywords
                var meaningfulKeywords = allKeywords.Where(k => !_stopwords.Contains(k) && k.Length > 2).ToArray();
                var keywords = meaningfulKeywords.Length > 0 ? meaningfulKeywords : allKeywords;

                if (keywords.Length == 0)
                    return new List<KnowledgeBaseChunkDto>();

                // Build SQL-side LIKE filters for each keyword (push filtering to DB, not in-memory)
                var baseQuery = _db.KnowledgeBaseChunks
                    .Include(c => c.Document)
                    .Where(c => c.Document != null && c.Document.IsActive == true);

                // Use top 3 keywords for SQL LIKE
                var topKeywords = keywords.Take(3).ToArray();
                var p0 = $"%{topKeywords[0]}%";
                var p1 = topKeywords.Length > 1 ? $"%{topKeywords[1]}%" : null;
                var p2 = topKeywords.Length > 2 ? $"%{topKeywords[2]}%" : null;

                var filteredQuery = baseQuery.Where(c =>
                    (EF.Functions.Like(c.Content, p0) || EF.Functions.Like(c.Document.Title, p0) || EF.Functions.Like(c.Document.Category, p0))
                    || (p1 != null && (EF.Functions.Like(c.Content, p1) || EF.Functions.Like(c.Document.Title, p1) || EF.Functions.Like(c.Document.Category, p1)))
                    || (p2 != null && (EF.Functions.Like(c.Content, p2) || EF.Functions.Like(c.Document.Title, p2) || EF.Functions.Like(c.Document.Category, p2))));

                var matchedChunks = await filteredQuery
                    .Take(topK * 5)
                    .ToListAsync();

                stopwatch.Stop();
                if (!string.IsNullOrEmpty(correlationId))
                {
                    _logger.LogPerformance("KBKeywordSearch", stopwatch.ElapsedMilliseconds, correlationId);
                }

                var results = matchedChunks
                    .Select(c =>
                    {
                        var contentLower = c.Content?.ToLower() ?? "";
                        var titleLower = c.Document?.Title?.ToLower() ?? "";
                        var categoryLower = c.Document?.Category?.ToLower() ?? "";

                        var titleMatches = keywords.Count(k => titleLower.Contains(k));
                        var contentMatches = keywords.Count(k => contentLower.Contains(k));
                        var relevanceScore = (titleMatches * 2.0 + contentMatches) / (keywords.Length * 3.0);

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

                if (_db.Database.IsNpgsql())
                {
                    var pgResults = await SearchByPgVectorAsync(embedding, topK);
                    if (pgResults.Count > 0)
                    {
                        stopwatch.Stop();
                        if (!string.IsNullOrEmpty(correlationId))
                        {
                            _logger.LogPerformance("KBSemanticSearchPgVector", stopwatch.ElapsedMilliseconds, correlationId);
                        }

                        return pgResults;
                    }
                }

                // Use the static embedding cache — if warm, no DB/JSON needed
                var needsLoad = NeedsEmbeddingCacheLoad();

                if (needsLoad)
                {
                    await EmbeddingCacheLoadLock.WaitAsync();
                    try
                    {
                        if (NeedsEmbeddingCacheLoad())
                        {
                            await ReloadEmbeddingCacheAsync();
                        }
                    }
                    finally
                    {
                        EmbeddingCacheLoadLock.Release();
                    }
                }

                // Compute cosine similarity from a stable snapshot of the in-memory cache.
                var embeddingSnapshot = _embeddingCache;
                var metadataSnapshot = _metadataCache;

                var results = embeddingSnapshot
                    .AsParallel()
                    .Select(kv =>
                    {
                        var similarity = CosineSimilarity(embedding, kv.Value);
                        if (similarity < 0.3f) return null;

                        if (!metadataSnapshot.TryGetValue(kv.Key, out var meta)) return null;

                        return new KnowledgeBaseChunkDto
                        {
                            Id = meta.Id,
                            DocumentId = meta.DocumentId,
                            Title = meta.Title,
                            Content = meta.Content,
                            Category = meta.Category,
                            ChunkIndex = meta.ChunkIndex,
                            RelevanceScore = similarity
                        };
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
            // Check cache for keyword-only searches (semantic results vary)
            var useCache = embedding == null;
            var cacheKey = useCache ? $"KB_Search_{Utilities.HashHelper.ToShortStableHash(query)}_{topK}" : null;
            if (useCache)
            {
                var cached = _cacheService.Get<List<KnowledgeBaseChunkDto>>(cacheKey);
                if (cached != null)
                {
                    _logger.LogInformation($"[KnowledgeBaseSearchService] Cache HIT for: {query}");
                    return cached;
                }
            }

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

                // Cache keyword-only results
                if (useCache && finalResults.Count > 0)
                {
                    _cacheService.Set(cacheKey, finalResults, KB_CACHE_MINUTES);
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

        private static bool NeedsEmbeddingCacheLoad()
        {
            return _embeddingCache.IsEmpty ||
                DateTime.UtcNow - _lastEmbeddingCacheRefreshUtc > EmbeddingCacheTtl;
        }

        private async Task<List<KnowledgeBaseChunkDto>> SearchByPgVectorAsync(float[] embedding, int topK)
        {
            try
            {
                var queryVector = new Vector(embedding);
                var rows = await _db.KnowledgeBaseChunks
                    .AsNoTracking()
                    .Where(c => c.Document != null &&
                        c.Document.IsActive == true &&
                        c.EmbeddingVector != null)
                    .Select(c => new
                    {
                        c.Id,
                        c.DocumentId,
                        c.ChunkIndex,
                        c.Content,
                        Title = c.Document.Title,
                        Category = c.Document.Category,
                        Distance = c.EmbeddingVector.CosineDistance(queryVector)
                    })
                    .OrderBy(x => x.Distance)
                    .Take(topK)
                    .ToListAsync();

                return rows
                    .Where(r => r.Distance <= 0.7)
                    .Select(r => new KnowledgeBaseChunkDto
                    {
                        Id = r.Id,
                        DocumentId = r.DocumentId,
                        Title = r.Title,
                        Content = r.Content,
                        Category = r.Category,
                        ChunkIndex = r.ChunkIndex,
                        RelevanceScore = Math.Max(0, 1 - r.Distance)
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[KnowledgeBaseSearchService] pgvector semantic search failed, falling back to in-memory search: {0}", ex.Message);
                return new List<KnowledgeBaseChunkDto>();
            }
        }

        private async Task ReloadEmbeddingCacheAsync()
        {
            var chunks = await _db.KnowledgeBaseChunks
                .Include(c => c.Document)
                .Where(c => c.Document != null && c.Document.IsActive == true && c.Embedding != null)
                .AsNoTracking()
                .ToListAsync();

            var embeddings = new Dictionary<int, float[]>();
            var metadata = new Dictionary<int, KnowledgeBaseChunkDto>();

            foreach (var c in chunks)
            {
                try
                {
                    var parsed = EmbeddingSerializer.Deserialize(c.Embedding);
                    if (parsed.Length == 0)
                    {
                        continue;
                    }

                    embeddings[c.Id] = parsed;
                    metadata[c.Id] = new KnowledgeBaseChunkDto
                    {
                        Id = c.Id,
                        DocumentId = c.DocumentId,
                        Title = c.Document?.Title,
                        Content = c.Content,
                        Category = c.Document?.Category,
                        ChunkIndex = c.ChunkIndex
                    };
                }
                catch (Exception parseEx)
                {
                    _logger.LogWarning("[KnowledgeBaseSearchService] Skipping malformed embedding for chunk {0}: {1}", c.Id, parseEx.Message);
                }
            }

            if (embeddings.Count > 0)
            {
                ReplaceEmbeddingCache(embeddings, metadata);
                _logger.LogInformation("[KnowledgeBaseSearchService] Embedding cache loaded atomically: {0} chunks", embeddings.Count);
            }
            else
            {
                _logger.LogWarning("[KnowledgeBaseSearchService] Embedding cache reload produced 0 chunks; keeping previous cache");
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

        private static string EscapeLikePattern(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_")
                .Replace("[", "\\[");
        }
    }
}
