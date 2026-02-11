using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Initialize and manage Qdrant collections
    /// </summary>
    public interface IQdrantInitializer
    {
        /// <summary>
        /// Create collection if not exists
        /// </summary>
        Task<bool> InitializeCollectionAsync();

        /// <summary>
        /// Delete collection
        /// </summary>
        Task<bool> DeleteCollectionAsync();
    }

    /// <summary>
    /// Service for initializing and managing Qdrant vector database collections
    /// 
    /// Creates or deletes Qdrant collections for knowledge base embeddings.
    /// Uses HTTP REST API to communicate with Qdrant service.
    /// Compatible with .NET 10 and uses proper dependency injection.
    /// </summary>
    public class QdrantInitializer : IQdrantInitializer
    {
        private readonly ILoggerService _logger;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly string _qdrantUrl;
        private readonly string _collectionName;
        private readonly int _embeddingDimensions;

        /// <summary>
        /// Initialize Qdrant initializer with dependency injection
        /// </summary>
        public QdrantInitializer(
            ILoggerService logger,
            IConfiguration configuration,
            HttpClient httpClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

            // Read configuration
            _qdrantUrl = _configuration.GetValue("Qdrant:Url", "http://localhost:6333");
            _collectionName = _configuration.GetValue("Qdrant:CollectionName", "jifas_kb");
            _embeddingDimensions = _configuration.GetValue("Qdrant:EmbeddingDimensions", 3072);

            _logger.LogInformation("[QdrantInitializer] Initialized - URL: {0}, Collection: {1}, Dimensions: {2}", 
                _qdrantUrl, _collectionName, _embeddingDimensions);
        }

        /// <summary>
        /// Initialize collection - create if not exists
        /// </summary>
        public async Task<bool> InitializeCollectionAsync()
        {
            try
            {
                _logger.LogInformation("[QdrantInitializer] Initializing collection: {0}", _collectionName);

                // Check if collection exists
                if (await CollectionExistsAsync())
                {
                    _logger.LogInformation("[QdrantInitializer] Collection already exists");
                    return true;
                }

                // Create collection
                var url = $"{_qdrantUrl.TrimEnd('/')}/collections/{_collectionName}";
                
                var createRequest = new
                {
                    vectors = new
                    {
                        size = _embeddingDimensions,
                        distance = "Cosine"  // Cosine similarity for semantic search
                    }
                };

                var json = JsonConvert.SerializeObject(createRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PutAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[QdrantInitializer] Collection created successfully");
                    return true;
                }
                else
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    _logger.LogError("[QdrantInitializer] Failed to create collection ({0}): {1}", 
                        null, response.StatusCode, responseText);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantInitializer] Initialization error: {0}", ex, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Delete collection from Qdrant
        /// </summary>
        public async Task<bool> DeleteCollectionAsync()
        {
            try
            {
                _logger.LogInformation("[QdrantInitializer] Deleting collection: {0}", _collectionName);

                var url = $"{_qdrantUrl.TrimEnd('/')}/collections/{_collectionName}";
                var response = await _httpClient.DeleteAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[QdrantInitializer] Collection deleted successfully");
                    return true;
                }
                else
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    _logger.LogError("[QdrantInitializer] Failed to delete collection ({0}): {1}", 
                        null, response.StatusCode, responseText);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantInitializer] Deletion error: {0}", ex, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Check if collection exists in Qdrant
        /// </summary>
        private async Task<bool> CollectionExistsAsync()
        {
            try
            {
                var url = $"{_qdrantUrl.TrimEnd('/')}/collections/{_collectionName}";
                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("[QdrantInitializer] Collection exists: {0}", _collectionName);
                    return true;
                }
                
                _logger.LogDebug("[QdrantInitializer] Collection does not exist: {0}", _collectionName);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[QdrantInitializer] Error checking collection existence: {0}", ex.Message);
                return false;
            }
        }
    }
}
