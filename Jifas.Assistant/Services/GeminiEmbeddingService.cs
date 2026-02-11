using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Embedding service interface for generating and comparing embeddings
    /// </summary>
    public interface IEmbeddingService
    {
        Task<List<float>> GenerateEmbeddingAsync(string text);
        Task<List<List<float>>> GenerateBatchEmbeddingsAsync(List<string> texts);
        double CalculateCosineSimilarity(List<float> embedding1, List<float> embedding2);
    }

    /// <summary>
    /// Gemini Embedding Service - Uses Google Gemini API for embeddings
    /// FREE tier available!
    /// Compatible with .NET 10
    /// </summary>
    public class GeminiEmbeddingService : IEmbeddingService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILoggerService _logger;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string _baseUrl;

        public GeminiEmbeddingService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILoggerService logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;

            // Get API key from configuration
            _apiKey = _configuration["Gemini:ApiKey"];
            
            if (string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogError("[GeminiEmbedding] Gemini API key not configured", null);
                throw new InvalidOperationException(
                    "Gemini API key not found. Please set Gemini:ApiKey in appsettings.json"
                );
            }

            // Use Gemini embedding model
            // gemini-embedding-001: 3072 dimensions (recommended for high accuracy)
            _model = "gemini-embedding-001";
            _baseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
            
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            
            _logger.LogInformation("[GeminiEmbedding] Service initialized with model: {0}", _model);
        }

        public async Task<List<float>> GenerateEmbeddingAsync(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("[GeminiEmbedding] Empty text provided for embedding");
                    return new List<float>();
                }

                // Clean text
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

                // Truncate if too long (Gemini max ~10,000 chars)
                if (text.Length > 10000)
                {
                    text = text.Substring(0, 10000);
                    _logger.LogDebug("[GeminiEmbedding] Text truncated to 10000 characters");
                }

                // Gemini API endpoint
                var url = $"{_baseUrl}/{_model}:embedContent?key={_apiKey}";

                // Request body format untuk Gemini
                var requestBody = new
                {
                    model = $"models/{_model}",
                    content = new
                    {
                        parts = new[]
                        {
                            new { text = text }
                        }
                    }
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                var responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("[GeminiEmbedding] API Error {0}: {1}", null, response.StatusCode, responseText);
                    return new List<float>();
                }

                // Parse response
                var jsonResponse = JObject.Parse(responseText);
                var embeddingArray = jsonResponse["embedding"]?["values"]?.ToObject<List<float>>();

                if (embeddingArray == null || embeddingArray.Count == 0)
                {
                    _logger.LogWarning("[GeminiEmbedding] No embedding returned from API");
                    return new List<float>();
                }

                _logger.LogDebug("[GeminiEmbedding] Generated {0}-dimensional embedding", embeddingArray.Count);
                return embeddingArray;
            }
            catch (Exception ex)
            {
                _logger.LogError("[GeminiEmbedding] Error generating embedding: {0}", ex, ex.Message);
                return new List<float>();
            }
        }

        public async Task<List<List<float>>> GenerateBatchEmbeddingsAsync(List<string> texts)
        {
            if (texts == null || texts.Count == 0)
            {
                _logger.LogWarning("[GeminiEmbedding] Empty text list provided for batch embedding");
                return new List<List<float>>();
            }

            var embeddings = new List<List<float>>();

            try
            {
                _logger.LogInformation("[GeminiEmbedding] Starting batch embedding for {0} texts", texts.Count);

                // Process texts with rate limiting
                foreach (var text in texts)
                {
                    var embedding = await GenerateEmbeddingAsync(text);
                    embeddings.Add(embedding);
                    
                    // Small delay to respect rate limits
                    await Task.Delay(100);
                }

                _logger.LogInformation("[GeminiEmbedding] Batch embedding completed: {0} embeddings generated", embeddings.Count);
                return embeddings;
            }
            catch (Exception ex)
            {
                _logger.LogError("[GeminiEmbedding] Error in batch embedding: {0}", ex, ex.Message);
                return embeddings;
            }
        }

        public double CalculateCosineSimilarity(List<float> embedding1, List<float> embedding2)
        {
            if (embedding1 == null || embedding2 == null)
            {
                _logger.LogWarning("[GeminiEmbedding] Null embedding provided for similarity calculation");
                return 0;
            }

            if (embedding1.Count != embedding2.Count)
            {
                _logger.LogWarning("[GeminiEmbedding] Embedding dimension mismatch: {0} vs {1}", embedding1.Count, embedding2.Count);
                return 0;
            }

            if (embedding1.Count == 0)
            {
                _logger.LogWarning("[GeminiEmbedding] Empty embeddings for similarity calculation");
                return 0;
            }

            try
            {
                // Calculate dot product
                double dotProduct = 0;
                for (int i = 0; i < embedding1.Count; i++)
                {
                    dotProduct += embedding1[i] * embedding2[i];
                }

                // Calculate magnitudes
                double magnitude1 = Math.Sqrt(embedding1.Sum(x => x * x));
                double magnitude2 = Math.Sqrt(embedding2.Sum(x => x * x));

                if (magnitude1 == 0 || magnitude2 == 0)
                {
                    _logger.LogWarning("[GeminiEmbedding] Zero magnitude embedding in similarity calculation");
                    return 0;
                }

                // Cosine similarity
                double similarity = dotProduct / (magnitude1 * magnitude2);
                
                // Clamp to [0, 1]
                return Math.Max(0, Math.Min(1, similarity));
            }
            catch (Exception ex)
            {
                _logger.LogError("[GeminiEmbedding] Similarity calculation error: {0}", ex, ex.Message);
                return 0;
            }
        }
    }
}
