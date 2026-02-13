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
                if (keywords.Length == 0)
                    return new List<KnowledgeBaseChunkDto>();

                var lowerQuery = query.ToLower();

                var chunks = await _db.KnowledgeBaseChunks
                    .Include(c => c.Document)
                    .Where(c => c.Document != null && c.Document.IsActive == true)
                    .ToListAsync();

                var results = chunks
                    .Select(c =>
                    {
                        var contentLower = c.Content.ToLower();
                        var titleLower = c.Document?.Title.ToLower() ?? "";
                        var categoryLower = c.Document?.Category.ToLower() ?? "";

                        // Check if query matches - with FUZZY matching support for typos
                        bool matchFound = keywords.Any(k => 
                            contentLower.Contains(k) || 
                            titleLower.Contains(k) || 
                            categoryLower.Contains(k) ||
                            HasFuzzyMatch(contentLower, k, tolerance: 1) ||  // Allow 1-char difference
                            HasFuzzyMatch(titleLower, k, tolerance: 1) ||
                            HasFuzzyMatch(categoryLower, k, tolerance: 1));

                        if (!matchFound) return null;

                        // Calculate relevance score
                        var score = CalculateKeywordRelevance(c.Content, c.Document?.Title ?? "", 
                                                             c.Document?.Category ?? "", keywords);

                        return new KnowledgeBaseChunkDto
                        {
                            Id = c.Id,
                            DocumentId = c.DocumentId,
                            Title = c.Document?.Title ?? "Unknown",
                            Content = c.Content,
                            Category = c.Document?.Category ?? "General",
                            ChunkIndex = c.ChunkIndex,
                            RelevanceScore = score
                        };
                    })
                    .Where(x => x != null)
                    .OrderByDescending(x => x.RelevanceScore)
                    .Take(topK)
                    .ToList();

                _logger.LogInformation($"[KnowledgeBaseSearchService] Found {results.Count} keyword matches");
                
                // FALLBACK: If no results, try broader search with partial matches
                if (results.Count == 0)
                {
                    _logger.LogWarning($"[KnowledgeBaseSearchService] No exact matches found for '{query}', trying fallback search");
                    results = await FallbackSearchAsync(chunks, keywords, topK);
                }

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
                // ? PARALLEL EXECUTION: Run both keyword and semantic search simultaneously
                var keywordTask = SearchByKeywordAsync(query, topK);
                
                // If embedding provided, run semantic search in parallel; otherwise complete task
                var semanticTask = embedding != null && embedding.Length > 0
                    ? SearchBySemanticAsync(embedding, topK)
                    : Task.FromResult(new List<KnowledgeBaseChunkDto>());

                // Wait for both to complete
                await Task.WhenAll(keywordTask, semanticTask);

                var keywordResults = keywordTask.Result;
                var semanticResults = semanticTask.Result;

                // If no semantic search was performed, return keyword results only
                if (semanticResults.Count == 0)
                {
                    _logger.LogInformation($"[KnowledgeBaseSearchService] Hybrid search: {keywordResults.Count} keyword results only");
                    return keywordResults;
                }

                // ? MERGE RESULTS from both searches
                var merged = new Dictionary<int, KnowledgeBaseChunkDto>();

                foreach (var result in keywordResults)
                {
                    merged[result.Id] = result;
                }

                foreach (var result in semanticResults)
                {
                    if (merged.ContainsKey(result.Id))
                    {
                        // Average the scores from both methods
                        merged[result.Id].RelevanceScore = (merged[result.Id].RelevanceScore + result.RelevanceScore) / 2;
                    }
                    else
                    {
                        merged[result.Id] = result;
                    }
                }

                var finalResults = merged.Values
                    .OrderByDescending(x => x.RelevanceScore)
                    .Take(topK)
                    .ToList();

                _logger.LogInformation($"[KnowledgeBaseSearchService] Hybrid search complete: {keywordResults.Count} keyword + {semanticResults.Count} semantic = {finalResults.Count} merged results");
                return finalResults;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[KnowledgeBaseSearchService] Hybrid search error: {ex.Message}");
                return new List<KnowledgeBaseChunkDto>();
            }
        }

        private double CalculateKeywordRelevance(string content, string title, string category, string[] keywords)
        {
            var score = 0.0;
            var contentLower = content.ToLower();
            var titleLower = title.ToLower();
            var categoryLower = category.ToLower();

            foreach (var keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword)) continue;

                // IMPROVED PRIORITY SCORING:
                // 1. Title match = 1.0 (exact module match)
                // 2. Category match = 0.8
                // 3. Section header match (FITUR UTAMA, WORKFLOW, etc.) = 0.9
                // 4. Early content match (first 500 chars) = 0.6
                // 5. General content frequency = 0.1-0.15

                if (titleLower == keyword || titleLower.Contains($" {keyword} ") || 
                    titleLower.StartsWith(keyword + " ") || titleLower.EndsWith($" {keyword}"))
                {
                    score += 1.0; // Title match gets max score
                }
                else if (categoryLower.Contains(keyword))
                {
                    score += 0.8; // Category match is high
                }
                else
                {
                    // Check for section headers (FITUR UTAMA, WORKFLOW, FIELD REFERENCE, etc)
                    // These are typically formatted as: KEYWORD UTAMA or similar
                    var lines = contentLower.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    bool headerFound = false;
                    foreach (var line in lines.Take(20)) // Check first 20 lines for headers
                    {
                        var trimmedLine = line.Trim();
                        if (trimmedLine.StartsWith(keyword) && (
                            trimmedLine.Length < 100 || // Likely a header if short
                            trimmedLine.Contains("utama") || trimmedLine.Contains("document") ||
                            trimmedLine.Contains("grid") || trimmedLine.Contains("workflow")))
                        {
                            score += 0.9; // Section header match
                            headerFound = true;
                            break;
                        }
                    }

                    if (!headerFound)
                    {
                        // Check if keyword appears in first 500 chars (higher relevance for early mention)
                        var firstPart = contentLower.Substring(0, Math.Min(500, contentLower.Length));
                        if (firstPart.Contains(keyword))
                        {
                            score += 0.6; // Early mention gets good score
                        }
                        else
                        {
                            // Count occurrences in full content (frequency-based)
                            var count = (contentLower.Length - contentLower.Replace(keyword, "").Length) / keyword.Length;
                            score += Math.Min(count * 0.15, 0.5);
                        }
                    }
                }
            }

            return Math.Min(score, 1.0); // Normalize to [0, 1]
        }

        /// <summary>
        /// Levenshtein distance-based fuzzy matching for typo tolerance
        /// Allows matching words that differ by N characters
        /// </summary>
        private bool HasFuzzyMatch(string text, string keyword, int tolerance = 1)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(keyword))
                return false;

            // Split text into words
            var words = text.Split(new[] { ' ', '\t', '\n', '\r', '.', ',', ':', ';', '!', '?' }, 
                                  StringSplitOptions.RemoveEmptyEntries);

            foreach (var word in words)
            {
                if (LevenshteinDistance(word, keyword) <= tolerance)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Calculate Levenshtein distance between two strings
        /// Returns minimum edits (insert, delete, replace) needed to transform one string to another
        /// </summary>
        private int LevenshteinDistance(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1)) return s2.Length;
            if (string.IsNullOrEmpty(s2)) return s1.Length;

            var dp = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 0; i <= s1.Length; i++)
                dp[i, 0] = i;

            for (int j = 0; j <= s2.Length; j++)
                dp[0, j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    dp[i, j] = Math.Min(
                        Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                        dp[i - 1, j - 1] + cost
                    );
                }
            }

            return dp[s1.Length, s2.Length];
        }

        /// <summary>
        /// Fallback search when exact matches not found
        /// Uses partial matching and word boundary tolerance
        /// </summary>
        private async Task<List<KnowledgeBaseChunkDto>> FallbackSearchAsync(
            List<KnowledgeBaseChunks> chunks, string[] keywords, int topK)
        {
            try
            {
                _logger.LogInformation("[KnowledgeBaseSearchService] Executing fallback search");

                // First fallback: search by individual word substrings (more lenient)
                var results = new List<KnowledgeBaseChunkDto>();

                foreach (var chunk in chunks)
                {
                    var contentLower = chunk.Content.ToLower();
                    var titleLower = chunk.Document?.Title.ToLower() ?? "";
                    var categoryLower = chunk.Document?.Category.ToLower() ?? "";

                    // Count how many keywords appear (partial match)
                    int keywordMatches = 0;
                    foreach (var keyword in keywords)
                    {
                        if (keyword.Length >= 3)
                        {
                            var prefix = keyword.Substring(0, Math.Min(3, keyword.Length));
                            if (contentLower.Contains(prefix) || titleLower.Contains(prefix))
                            {
                                keywordMatches++;
                            }
                        }
                    }

                    // Include chunks that have at least 1 keyword match
                    if (keywordMatches > 0)
                    {
                        var score = (double)keywordMatches / keywords.Length;
                        
                        results.Add(new KnowledgeBaseChunkDto
                        {
                            Id = chunk.Id,
                            DocumentId = chunk.DocumentId,
                            Title = chunk.Document?.Title ?? "Unknown",
                            Content = chunk.Content,
                            Category = chunk.Document?.Category ?? "General",
                            ChunkIndex = chunk.ChunkIndex,
                            RelevanceScore = Math.Min(score, 0.7) // Cap at 0.7 for fallback
                        });
                    }
                }

                // Sort by relevance and return top K
                var finalResults = results
                    .OrderByDescending(x => x.RelevanceScore)
                    .Take(topK)
                    .ToList();

                _logger.LogInformation($"[KnowledgeBaseSearchService] Fallback search found {finalResults.Count} results");
                return finalResults;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[KnowledgeBaseSearchService] Fallback search error: {ex.Message}");
                return new List<KnowledgeBaseChunkDto>();
            }
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
