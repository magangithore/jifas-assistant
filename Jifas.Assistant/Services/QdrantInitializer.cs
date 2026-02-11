using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Jifas.Chatbot.Services
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

    public class QdrantInitializer : IQdrantInitializer
    {
        private readonly ILoggerService _logger;
        private readonly string _qdrantUrl;
        private readonly string _collectionName;
        private readonly int _embeddingDimensions;
        private readonly HttpClient _httpClient;

        public QdrantInitializer()
        {
            _logger = LoggerFactory.GetLogger();
            _qdrantUrl = ConfigurationManager.AppSettings["Qdrant:Url"] ?? "http://localhost:6333";
            _collectionName = ConfigurationManager.AppSettings["Qdrant:CollectionName"] ?? "jifas_kb";
            _embeddingDimensions = int.TryParse(ConfigurationManager.AppSettings["Qdrant:EmbeddingDimensions"], out var dim) 
                ? dim 
                : 384;
            _httpClient = new HttpClient();

            _logger.LogInformation("[QdrantInitializer] Initialized - URL: {0}, Collection: {1}, Dimensions: {2}", 
                _qdrantUrl, _collectionName, _embeddingDimensions);
        }

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
                var url = $"{_qdrantUrl}/collections/{_collectionName}";
                
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
                    var msg = "[QdrantInitializer] Failed to create collection: " + response.StatusCode.ToString();
                    _logger.LogError(msg);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantInitializer] Initialization error: " + ex.Message);
                return false;
            }
        }

        public async Task<bool> DeleteCollectionAsync()
        {
            try
            {
                _logger.LogInformation("[QdrantInitializer] Deleting collection: {0}", _collectionName);

                var url = $"{_qdrantUrl}/collections/{_collectionName}";
                var response = await _httpClient.DeleteAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[QdrantInitializer] Collection deleted successfully");
                    return true;
                }
                else
                {
                    var msg = "[QdrantInitializer] Failed to delete collection: " + response.StatusCode.ToString();
                    _logger.LogError(msg);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantInitializer] Deletion error: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Check if collection exists
        /// </summary>
        private async Task<bool> CollectionExistsAsync()
        {
            try
            {
                var url = $"{_qdrantUrl}/collections/{_collectionName}";
                var response = await _httpClient.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
