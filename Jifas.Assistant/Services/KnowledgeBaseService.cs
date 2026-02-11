using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using Jifas.Chatbot.DAL;
using Newtonsoft.Json;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Knowledge Base service for JIFAS AI Assistant
    /// Performs semantic search on KB documents and chunks
    /// Uses Qdrant vector DB as primary (if enabled), SQL Server as fallback
    /// Phase 6C: Added Qdrant integration with SQL fallback
    /// </summary>
    public class KnowledgeBaseService : IKnowledgeBaseService
    {
        private readonly JIFAS_AssistantEntities _db;
        private readonly IEmbeddingService _embeddingService;
        private readonly ILoggerService _logger;
        private readonly ICacheService _cacheService;
        private readonly IQdrantVectorService _qdrantService;

        /// <summary>
        /// Initialize embedding service based on configuration
        /// Prefers Gemini (if API key available), falls back to OpenAI
        /// </summary>
        private static IEmbeddingService InitializeEmbeddingService()
        {
            var geminiKey = System.Configuration.ConfigurationManager.AppSettings["Gemini:ApiKey"];
            if (!string.IsNullOrEmpty(geminiKey))
            {
                return new GeminiEmbeddingService();
            }
            return new OpenAIEmbeddingService();
        }

        public KnowledgeBaseService()
        {
            _db = new JIFAS_AssistantEntities();
            _logger = LoggerFactory.GetLogger();
            _cacheService = new MemoryCacheService();
            _embeddingService = InitializeEmbeddingService();
            _qdrantService = new QdrantVectorService(_embeddingService);
            
            _logger.LogInformation("[KnowledgeBaseService] Initialized with {0} embeddings", 
                _embeddingService.GetType().Name.Contains("Gemini") ? "Gemini" : "OpenAI");
        }

        public KnowledgeBaseService(JIFAS_AssistantEntities db)
        {
            _db = db;
            _logger = LoggerFactory.GetLogger();
            _cacheService = new MemoryCacheService();
            _embeddingService = InitializeEmbeddingService();
            _qdrantService = new QdrantVectorService(_embeddingService);
        }

        public KnowledgeBaseService(JIFAS_AssistantEntities db, IEmbeddingService embeddingService)
        {
            _db = db;
            _embeddingService = embeddingService;
            _logger = LoggerFactory.GetLogger();
            _cacheService = new MemoryCacheService();
            _qdrantService = new QdrantVectorService(embeddingService);
        }

        public KnowledgeBaseService(JIFAS_AssistantEntities db, IEmbeddingService embeddingService, IQdrantVectorService qdrantService)
        {
            _db = db;
            _embeddingService = embeddingService;
            _qdrantService = qdrantService;
            _logger = LoggerFactory.GetLogger();
            _cacheService = new MemoryCacheService();
        }

        public async Task<List<KnowledgeBaseResult>> SearchAsync(string query, int topK = 3)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                    return new List<KnowledgeBaseResult>();

                // Check if cache is enabled
                var enableCache = bool.TryParse(System.Configuration.ConfigurationManager.AppSettings["Caching:EnableKBCache"], out var cacheEnabled) 
                    ? cacheEnabled 
                    : true;

                // Check cache first
                if (enableCache)
                {
                    var cacheKey = $"KB_Search_{query.GetHashCode()}_{topK}";
                    var cachedResult = _cacheService.Get<List<KnowledgeBaseResult>>(cacheKey);
                    
                    if (cachedResult != null)
                    {
                        _logger.LogInformation("[KB Search] Cache HIT for query: {0}", query);
                        return cachedResult;
                    }
                }

                _logger.LogInformation("[KB Search] Query: {0}", query);

                // Phase 6C: Try Qdrant first (if enabled)
                var useQdrant = bool.TryParse(System.Configuration.ConfigurationManager.AppSettings["Search:UseQdrant"], out var qdrantEnabled) 
                    ? qdrantEnabled 
                    : false;

                List<KnowledgeBaseResult> results = null;

                if (useQdrant)
                {
                    try
                    {
                        _logger.LogInformation("[KB Search] Attempting Qdrant search");
                        var qdrantTopK = int.TryParse(ConfigurationManager.AppSettings["Search:QdrantTopK"], out var k) ? k : 5;
                        results = await _qdrantService.SearchAsync(query, qdrantTopK);
                        
                        if (results != null && results.Count > 0)
                        {
                            _logger.LogInformation("[KB Search] Qdrant returned {0} results", results.Count);
                            // Apply re-ranking and other options to Qdrant results
                            results = await ApplyPostSearchEnhancementsAsync(results, query, topK);
                            
                            // Cache results before returning
                            if (enableCache)
                            {
                                var cacheKey = $"KB_Search_{query.GetHashCode()}_{topK}";
                                var cacheDuration = int.TryParse(ConfigurationManager.AppSettings["Caching:KBSearchCacheDurationMinutes"], out var duration) 
                                    ? duration 
                                    : 30;
                                _cacheService.Set(cacheKey, results, cacheDuration);
                            }
                            
                            return results;
                        }
                        else
                        {
                            _logger.LogWarning("[KB Search] Qdrant returned no results, falling back to SQL");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("[KB Search] Qdrant search failed: {0}, falling back to SQL", ex.Message);
                    }
                }

                // SQL fallback: Use original semantic search logic
                results = await SearchSQLAsync(query, topK);

                // Cache results before returning
                if (enableCache && results.Count > 0)
                {
                    var cacheKey = $"KB_Search_{query.GetHashCode()}_{topK}";
                    var cacheDuration = int.TryParse(ConfigurationManager.AppSettings["Caching:KBSearchCacheDurationMinutes"], out var duration) 
                        ? duration 
                        : 30;
                    _cacheService.Set(cacheKey, results, cacheDuration);
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError("[KB Search] Error in search", ex);
                return await KeywordSearchAsync(query, topK);
            }
        }

        /// <summary>
        /// Apply post-search enhancements (re-ranking with metadata/popularity boost) to any result set
        /// Reusable for both Qdrant and SQL results
        /// </summary>
        private async Task<List<KnowledgeBaseResult>> ApplyPostSearchEnhancementsAsync(List<KnowledgeBaseResult> initialResults, string query, int topK)
        {
            try
            {
                if (initialResults == null || initialResults.Count == 0)
                    return initialResults;

                // Extract keywords for re-ranking
                var keywords = ExtractKeywords(query);

                // Get document popularity scores for reranking
                var docPopularity = await CalculateDocumentPopularityAsync();

                // Apply re-ranking boost to results
                var rerankedResults = new List<KnowledgeBaseResult>();
                foreach (var result in initialResults)
                {
                    var boost = CalculateRerankerBoost(result, keywords, docPopularity);
                    result.Score += boost;
                    rerankedResults.Add(result);
                }

                // Re-sort by adjusted score and return top K
                return rerankedResults
                    .OrderByDescending(r => r.Score)
                    .Take(topK)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[KB Search] Error applying post-search enhancements: {0}", ex.Message);
                return initialResults.Take(topK).ToList();
            }
        }

        /// <summary>
        /// Phase 6C: Original SQL-based semantic search (now a separate method for clarity)
        /// Called as fallback when Qdrant is disabled or unavailable
        /// </summary>
        private async Task<List<KnowledgeBaseResult>> SearchSQLAsync(string query, int topK = 3)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                    return new List<KnowledgeBaseResult>();

                _logger.LogInformation("[KB Search SQL] Query: {0}", query);

                // Generate query embedding for semantic search
                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
                
                if (queryEmbedding == null || queryEmbedding.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[KB Search SQL] Failed to generate query embedding, falling back to keyword search");
                    return await KeywordSearchAsync(query, topK);
                }

                System.Diagnostics.Debug.WriteLine($"[KB Search SQL] Generated {queryEmbedding.Count}-dim query embedding");

                // Extract keywords for hybrid scoring
                var keywords = ExtractKeywords(query);
                System.Diagnostics.Debug.WriteLine($"[KB Search SQL] Extracted keywords: {string.Join(", ", keywords)}");

                // Get all chunks with embeddings
                var chunks = await _db.KnowledgeBaseChunks
                    .Include(c => c.KnowledgeBaseDocuments)
                    .Where(c => c.KnowledgeBaseDocuments.IsActive == true && c.EmbeddingVector != null)
                    .ToListAsync();

                System.Diagnostics.Debug.WriteLine($"[KB Search SQL] Found {chunks.Count} chunks with embeddings");

                if (chunks.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[KB Search SQL] No embeddings found, falling back to keyword search");
                    return await KeywordSearchAsync(query, topK);
                }

                // Get document popularity scores for reranking
                var docPopularity = await CalculateDocumentPopularityAsync();

                // Calculate hybrid + reranked scores (semantic + keyword + metadata + popularity)
                var rankedChunks = new List<(KnowledgeBaseChunks chunk, double semanticScore, double keywordScore, double hybridScore, double finalScore)>();

                foreach (var chunk in chunks)
                {
                    try
                    {
                        var chunkEmbedding = JsonConvert.DeserializeObject<List<float>>(chunk.EmbeddingVector);
                        if (chunkEmbedding != null && chunkEmbedding.Count > 0)
                        {
                            // Semantic score (vector similarity)
                            var semanticScore = _embeddingService.CalculateCosineSimilarity(queryEmbedding, chunkEmbedding);
                            
                            // Keyword score (text-based relevance)
                            var keywordScore = CalculateChunkRelevanceScore(chunk, keywords);
                            
                            // Hybrid score: 60% semantic, 40% keyword (balanced approach)
                            var hybridScore = (semanticScore * 0.6) + (keywordScore * 0.4);
                            
                            // Reranking boost: metadata + popularity
                            var rerankerBoost = CalculateRerankerBoost(chunk, keywords, docPopularity, query);
                            
                            // Final score: hybrid + reranking boost
                            var finalScore = hybridScore + rerankerBoost;
                            
                            rankedChunks.Add((chunk, semanticScore, keywordScore, hybridScore, finalScore));
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[KB Search SQL] Error parsing embedding for chunk {chunk.Id}: {ex.Message}");
                    }
                }

                // Rank by final score (hybrid + reranking)
                var topChunks = rankedChunks
                    .OrderByDescending(x => x.finalScore)
                    .Take(topK)
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"[KB Search SQL] Top {topChunks.Count} reranked results:");
                foreach (var (chunk, semScore, kwScore, hybridScore, finalScore) in topChunks)
                {
                    System.Diagnostics.Debug.WriteLine($"  - Final: {finalScore:F3} (Hybrid: {hybridScore:F3} [Semantic: {semScore:F3}, Keyword: {kwScore:F3}]) | Doc: {chunk.KnowledgeBaseDocuments?.Title}");
                }

                // Convert to results using final scores
                var minRelevanceScore = double.TryParse(ConfigurationManager.AppSettings["KnowledgeBase:MinRelevanceScore"], out var threshold) ? threshold : 0.3;
                var results = topChunks
                    .Where(x => x.finalScore > minRelevanceScore)
                    .Select(x => new KnowledgeBaseResult
                    {
                        DocumentId = x.chunk.DocumentId,
                        Title = x.chunk.KnowledgeBaseDocuments?.Title ?? "JIFAS Document",
                        Content = x.chunk.Content,
                        Category = x.chunk.KnowledgeBaseDocuments?.Category ?? "General",
                        Department = x.chunk.KnowledgeBaseDocuments?.Department ?? "JIFAS",
                        Score = x.finalScore
                    })
                    .ToList();

                // If no good matches, fallback to keyword search
                if (results.Count == 0)
                {
                    _logger.LogWarning("[KB Search SQL] No reranked matches, falling back to keyword search");
                    return await KeywordSearchAsync(query, topK);
                }

                _logger.LogInformation("[KB Search SQL] Returning {0} reranked results", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError("[KB Search SQL] Error in search", ex);
                return await KeywordSearchAsync(query, topK);
            }
        }

        /// <summary>
        /// Fallback keyword-based search when embeddings not available
        /// </summary>
        private async Task<List<KnowledgeBaseResult>> KeywordSearchAsync(string query, int topK = 3)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                    return new List<KnowledgeBaseResult>();

                var keywords = ExtractKeywords(query);
                var results = new List<KnowledgeBaseResult>();

                // Search chunks first (more granular results)
                var chunks = await _db.KnowledgeBaseChunks
                    .Include(c => c.KnowledgeBaseDocuments)
                    .Where(c => c.KnowledgeBaseDocuments.IsActive == true)
                    .ToListAsync();

                foreach (var chunk in chunks)
                {
                    var score = CalculateChunkRelevanceScore(chunk, keywords);
                    if (score > 0.3)  // Minimum relevance threshold
                    {
                        var existingResult = results.FirstOrDefault(r => r.DocumentId == chunk.DocumentId);
                        if (existingResult != null)
                        {
                            // Keep highest score
                            if (score > existingResult.Score)
                            {
                                existingResult.Content = chunk.Content;
                                existingResult.Score = score;
                            }
                        }
                        else
                        {
                            results.Add(new KnowledgeBaseResult
                            {
                                DocumentId = chunk.DocumentId,
                                Title = chunk.KnowledgeBaseDocuments?.Title ?? "JIFAS Document",
                                Content = chunk.Content,
                                Category = chunk.KnowledgeBaseDocuments?.Category ?? "General",
                                Department = chunk.KnowledgeBaseDocuments?.Department ?? "JIFAS",
                                Score = score
                            });
                        }
                    }
                }

                // If no chunks found, search full documents
                if (results.Count == 0)
                {
                    var documents = await _db.KnowledgeBaseDocuments
                        .Where(d => d.IsActive == true)
                        .ToListAsync();

                    foreach (var doc in documents)
                    {
                        var score = CalculateRelevanceScore(doc, keywords);
                        if (score > 0.3)
                        {
                            results.Add(new KnowledgeBaseResult
                            {
                                DocumentId = doc.Id,
                                Title = doc.Title,
                                Content = doc.Content,
                                Category = doc.Category,
                                Department = doc.Department,
                                Score = score
                            });
                        }
                    }
                }

                // Sort by score and return top K
                return results
                    .OrderByDescending(r => r.Score)
                    .Take(topK)
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[KnowledgeBaseService] Search Error: {ex.Message}");
                return new List<KnowledgeBaseResult>();
            }
        }

        public async Task<List<KnowledgeBaseResult>> GetAllDocumentsAsync()
        {
            try
            {
                var documents = await _db.KnowledgeBaseDocuments
                    .Where(d => d.IsActive == true)
                    .ToListAsync();

                return documents.Select(d => new KnowledgeBaseResult
                {
                    DocumentId = d.Id,
                    Title = d.Title,
                    Content = d.Content,
                    Category = d.Category,
                    Department = d.Department,
                    Score = 1.0
                }).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[KnowledgeBaseService] GetAll Error: {ex.Message}");
                return new List<KnowledgeBaseResult>();
            }
        }

        public async Task<KnowledgeBaseResult> GetDocumentByIdAsync(int id)
        {
            try
            {
                var doc = await _db.KnowledgeBaseDocuments
                    .FirstOrDefaultAsync(d => d.Id == id && d.IsActive == true);

                if (doc == null)
                    return null;

                return new KnowledgeBaseResult
                {
                    DocumentId = doc.Id,
                    Title = doc.Title,
                    Content = doc.Content,
                    Category = doc.Category,
                    Department = doc.Department,
                    Score = 1.0
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[KnowledgeBaseService] GetById Error: {ex.Message}");
                return null;
            }
        }

        private List<string> ExtractKeywords(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<string>();

            // Remove common stop words
            var stopWords = new HashSet<string>
            {
                "yang", "dan", "di", "ke", "dari", "untuk", "dengan", "pada",
                "adalah", "ini", "itu", "atau", "juga", "saya", "aku", "kamu",
                "bagaimana", "apa", "dimana", "kapan", "siapa", "mengapa",
                "cara", "bisa", "tidak", "ada", "mau", "ingin", "the", "a", "an",
                "is", "are", "was", "were", "be", "been", "being", "have", "has",
                "how", "what", "where", "when", "who", "why", "can", "could"
            };

            var words = query.ToLower()
                .Split(new[] { ' ', ',', '.', '?', '!', ';', ':', '-', '(', ')', '[', ']' }, 
                       StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2 && !stopWords.Contains(w))
                .Distinct()
                .ToList();

            return words;
        }

        private double CalculateRelevanceScore(KnowledgeBaseDocuments doc, List<string> keywords)
        {
            if (doc == null || keywords == null || keywords.Count == 0)
                return 0;

            double score = 0;
            var content = (doc.Content ?? "").ToLower();
            var title = (doc.Title ?? "").ToLower();
            var category = (doc.Category ?? "").ToLower();
            var tags = (doc.Tags ?? "").ToLower();

            foreach (var keyword in keywords)
            {
                if (string.IsNullOrEmpty(keyword))
                    continue;

                // Title match (highest weight)
                if (title.Contains(keyword))
                    score += 3.0;

                // Category match
                if (category.Contains(keyword))
                    score += 2.0;

                // Tags match
                if (tags.Contains(keyword))
                    score += 2.0;

                // Content match
                if (content.Contains(keyword))
                    score += 1.0;

                // Count occurrences in content (limited boost)
                var occurrences = CountOccurrences(content, keyword);
                score += Math.Min(occurrences * 0.1, 1.0);
            }

            // Normalize by keyword count
            return score / keywords.Count;
        }

        private double CalculateChunkRelevanceScore(KnowledgeBaseChunks chunk, List<string> keywords)
        {
            if (chunk == null || keywords == null || keywords.Count == 0)
                return 0;

            double score = 0;
            var content = (chunk.Content ?? "").ToLower();

            foreach (var keyword in keywords)
            {
                if (string.IsNullOrEmpty(keyword))
                    continue;

                if (content.Contains(keyword))
                    score += 1.0;

                var occurrences = CountOccurrences(content, keyword);
                score += Math.Min(occurrences * 0.2, 2.0);
            }

            return score / keywords.Count;
        }

        private int CountOccurrences(string text, string keyword)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(keyword, index, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                count++;
                index += keyword.Length;
            }
            return count;
        }

        /// <summary>
        /// Calculate reranking boost for KnowledgeBaseResult objects (used with Qdrant results)
        /// </summary>
        private double CalculateRerankerBoost(KnowledgeBaseResult result, List<string> keywords, 
            Dictionary<int, DocumentPopularity> docPopularity)
        {
            double boost = 0;

            if (result == null || keywords == null || keywords.Count == 0)
                return boost;

            // 1. Category/Department match boost (0.0-0.15)
            if (!string.IsNullOrEmpty(result.Category))
            {
                var categoryLower = result.Category.ToLower();
                if (keywords.Any(k => !string.IsNullOrEmpty(k) && categoryLower.Contains(k)))
                {
                    boost += 0.10;
                }
            }

            if (!string.IsNullOrEmpty(result.Department))
            {
                var deptLower = result.Department.ToLower();
                if (keywords.Any(k => !string.IsNullOrEmpty(k) && deptLower.Contains(k)))
                {
                    boost += 0.05;
                }
            }

            // 2. Popularity boost (0.0-0.20)
            if (docPopularity.ContainsKey(result.DocumentId))
            {
                var popularity = docPopularity[result.DocumentId];
                
                var frequencyBoost = Math.Min(popularity.HitCount * 0.01, 0.10);
                boost += frequencyBoost;

                var daysSinceLastAccess = (DateTime.Now - popularity.LastAccessedAt).TotalDays;
                if (daysSinceLastAccess < 7)
                    boost += 0.05;
                else if (daysSinceLastAccess < 30)
                    boost += 0.025;

                if (popularity.AverageConfidence > 0.75)
                    boost += 0.05;
                else if (popularity.AverageConfidence > 0.60)
                    boost += 0.025;
            }

            return boost;
        }

        /// <summary>
        /// Calculate reranking boost based on metadata and popularity (for SQL chunks)
        /// Boosts results that match category/department and have high usage
        /// </summary>
        private double CalculateRerankerBoost(KnowledgeBaseChunks chunk, List<string> keywords, 
            Dictionary<int, DocumentPopularity> docPopularity, string query)
        {
            double boost = 0;
            var doc = chunk?.KnowledgeBaseDocuments;

            if (doc == null || keywords == null || keywords.Count == 0)
                return boost;

            // 1. Category/Department match boost (0.0-0.15)
            if (!string.IsNullOrEmpty(doc.Category))
            {
                var categoryLower = doc.Category.ToLower();
                if (keywords.Any(k => !string.IsNullOrEmpty(k) && categoryLower.Contains(k)))
                {
                    boost += 0.10; // Category contains query keyword
                }
            }

            if (!string.IsNullOrEmpty(doc.Department))
            {
                var deptLower = doc.Department.ToLower();
                if (keywords.Any(k => !string.IsNullOrEmpty(k) && deptLower.Contains(k)))
                {
                    boost += 0.05; // Department contains query keyword
                }
            }

            // 2. Popularity boost (0.0-0.20)
            if (docPopularity.ContainsKey(doc.Id))
            {
                var popularity = docPopularity[doc.Id];
                
                // Usage frequency boost (normalized to 0-0.10)
                var frequencyBoost = Math.Min(popularity.HitCount * 0.01, 0.10);
                boost += frequencyBoost;

                // Recency boost (0-0.05) - prefer recently accessed docs
                var daysSinceLastAccess = (DateTime.Now - popularity.LastAccessedAt).TotalDays;
                if (daysSinceLastAccess < 7) // Last week = max boost
                    boost += 0.05;
                else if (daysSinceLastAccess < 30) // Last month = half boost
                    boost += 0.025;

                // Confidence boost (0-0.05) - prefer docs with consistent high confidence
                if (popularity.AverageConfidence > 0.75)
                    boost += 0.05;
                else if (popularity.AverageConfidence > 0.60)
                    boost += 0.025;
            }

            System.Diagnostics.Debug.WriteLine($"[KB Search] Reranker boost for doc '{doc.Title}': +{boost:F3}");
            return boost;
        }

        /// <summary>
        /// Calculate popularity scores for all documents based on conversation history
        /// Returns dict of DocumentId -> Popularity metrics
        /// </summary>
        private async Task<Dictionary<int, DocumentPopularity>> CalculateDocumentPopularityAsync()
        {
            var result = new Dictionary<int, DocumentPopularity>();

            try
            {
                // Fix N+1 query problem: Get all conversations at once
                var conversations = await _db.Conversations
                    .Where(c => c.IsFromKnowledgeBase == true)
                    .ToListAsync();

                // Get all active documents once
                var allDocuments = await _db.KnowledgeBaseDocuments
                    .Where(d => d.IsActive == true)
                    .ToListAsync();

                // Group by category to infer document hits
                var categoryGroups = conversations.GroupBy(c => c.Category ?? "general");

                foreach (var group in categoryGroups)
                {
                    // Find matching document based on category (using in-memory search)
                    var doc = allDocuments.FirstOrDefault(d => 
                        d.Category == group.Key || 
                        (d.Title != null && d.Title.ToLower().Contains(group.Key.ToLower())));

                    if (doc != null && !result.ContainsKey(doc.Id))
                    {
                        var hitCount = group.Count();
                        var avgConfidence = group.Average(c => c.ConfidenceScore ?? 0);
                        var lastAccessed = group.OrderByDescending(c => c.CreatedAt).FirstOrDefault()?.CreatedAt ?? DateTime.Now;

                        result[doc.Id] = new DocumentPopularity
                        {
                            DocumentId = doc.Id,
                            HitCount = hitCount,
                            AverageConfidence = avgConfidence,
                            LastAccessedAt = lastAccessed
                        };

                        System.Diagnostics.Debug.WriteLine(
                            $"[KB Search] Doc popularity: ID={doc.Id}, Title='{doc.Title}', Hits={hitCount}, AvgConf={avgConfidence:F2}, LastAccess={lastAccessed:g}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[KB Search] Error calculating popularity: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Helper class for tracking document popularity metrics
        /// </summary>
        private class DocumentPopularity
        {
            public int DocumentId { get; set; }
            public int HitCount { get; set; }
            public double AverageConfidence { get; set; }
            public DateTime LastAccessedAt { get; set; }
        }
    }
}
