using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Qdrant Vector Database Service
    /// Handles semantic search using vector embeddings
    /// Replaces KB search with vector similarity
    /// </summary>
    public interface IQdrantVectorService
    {
        /// <summary>
        /// Search for similar documents using vector similarity
        /// </summary>
        Task<List<KnowledgeBaseResult>> SearchAsync(string query, int topK = 5);

        /// <summary>
        /// Index a document (store embeddings) - for KB seeding
        /// </summary>
        Task IndexDocumentAsync(int documentId, float[] embedding, string title, string content, string category);

        /// <summary>
        /// Index document with metadata payload (for seeding service)
        /// </summary>
        Task IndexDocumentAsync(string pointId, List<float> embedding, Dictionary<string, object> metadata);

        /// <summary>
        /// Initialize Qdrant collection (create if not exists)
        /// </summary>
        Task<bool> InitializeCollectionAsync();

        /// <summary>
        /// Delete Qdrant collection
        /// </summary>
        Task<bool> DeleteCollectionAsync();

        /// <summary>
        /// Check if Qdrant is healthy
        /// </summary>
        Task<bool> IsHealthyAsync();
    }

    public class QdrantVectorService : IQdrantVectorService
    {
        private readonly ILoggerService _logger;
        private readonly IEmbeddingService _embeddingService;
        private readonly string _qdrantUrl;
        private readonly string _collectionName = "jifas_kb";
        private readonly HttpClient _httpClient;

        public QdrantVectorService(IEmbeddingService embeddingService)
        {
            _logger = LoggerFactory.GetLogger();
            _embeddingService = embeddingService;
            _qdrantUrl = ConfigurationManager.AppSettings["Qdrant:Url"] ?? "http://localhost:6333";
            _httpClient = new HttpClient();

            _logger.LogInformation("[QdrantVectorService] Initialized with URL: {0}", _qdrantUrl);
        }

        public async Task<List<KnowledgeBaseResult>> SearchAsync(string query, int topK = 5)
        {
            try
            {
                // Step 1: Generate embedding for query
                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
                if (queryEmbedding == null || queryEmbedding.Count == 0)
                {
                    _logger.LogWarning("[QdrantVectorService] Failed to generate query embedding for: " + query);
                    return new List<KnowledgeBaseResult>();
                }

                // Step 2: Search in Qdrant
                var results = await SearchVectorsAsync(queryEmbedding, topK);

                _logger.LogInformation("[QdrantVectorService] Found {0} results for query: {1}", results.Count, query);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantVectorService] Search error: " + ex.Message);
                return new List<KnowledgeBaseResult>();
            }
        }

        public async Task IndexDocumentAsync(int documentId, float[] embedding, string title, string content, string category)
        {
            try
            {
                // Create point with payload (metadata)
                var point = new
                {
                    id = documentId,
                    vector = embedding,
                    payload = new
                    {
                        documentId = documentId,
                        title = title,
                        content = content,
                        category = category,
                        indexedDate = DateTime.UtcNow
                    }
                };

                // Upsert to Qdrant
                var url = $"{_qdrantUrl}/collections/{_collectionName}/points?wait=true";
                var json = JsonConvert.SerializeObject(new { points = new[] { point } });
                var content_http = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PutAsync(url, content_http);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[QdrantVectorService] Failed to index document {0}: {1}", documentId, response.StatusCode);
                    return;
                }

                _logger.LogInformation("[QdrantVectorService] Indexed document {0}: {1}", documentId, title);
            }
            catch (Exception ex)
            {
                var msg = "[QdrantVectorService] Indexing error for doc " + documentId + ": " + ex.Message;
                _logger.LogError(msg);
            }
        }

        /// <summary>
        /// Index document with metadata payload (for seeding service)
        /// Overload to support List<float> embeddings and custom metadata
        /// </summary>
        public async Task IndexDocumentAsync(string pointId, List<float> embedding, Dictionary<string, object> metadata)
        {
            try
            {
                if (!long.TryParse(pointId, out var longPointId))
                {
                    _logger.LogWarning("[QdrantVectorService] Invalid point ID: {0}", pointId);
                    return;
                }

                // Convert List<float> to array for Qdrant
                var embeddingArray = embedding?.ToArray() ?? new float[0];

                // Create point with payload (metadata)
                var point = new
                {
                    id = longPointId,
                    vector = embeddingArray,
                    payload = metadata
                };

                // Upsert to Qdrant
                var url = $"{_qdrantUrl}/collections/{_collectionName}/points?wait=true";
                var json = JsonConvert.SerializeObject(new { points = new[] { point } });
                var content_http = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PutAsync(url, content_http);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[QdrantVectorService] Failed to index point {0}: {1}", pointId, response.StatusCode);
                    return;
                }

                _logger.LogDebug("[QdrantVectorService] Indexed point {0}", pointId);
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantVectorService] Indexing error for point " + pointId + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Initialize Qdrant collection (create if not exists)
        /// </summary>
        public async Task<bool> InitializeCollectionAsync()
        {
            var initializer = new QdrantInitializer();
            return await initializer.InitializeCollectionAsync();
        }

        /// <summary>
        /// Delete Qdrant collection
        /// </summary>
        public async Task<bool> DeleteCollectionAsync()
        {
            var initializer = new QdrantInitializer();
            return await initializer.DeleteCollectionAsync();
        }

        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                var url = $"{_qdrantUrl}/health";
                var response = await _httpClient.GetAsync(url);
                var isHealthy = response.IsSuccessStatusCode;

                _logger.LogInformation("[QdrantVectorService] Health check: {0}", isHealthy ? "HEALTHY" : "UNHEALTHY");
                return isHealthy;
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantVectorService] Health check failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Internal: Search vectors in Qdrant
        /// </summary>
        private async Task<List<KnowledgeBaseResult>> SearchVectorsAsync(List<float> vector, int topK)
        {
            try
            {
                var url = $"{_qdrantUrl}/collections/{_collectionName}/points/search";
                
                var searchRequest = new
                {
                    vector = vector,
                    limit = topK,
                    with_payload = true
                };

                var json = JsonConvert.SerializeObject(searchRequest);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, httpContent);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[QdrantVectorService] Search failed: {0}", response.StatusCode);
                    return new List<KnowledgeBaseResult>();
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                var searchResponse = JsonConvert.DeserializeObject<QdrantSearchResponse>(responseBody);

                if (searchResponse?.Result == null || searchResponse.Result.Count == 0)
                {
                    return new List<KnowledgeBaseResult>();
                }

                // Convert Qdrant results to KnowledgeBaseResult
                var results = new List<KnowledgeBaseResult>();

                foreach (var point in searchResponse.Result)
                {
                    if (point.Payload != null)
                    {
                        results.Add(new KnowledgeBaseResult
                        {
                            DocumentId = (int)(point.Payload["documentId"] ?? point.Id),
                            Title = point.Payload.ContainsKey("title") ? point.Payload["title"].ToString() : "Unknown",
                            Content = point.Payload.ContainsKey("content") ? point.Payload["content"].ToString() : "",
                            Category = point.Payload.ContainsKey("category") ? point.Payload["category"].ToString() : "",
                            Score = point.Score
                        });
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantVectorService] Vector search error: " + ex.Message);
                return new List<KnowledgeBaseResult>();
            }
        }

        // DTO for Qdrant responses
        private class QdrantSearchResponse
        {
            [JsonProperty("result")]
            public List<QdrantPoint> Result { get; set; }
        }

        private class QdrantPoint
        {
            [JsonProperty("id")]
            public long Id { get; set; }

            [JsonProperty("score")]
            public double Score { get; set; }

            [JsonProperty("payload")]
            public Dictionary<string, object> Payload { get; set; }
        }
    }
}
