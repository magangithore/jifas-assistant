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
    /// Service for generating embeddings using Google Gemini API
    /// Uses gemini-embedding-001 (3072-dimensional embeddings)
    /// FREE tier available!
    /// </summary>
    public interface IEmbeddingService
    {
        Task<List<float>> GenerateEmbeddingAsync(string text);
        Task<List<List<float>>> GenerateBatchEmbeddingsAsync(List<string> texts);
        double CalculateCosineSimilarity(List<float> embedding1, List<float> embedding2);
    }

    public class GeminiEmbeddingService : IEmbeddingService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILoggerService _logger;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string _baseUrl;

        public GeminiEmbeddingService(IConfiguration configuration, ILoggerService logger, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();
            
            _apiKey = configuration["Gemini:ApiKey"];
            _model = "gemini-embedding-001"; // 3072-dimensional
            _baseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new InvalidOperationException("Gemini:ApiKey not configured in appsettings.json");
            }

            // FIX #1: Validate embedding dimension consistency
            var configuredDimension = int.TryParse(
                configuration["Qdrant:EmbeddingDimensions"], 
                out int dimension) ? dimension : 3072;
            
            const int EXPECTED_DIMENSION = 3072; // gemini-embedding-001 is 3072-dimensional
            
            if (configuredDimension != EXPECTED_DIMENSION)
            {
                var errorMsg = $"[CRITICAL] Embedding dimension mismatch! " +
                    $"Expected {EXPECTED_DIMENSION} for gemini-embedding-001, " +
                    $"but configured {configuredDimension}. " +
                    $"Update Qdrant:EmbeddingDimensions in appsettings.json";
                
                _logger.LogError(errorMsg);
                throw new InvalidOperationException(errorMsg);
            }

            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _logger.LogInformation($"[GeminiEmbeddingService] Initialized with model: {_model} ({EXPECTED_DIMENSION}-dimensional)");
            _logger.LogInformation($"[GeminiEmbeddingService] Embedding dimension validated: {configuredDimension} dimensions");
        }

        public async Task<List<float>> GenerateEmbeddingAsync(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                    return new List<float>();

                // Clean text
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

                // Truncate if too long (Gemini max ~10,000 chars)
                if (text.Length > 10000)
                    text = text.Substring(0, 10000);

                var url = $"{_baseUrl}/{_model}:embedContent?key={_apiKey}";

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
                    _logger.LogError($"[GeminiEmbeddingService] API Error: {response.StatusCode} - {responseText}");
                    return new List<float>();
                }

                var jsonResponse = JObject.Parse(responseText);
                var embeddingArray = jsonResponse["embedding"]?["values"]?.ToObject<List<float>>();

                if (embeddingArray == null || embeddingArray.Count == 0)
                {
                    _logger.LogWarning("[GeminiEmbeddingService] No embedding returned from API");
                    return new List<float>();
                }

                _logger.LogDebug($"[GeminiEmbeddingService] Generated {embeddingArray.Count}-dim embedding");
                return embeddingArray;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GeminiEmbeddingService] Error: {ex.Message}");
                return new List<float>();
            }
        }

        public async Task<List<List<float>>> GenerateBatchEmbeddingsAsync(List<string> texts)
        {
            var embeddings = new List<List<float>>();

            // Process with rate limiting
            foreach (var text in texts)
            {
                var embedding = await GenerateEmbeddingAsync(text);
                embeddings.Add(embedding);
                
                // Small delay to respect rate limits
                await Task.Delay(100);
            }

            _logger.LogInformation($"[GeminiEmbeddingService] Generated {embeddings.Count} embeddings");
            return embeddings;
        }

        public double CalculateCosineSimilarity(List<float> embedding1, List<float> embedding2)
        {
            if (embedding1 == null || embedding2 == null || embedding1.Count == 0 || embedding2.Count == 0)
                return 0;

            if (embedding1.Count != embedding2.Count)
                return 0;

            try
            {
                double dotProduct = 0;
                for (int i = 0; i < embedding1.Count; i++)
                {
                    dotProduct += embedding1[i] * embedding2[i];
                }

                double magnitude1 = Math.Sqrt(embedding1.Sum(x => x * x));
                double magnitude2 = Math.Sqrt(embedding2.Sum(x => x * x));

                if (magnitude1 == 0 || magnitude2 == 0)
                    return 0;

                double similarity = dotProduct / (magnitude1 * magnitude2);
                return Math.Max(0, Math.Min(1, similarity)); // Clamp to [0, 1]
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GeminiEmbeddingService] Similarity calculation error: {ex.Message}");
                return 0;
            }
        }
    }
}
