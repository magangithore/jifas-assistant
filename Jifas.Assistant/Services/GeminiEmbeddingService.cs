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
    /// Legacy Embedding Service (tidak aktif - gunakan OllamaEmbeddingService)
    /// Menghasilkan embedding vectors untuk Knowledge Base
    /// Model dikonfigurasi via Embedding:Model di appsettings.json
    /// </summary>
    public class OllamaLegacyEmbeddingService : IEmbeddingService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<OllamaLegacyEmbeddingService> _logger;
        private readonly string _apiKey;
        private readonly string _embeddingModel;
        private readonly int _timeoutSeconds;
        private const string OLLAMA_EMBED_BASE = "http://10.0.12.54:11434";

        public OllamaLegacyEmbeddingService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<OllamaLegacyEmbeddingService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _apiKey = configuration["Ollama:ApiKey"] ?? string.Empty;
            _embeddingModel = configuration["Embedding:Model"] ?? "qwen3-embedding:4b";
            _timeoutSeconds = configuration.GetValue<int>("Embedding:TimeoutSeconds", 30);
        }

        /// <summary>
        /// Generate single embedding dari text
        /// </summary>
        public async Task<byte[]> GenerateEmbeddingAsync(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("[OllamaEmbedding] Empty text provided");
                    return null;
                }

                var embeddings = await GenerateEmbeddingsAsync(new[] { text });
                return embeddings?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError("[OllamaEmbedding] Error generating embedding: {0}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Generate multiple embeddings menggunakan Ollama batchEmbedContents API
        /// </summary>
        public async Task<byte[][]> GenerateEmbeddingsAsync(string[] texts)
        {
            try
            {
                if (texts == null || texts.Length == 0)
                {
                    _logger.LogWarning("[OllamaEmbedding] No texts provided");
                    return Array.Empty<byte[]>();
                }

                // Ollama embedding endpoint
                var url = $"{OLLAMA_EMBED_BASE}/{_embeddingModel}:batchEmbedContents?key={_apiKey}";

                // Build batch request
                var requests = texts.Select(t => new
                {
                    model = _embeddingModel,
                    content = new
                    {
                        parts = new[] { new { text = t } }
                    },
                    taskType = "RETRIEVAL_DOCUMENT"
                }).ToArray();

                var requestBody = new { requests };
                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
                _logger.LogDebug("[OllamaEmbedding] Generating {0} embeddings with {1}", texts.Length, _embeddingModel);

                var response = await _httpClient.PostAsync(url, content, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("[OllamaEmbedding] API error {0}: {1}", response.StatusCode, error);
                    throw new Exception($"Ollama Embedding API returned {response.StatusCode}: {error}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var jsonDocument = JsonDocument.Parse(responseContent);

                // Parse batchEmbedContents response: { embeddings: [{ values: [...] }, ...] }
                var embeddings = jsonDocument.RootElement.GetProperty("embeddings");
                var result = new List<byte[]>();

                foreach (var embeddingObj in embeddings.EnumerateArray())
                {
                    var values = embeddingObj.GetProperty("values");
                    var floatValues = values.EnumerateArray()
                        .Select(e => (float)e.GetDouble())
                        .ToArray();

                    using var memoryStream = new MemoryStream();
                    using var writer = new BinaryWriter(memoryStream);
                    foreach (var floatValue in floatValues)
                        writer.Write(floatValue);

                    result.Add(memoryStream.ToArray());
                }

                _logger.LogInformation("[OllamaEmbedding] Generated {0} embeddings successfully", result.Count);
                return result.ToArray();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError("[OllamaEmbedding] HTTP error: {0}", ex.Message);
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("[OllamaEmbedding] Embedding request timeout ({0}s)", _timeoutSeconds);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError("[OllamaEmbedding] Error generating embeddings: {0}", ex.Message);
                throw;
            }
        }
    }
}



