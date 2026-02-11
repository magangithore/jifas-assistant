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
    /// Service for generating embeddings using Azure OpenAI or OpenAI API
    /// Compatible with .NET Framework 4.8
    /// </summary>
    public interface IEmbeddingService
    {
        Task<List<float>> GenerateEmbeddingAsync(string text);
        Task<List<List<float>>> GenerateBatchEmbeddingsAsync(List<string> texts);
        double CalculateCosineSimilarity(List<float> embedding1, List<float> embedding2);
    }

    public class OpenAIEmbeddingService : IEmbeddingService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string _baseUrl;

        public OpenAIEmbeddingService()
        {
            _httpClient = new HttpClient();
            
            // Try Azure OpenAI first, fallback to OpenAI
            var azureEndpoint = System.Configuration.ConfigurationManager.AppSettings["Azure:OpenAI:Endpoint"];
            var azureKey = System.Configuration.ConfigurationManager.AppSettings["Azure:OpenAI:Key"];
            var openAIKey = System.Configuration.ConfigurationManager.AppSettings["OpenAI:ApiKey"];

            if (!string.IsNullOrEmpty(azureEndpoint) && !string.IsNullOrEmpty(azureKey))
            {
                // Azure OpenAI
                _baseUrl = $"{azureEndpoint.TrimEnd('/')}/openai/deployments/text-embedding-ada-002/embeddings?api-version=2023-05-15";
                _apiKey = azureKey;
                _httpClient.DefaultRequestHeaders.Add("api-key", _apiKey);
                System.Diagnostics.Debug.WriteLine("[OpenAIEmbedding] Using Azure OpenAI");
            }
            else if (!string.IsNullOrEmpty(openAIKey))
            {
                // OpenAI
                _baseUrl = "https://api.openai.com/v1/embeddings";
                _apiKey = openAIKey;
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
                System.Diagnostics.Debug.WriteLine("[OpenAIEmbedding] Using OpenAI Direct");
            }
            else
            {
                throw new InvalidOperationException(
                    "No OpenAI embedding service configured. Please set Azure:OpenAI:Endpoint + Key or OpenAI:ApiKey in Web.config. " +
                    "Or use GeminiEmbeddingService instead (set Gemini:ApiKey)."
                );
            }

            _model = "text-embedding-ada-002";
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<List<float>> GenerateEmbeddingAsync(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                    return new List<float>();

                // Clean text (remove excessive whitespace)
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

                // Truncate if too long (max ~8000 tokens for ada-002)
                if (text.Length > 8000)
                    text = text.Substring(0, 8000);

                var requestBody = new
                {
                    input = text,
                    model = _model
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_baseUrl, content);
                var responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[EmbeddingService] Error: {response.StatusCode} - {responseText}");
                    return new List<float>();
                }

                var jsonResponse = JObject.Parse(responseText);
                var embeddingArray = jsonResponse["data"]?[0]?["embedding"]?.ToObject<List<float>>();

                if (embeddingArray == null || embeddingArray.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[EmbeddingService] No embedding returned");
                    return new List<float>();
                }

                System.Diagnostics.Debug.WriteLine($"[EmbeddingService] ? Generated {embeddingArray.Count}-dim embedding");
                return embeddingArray;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EmbeddingService] Error: {ex.Message}");
                return new List<float>();
            }
        }

        public async Task<List<List<float>>> GenerateBatchEmbeddingsAsync(List<string> texts)
        {
            var embeddings = new List<List<float>>();

            // Process in batches of 10 to avoid rate limits
            var batchSize = 10;
            for (int i = 0; i < texts.Count; i += batchSize)
            {
                var batch = texts.Skip(i).Take(batchSize).ToList();
                
                foreach (var text in batch)
                {
                    var embedding = await GenerateEmbeddingAsync(text);
                    embeddings.Add(embedding);
                    
                    // Small delay to avoid rate limits
                    await Task.Delay(100);
                }
            }

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
                System.Diagnostics.Debug.WriteLine($"[EmbeddingService] Similarity calculation error: {ex.Message}");
                return 0;
            }
        }
    }
}
