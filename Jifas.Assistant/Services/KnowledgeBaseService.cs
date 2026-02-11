using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Jifas.Assistant.Data;
using Jifas.Assistant.Data.Models;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Knowledge Base service for JIFAS AI Assistant
    /// Performs semantic search on KB documents using embeddings
    /// Supports hybrid search (semantic + keyword), re-ranking, and popularity boosting
    /// </summary>
    public class KnowledgeBaseService : IKnowledgeBaseService
    {
        private readonly ILoggerService _logger;
        private readonly IConfiguration _configuration;
        private readonly JifasAssistantDbContext _db;
        private readonly IEmbeddingService _embeddingService;
        private readonly ICacheService _cacheService;

        public KnowledgeBaseService(
            ILoggerService logger,
            IConfiguration configuration,
            JifasAssistantDbContext db,
            IEmbeddingService embeddingService,
            ICacheService cacheService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));

            _logger.LogInformation("[KnowledgeBaseService] Initialized with Gemini embeddings");
        }

        /// <summary>
        /// Search knowledge base with semantic search + keyword matching
        /// Supports caching and fallback to keyword search if semantic fails
        /// </summary>
        public async Task<List<KnowledgeBaseResult>> SearchAsync(string query, int topK = 3)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                    return new List<KnowledgeBaseResult>();

                // Check cache first
                var enableCache = _configuration.GetValue("Caching:EnableKBCache", true);
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

                _logger.LogInformation("[KB Search] Searching for: {0}", query);

                // Try semantic search with embeddings
                var results = await SearchWithEmbeddingsAsync(query, topK);

                // If no results, fallback to keyword search
                if (results.Count == 0)
                {
                    _logger.LogWarning("[KB Search] No semantic results, falling back to keyword search");
                    results = await KeywordSearchAsync(query, topK);
                }

                // Cache results before returning
                if (enableCache && results.Count > 0)
                {
                    var cacheKey = $"KB_Search_{query.GetHashCode()}_{topK}";
                    var cacheDurationMinutes = _configuration.GetValue("Caching:KBSearchCacheDurationMinutes", 30);
                    _cacheService.Set(cacheKey, results, cacheDurationMinutes);
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError("[KB Search] Error in search: {0}", ex, ex.Message);
                return await KeywordSearchAsync(query, topK);
            }
        }

        /// <summary>
        /// Semantic search using embeddings and cosine similarity
        /// </summary>
        private async Task<List<KnowledgeBaseResult>> SearchWithEmbeddingsAsync(string query, int topK = 3)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                    return new List<KnowledgeBaseResult>();

                _logger.LogInformation("[KB Search Embeddings] Generating query embedding");

                // Generate query embedding
                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
                if (queryEmbedding == null || queryEmbedding.Count == 0)
                {
                    _logger.LogWarning("[KB Search Embeddings] Failed to generate query embedding");
                    return new List<KnowledgeBaseResult>();
                }

                _logger.LogInformation("[KB Search Embeddings] Generated {0}-dimensional embedding", queryEmbedding.Count);

                // Get all active documents with embeddings
                var documents = await _db.KnowledgeBaseDocuments
                    .Where(d => d.IsActive == true && d.Embedding != null)
                    .ToListAsync();

                _logger.LogInformation("[KB Search Embeddings] Found {0} documents with embeddings", documents.Count);

                if (documents.Count == 0)
                    return new List<KnowledgeBaseResult>();

                // Extract keywords for hybrid scoring
                var keywords = ExtractKeywords(query);
                _logger.LogInformation("[KB Search Embeddings] Extracted {0} keywords", keywords.Count);

                // Calculate scores for each document
                var scoredDocuments = new List<(KnowledgeBaseDocument doc, double semanticScore, double keywordScore, double finalScore)>();

                foreach (var doc in documents)
                {
                    try
                    {
                        var docEmbedding = JsonConvert.DeserializeObject<List<float>>(doc.Embedding);
                        if (docEmbedding == null || docEmbedding.Count == 0)
                            continue;

                        // Semantic score (cosine similarity)
                        var semanticScore = _embeddingService.CalculateCosineSimilarity(queryEmbedding, docEmbedding);

                        // Keyword score
                        var keywordScore = CalculateRelevanceScore(doc, keywords);

                        // Hybrid score: 70% semantic, 30% keyword
                        var hybridScore = (semanticScore * 0.7) + (keywordScore * 0.3);

                        scoredDocuments.Add((doc, semanticScore, keywordScore, hybridScore));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("[KB Search Embeddings] Error scoring document {0}: {1}", doc.Id, ex.Message);
                    }
                }

                // Get top results by hybrid score
                var minRelevance = _configuration.GetValue("KnowledgeBase:MinRelevanceScore", 0.3);
                var topDocuments = scoredDocuments
                    .Where(x => x.finalScore >= minRelevance)
                    .OrderByDescending(x => x.finalScore)
                    .Take(topK)
                    .ToList();

                _logger.LogInformation("[KB Search Embeddings] Returning {0} results", topDocuments.Count);

                return topDocuments
                    .Select(x => new KnowledgeBaseResult
                    {
                        DocumentId = x.doc.Id,
                        Title = x.doc.Title,
                        Content = x.doc.Content,
                        Category = x.doc.Category ?? "General",
                        Score = x.finalScore
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError("[KB Search Embeddings] Error in embedding search: {0}", ex, ex.Message);
                return new List<KnowledgeBaseResult>();
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

                // Get all active documents
                var documents = await _db.KnowledgeBaseDocuments
                    .Where(d => d.IsActive == true)
                    .ToListAsync();

                _logger.LogInformation("[KB Keyword Search] Searching {0} documents for keywords", documents.Count);

                foreach (var doc in documents)
                {
                    var score = CalculateRelevanceScore(doc, keywords);
                    if (score > 0.2)  // Minimum relevance threshold
                    {
                        results.Add(new KnowledgeBaseResult
                        {
                            DocumentId = doc.Id,
                            Title = doc.Title,
                            Content = doc.Content,
                            Category = doc.Category ?? "General",
                            Score = score
                        });
                    }
                }

                // Sort by score and return top K
                var topResults = results
                    .OrderByDescending(r => r.Score)
                    .Take(topK)
                    .ToList();

                _logger.LogInformation("[KB Keyword Search] Found {0} results", topResults.Count);
                return topResults;
            }
            catch (Exception ex)
            {
                _logger.LogError("[KB Keyword Search] Error in keyword search: {0}", ex, ex.Message);
                return new List<KnowledgeBaseResult>();
            }
        }

        /// <summary>
        /// Get all active knowledge base documents
        /// </summary>
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
                    Category = d.Category ?? "General",
                    Score = 1.0
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError("[KB GetAll] Error retrieving all documents: {0}", ex, ex.Message);
                return new List<KnowledgeBaseResult>();
            }
        }

        /// <summary>
        /// Get specific document by ID
        /// </summary>
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
                    Category = doc.Category ?? "General",
                    Score = 1.0
                };
            }
            catch (Exception ex)
            {
                _logger.LogError("[KB GetById] Error retrieving document {0}: {1}", ex, id, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Extract keywords from query, removing common stop words
        /// </summary>
        private List<string> ExtractKeywords(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<string>();

            // Common stop words in Indonesian and English
            var stopWords = new HashSet<string>
            {
                "yang", "dan", "di", "ke", "dari", "untuk", "dengan", "pada", "adalah", "ini", "itu", "atau", "juga",
                "the", "a", "an", "is", "are", "was", "were", "be", "have", "has", "do", "does", "did",
                "how", "what", "where", "when", "who", "why", "can", "could", "would", "should", "will"
            };

            var words = query.ToLower()
                .Split(new[] { ' ', ',', '.', '?', '!', ';', ':', '-', '(', ')', '[', ']', '\n', '\t' }, 
                       StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2 && !stopWords.Contains(w))
                .Distinct()
                .ToList();

            return words;
        }

        /// <summary>
        /// Calculate relevance score for document based on keyword matching
        /// Uses title, content, and category for scoring
        /// </summary>
        private double CalculateRelevanceScore(KnowledgeBaseDocument doc, List<string> keywords)
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

                // Content match with frequency boost
                if (content.Contains(keyword))
                {
                    score += 1.0;
                    var occurrences = CountOccurrences(content, keyword);
                    score += Math.Min(occurrences * 0.1, 1.0);
                }
            }

            // Normalize by keyword count
            return keywords.Count > 0 ? score / keywords.Count : 0;
        }

        /// <summary>
        /// Count occurrences of keyword in text (case-insensitive)
        /// </summary>
        private int CountOccurrences(string text, string keyword)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(keyword))
                return 0;

            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(keyword, index, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                count++;
                index += keyword.Length;
            }
            return count;
        }
    }
}
