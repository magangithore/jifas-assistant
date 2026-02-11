using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Qdrant Vector Database Service
    /// Handles semantic search using vector embeddings
    /// Provides vector similarity search and document indexing
    /// </summary>
    public interface IQdrantVectorService
    {
        /// <summary>
        /// Search for similar documents using vector similarity
        /// </summary>
        Task<List<KnowledgeBaseResult>> SearchAsync(string query, int topK = 5);

        /// <summary>
        /// Index a document (store embeddings)
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

    /// <summary>
    /// Implementation of Qdrant Vector Database Service
    /// 
    /// Provides semantic search and document indexing capabilities using Qdrant vector database.
    /// Converts semantic queries to embeddings and searches for similar documents.
    /// Compatible with .NET 10 and uses proper dependency injection.
    /// </summary>
    public class QdrantVectorService : IQdrantVectorService
    {
        private readonly ILoggerService _logger;
        private readonly IConfiguration _configuration;
        private readonly IEmbeddingService _embeddingService;
        private readonly IQdrantInitializer _qdrantInitializer;
        private readonly HttpClient _httpClient;
        private readonly string _qdrantUrl;
        private readonly string _collectionName;

        /// <summary>
        /// Initialize Qdrant Vector Service with dependency injection
        /// </summary>
        public QdrantVectorService(
            ILoggerService logger,
            IConfiguration configuration,
            IEmbeddingService embeddingService,
            IQdrantInitializer qdrantInitializer,
            HttpClient httpClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _qdrantInitializer = qdrantInitializer ?? throw new ArgumentNullException(nameof(qdrantInitializer));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

            // Read Qdrant configuration with defaults
            _qdrantUrl = _configuration.GetValue("Qdrant:Url", "http://localhost:6333");
            _collectionName = _configuration.GetValue("Qdrant:CollectionName", "jifas_kb");

            _logger.LogInformation("[QdrantVectorService] Initialized with URL: {0}, Collection: {1}", 
                _qdrantUrl, _collectionName);
        }

        /// <summary>
        /// Search for similar documents using vector similarity
        /// Generates embedding for query and searches Qdrant collection
        /// </summary>
        public async Task<List<KnowledgeBaseResult>> SearchAsync(string query, int topK = 5)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    _logger.LogWarning("[QdrantVectorService] Empty query provided");
                    return new List<KnowledgeBaseResult>();
                }

                // Step 1: Generate embedding for query
                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
                if (queryEmbedding == null || queryEmbedding.Count == 0)
                {
                    _logger.LogWarning("[QdrantVectorService] Failed to generate query embedding for: {0}", query);
                    return new List<KnowledgeBaseResult>();
                }

                // Step 2: Search in Qdrant
                var results = await SearchVectorsAsync(queryEmbedding, topK);

                _logger.LogInformation("[QdrantVectorService] Found {0} results for query: {1}", results.Count, query);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantVectorService] Search error: {0}", ex, ex.Message);
                return new List<KnowledgeBaseResult>();
            }
        }

        /// <summary>
        /// Index a document with embedding and metadata to Qdrant
        /// </summary>
        public async Task IndexDocumentAsync(int documentId, float[] embedding, string title, string content, string category)
        {
            try
            {
                if (embedding == null || embedding.Length == 0)
                {
                    _logger.LogWarning("[QdrantVectorService] Empty embedding provided for document {0}", documentId);
                    return;
                }

                // Create point with payload (metadata)
                var point = new
                {
                    id = documentId,
                    vector = embedding,
                    payload = new
                    {
                        documentId = documentId,
                        title = title ?? "",
                        content = content ?? "",
                        category = category ?? "",
                        indexedDate = DateTime.UtcNow
                    }
                };

                // Upsert to Qdrant
                var success = await UpsertPointAsync(point);

                if (success)
                {
                    _logger.LogDebug("[QdrantVectorService] Indexed document {0}: {1}", documentId, title);
                }
                else
                {
                    _logger.LogWarning("[QdrantVectorService] Failed to index document {0}", documentId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantVectorService] Indexing error for document {0}: {1}", ex, documentId, ex.Message);
            }
        }

        /// <summary>
        /// Index document with metadata payload (for seeding service)
        /// Supports List<float> embeddings and custom metadata
        /// </summary>
        public async Task IndexDocumentAsync(string pointId, List<float> embedding, Dictionary<string, object> metadata)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(pointId) || !long.TryParse(pointId, out var longPointId))
                {
                    _logger.LogWarning("[QdrantVectorService] Invalid point ID: {0}", pointId);
                    return;
                }

                if (embedding == null || embedding.Count == 0)
                {
                    _logger.LogWarning("[QdrantVectorService] Empty embedding provided for point {0}", pointId);
                    return;
                }

                // Convert List<float> to array for Qdrant
                var embeddingArray = embedding.ToArray();

                // Create point with payload (metadata)
                var point = new
                {
                    id = longPointId,
                    vector = embeddingArray,
                    payload = metadata ?? new Dictionary<string, object>()
                };

                var success = await UpsertPointAsync(point);

                if (success)
                {
                    _logger.LogDebug("[QdrantVectorService] Indexed point {0}", pointId);
                }
                else
                {
                    _logger.LogWarning("[QdrantVectorService] Failed to index point {0}", pointId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantVectorService] Indexing error for point {0}: {1}", ex, pointId, ex.Message);
            }
        }

        /// <summary>
        /// Initialize Qdrant collection (create if not exists)
        /// </summary>
        public async Task<bool> InitializeCollectionAsync()
        {
            try
            {
                var result = await _qdrantInitializer.InitializeCollectionAsync();
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantVectorService] Error initializing collection: {0}", ex, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Delete Qdrant collection
        /// </summary>
        public async Task<bool> DeleteCollectionAsync()
        {
            try
            {
                var result = await _qdrantInitializer.DeleteCollectionAsync();
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantVectorService] Error deleting collection: {0}", ex, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Check if Qdrant service is healthy
        /// </summary>
        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                var url = $"{_qdrantUrl.TrimEnd('/')}/health";
                var response = await _httpClient.GetAsync(url);
                var isHealthy = response.IsSuccessStatusCode;

                _logger.LogInformation("[QdrantVectorService] Health check: {0}", isHealthy ? "HEALTHY" : "UNHEALTHY");
                return isHealthy;
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantVectorService] Health check failed: {0}", ex, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Search vectors in Qdrant collection
        /// </summary>
        private async Task<List<KnowledgeBaseResult>> SearchVectorsAsync(List<float> vector, int topK)
        {
            try
            {
                var url = $"{_qdrantUrl.TrimEnd('/')}/collections/{_collectionName}/points/search";
                
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
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("[QdrantVectorService] Search failed ({0}): {1}", response.StatusCode, errorContent);
                    return new List<KnowledgeBaseResult>();
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                var searchResponse = JsonConvert.DeserializeObject<QdrantSearchResponse>(responseBody);

                if (searchResponse?.Result == null || searchResponse.Result.Count == 0)
                {
                    _logger.LogDebug("[QdrantVectorService] No results found in Qdrant");
                    return new List<KnowledgeBaseResult>();
                }

                // Convert Qdrant results to KnowledgeBaseResult
                var results = new List<KnowledgeBaseResult>();

                foreach (var point in searchResponse.Result)
                {
                    try
                    {
                        if (point.Payload == null)
                            continue;

                        results.Add(new KnowledgeBaseResult
                        {
                            DocumentId = (int)(point.Payload.ContainsKey("documentId") 
                                ? point.Payload["documentId"] 
                                : point.Id),
                            Title = point.Payload.ContainsKey("title") 
                                ? point.Payload["title"]?.ToString() ?? "Unknown" 
                                : "Unknown",
                            Content = point.Payload.ContainsKey("content") 
                                ? point.Payload["content"]?.ToString() ?? "" 
                                : "",
                            Category = point.Payload.ContainsKey("category") 
                                ? point.Payload["category"]?.ToString() ?? "" 
                                : "",
                            Score = point.Score
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("[QdrantVectorService] Error converting point {0}: {1}", point.Id, ex.Message);
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantVectorService] Vector search error: {0}", ex, ex.Message);
                return new List<KnowledgeBaseResult>();
            }
        }

        /// <summary>
        /// Upsert a single point to Qdrant
        /// </summary>
        private async Task<bool> UpsertPointAsync(object point)
        {
            try
            {
                var url = $"{_qdrantUrl.TrimEnd('/')}/collections/{_collectionName}/points?wait=true";
                var payload = new { points = new[] { point } };
                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PutAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("[QdrantVectorService] Upsert failed ({0}): {1}", 
                        response.StatusCode, errorContent);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantVectorService] Error upserting point to Qdrant: {0}", ex, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// DTO for Qdrant search response
        /// </summary>
        private class QdrantSearchResponse
        {
            [JsonProperty("result")]
            public List<QdrantPoint> Result { get; set; }
        }

        /// <summary>
        /// DTO for Qdrant point in search results
        /// </summary>
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
