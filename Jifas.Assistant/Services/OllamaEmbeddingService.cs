using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Ollama-based embedding service menggunakan qwen3-embedding:4b model
    /// Menghasilkan 1024-dimensional vectors
    /// </summary>
    public class OllamaEmbeddingService : IEmbeddingService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<OllamaEmbeddingService> _logger;
        private readonly string _ollamaBaseUrl;
        private readonly string _embeddingModel;
        private readonly int _timeoutSeconds;

        public OllamaEmbeddingService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<OllamaEmbeddingService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _ollamaBaseUrl = configuration["Embedding:OllamaUrl"] ?? "http://10.0.12.54:11434";
            _embeddingModel = configuration["Embedding:Model"] ?? "qwen3-embedding:4b";
            _timeoutSeconds = configuration.GetValue<int>("Embedding:TimeoutSeconds", 30);
        }

        /// <summary>
        /// Generate embedding (vector) dari text menggunakan Ollama
        /// </summary>
        public async Task<byte[]> GenerateEmbeddingAsync(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("Text kosong untuk embedding");
                    return null;
                }

                var embeddings = await GenerateEmbeddingsAsync(new[] { text });
                return embeddings?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error generating embedding: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Generate multiple embeddings sekaligus
        /// </summary>
        public async Task<byte[][]> GenerateEmbeddingsAsync(string[] texts)
        {
            try
            {
                if (texts == null || texts.Length == 0)
                {
                    _logger.LogWarning("No texts provided for embedding");
                    return Array.Empty<byte[]>();
                }

                var url = $"{_ollamaBaseUrl}/api/embed";
                
                var requestBody = new
                {
                    model = _embeddingModel,
                    input = texts.ToList()
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Set timeout
                var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));

                _logger.LogDebug($"Calling Ollama embedding API: {url}");
                _logger.LogDebug($"Model: {_embeddingModel}, Texts count: {texts.Length}");

                var response = await _httpClient.PostAsync(url, content, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Ollama API error: {response.StatusCode} - {errorContent}");
                    throw new Exception($"Ollama API returned {response.StatusCode}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var jsonDocument = JsonDocument.Parse(responseContent);
                var embeddings = jsonDocument.RootElement.GetProperty("embeddings");

                var result = new List<byte[]>();

                foreach (var embeddingArray in embeddings.EnumerateArray())
                {
                    // Convert float[] to byte[]
                    var floatValues = embeddingArray.EnumerateArray()
                        .Select(e => (float)e.GetDouble())
                        .ToArray();

                    // Serialize float array to byte array
                    using (var memoryStream = new MemoryStream())
                    {
                        using (var writer = new BinaryWriter(memoryStream))
                        {
                            foreach (var floatValue in floatValues)
                            {
                                writer.Write(floatValue);
                            }
                        }
                        result.Add(memoryStream.ToArray());
                    }
                }

                _logger.LogInformation($"Generated {result.Count} embeddings successfully");
                return result.ToArray();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError($"HTTP error calling Ollama: {ex.Message}");
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.LogError($"Embedding request timeout ({_timeoutSeconds}s)");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error generating embeddings: {ex.Message}");
                throw;
            }
        }
    }
}
