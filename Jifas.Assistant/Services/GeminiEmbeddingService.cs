using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Gemini Embedding Service - Uses Google Gemini API for embeddings
    /// Compatible with .NET Framework 4.8
    /// FREE tier available!
    /// </summary>
    public class GeminiEmbeddingService : IEmbeddingService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string _baseUrl;

        public GeminiEmbeddingService()
        {
            _httpClient = new HttpClient();
            
            // Get API key from Web.config
            _apiKey = System.Configuration.ConfigurationManager.AppSettings["Gemini:ApiKey"];
            
            if (string.IsNullOrEmpty(_apiKey))
            {
                throw new InvalidOperationException(
                    "Gemini API key not found. Please set Gemini:ApiKey in Web.config"
                );
            }

            // Use Gemini gemini-embedding-001 model (3072-dim, high accuracy)
            // gemini-embedding-001: 3072 dimensions (RECOMMENDED for JIFAS - matches pre-loaded KB)
            // Note: Model name is "gemini-embedding-001" not "embedding-001"
            _model = "gemini-embedding-001"; // 3072-dim embeddings - matches our KB load
            _baseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
            
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            
            System.Diagnostics.Debug.WriteLine("[GeminiEmbedding] ? Service initialized with model: " + _model);
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
                    System.Diagnostics.Debug.WriteLine($"[GeminiEmbedding] Error: {response.StatusCode}");
                    System.Diagnostics.Debug.WriteLine($"[GeminiEmbedding] Response: {responseText}");
                    return new List<float>();
                }

                // Parse response
                var jsonResponse = JObject.Parse(responseText);
                var embeddingArray = jsonResponse["embedding"]?["values"]?.ToObject<List<float>>();

                if (embeddingArray == null || embeddingArray.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[GeminiEmbedding] No embedding returned");
                    return new List<float>();
                }

                System.Diagnostics.Debug.WriteLine($"[GeminiEmbedding] ? Generated {embeddingArray.Count}-dim embedding (Gemini {_model})");
                return embeddingArray;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GeminiEmbedding] Error: {ex.Message}");
                return new List<float>();
            }
        }

        public async Task<List<List<float>>> GenerateBatchEmbeddingsAsync(List<string> texts)
        {
            var embeddings = new List<List<float>>();

            // Gemini supports batch, but for simplicity process one by one
            // with rate limiting
            foreach (var text in texts)
            {
                var embedding = await GenerateEmbeddingAsync(text);
                embeddings.Add(embedding);
                
                // Small delay to respect rate limits
                await Task.Delay(100);
            }

            System.Diagnostics.Debug.WriteLine($"[GeminiEmbedding] ? Generated {embeddings.Count} embeddings");
            return embeddings;
        }

        public double CalculateCosineSimilarity(List<float> embedding1, List<float> embedding2)
        {
            if (embedding1 == null || embedding2 == null)
                return 0;

            if (embedding1.Count != embedding2.Count)
                return 0;

            if (embedding1.Count == 0)
                return 0;

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
                    return 0;

                // Cosine similarity
                double similarity = dotProduct / (magnitude1 * magnitude2);
                
                return Math.Max(0, Math.Min(1, similarity)); // Clamp to [0, 1]
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GeminiEmbedding] Similarity error: {ex.Message}");
                return 0;
            }
        }
    }
}
