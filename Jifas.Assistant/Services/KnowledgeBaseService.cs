using System.Collections.Generic;
using System.Threading.Tasks;
using Jifas.Assistant.Services;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Knowledge Base Service - Search and retrieval
    /// </summary>
    public interface IKnowledgeBaseService
    {
        Task<List<KnowledgeBaseResult>> SearchAsync(string query, int topK = 5);

        /// <summary>
        /// Hybrid search using both keyword and semantic embedding
        /// </summary>
        Task<List<KnowledgeBaseResult>> SearchWithEmbeddingAsync(string query, float[] embedding, int topK = 5);
    }

    public class KnowledgeBaseService : IKnowledgeBaseService
    {
        private readonly IKnowledgeBaseSearchService _searchService;
        private readonly ILoggerService _logger;

        public KnowledgeBaseService(IKnowledgeBaseSearchService searchService, ILoggerService logger)
        {
            _searchService = searchService;
            _logger = logger;
        }

        public async Task<List<KnowledgeBaseResult>> SearchAsync(string query, int topK = 5)
        {
            return await SearchWithEmbeddingAsync(query, null, topK);
        }

        public async Task<List<KnowledgeBaseResult>> SearchWithEmbeddingAsync(string query, float[] embedding, int topK = 5)
        {
            try
            {
                _logger.LogInformation($"[KnowledgeBaseService] Searching KB: {query} (semantic: {embedding != null})");

                // Delegate to RAG search service with optional embedding
                var results = await _searchService.SearchAsync(query, embedding, topK);

                // Convert to KnowledgeBaseResult format
                var kbResults = new List<KnowledgeBaseResult>();
                foreach (var chunk in results)
                {
                    kbResults.Add(new KnowledgeBaseResult
                    {
                        DocumentId = chunk.DocumentId,
                        Title = chunk.Title,
                        Content = chunk.Content,
                        Category = chunk.Category,
                        Score = chunk.RelevanceScore
                    });
                }

                return kbResults;
            }
            catch (System.Exception ex)
            {
                _logger.LogError($"[KnowledgeBaseService] Search error: {ex.Message}");
                return new List<KnowledgeBaseResult>();
            }
        }
    }
}
