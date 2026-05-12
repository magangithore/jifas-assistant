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
    /// Legacy Gemini Embedding Service (tidak aktif - gunakan OllamaEmbeddingService)
    /// Menggantikan OllamaEmbeddingService - menghasilkan 768-dimensional vectors
    /// Model: models/text-embedding-004 (state-of-the-art, gratis tier tersedia)
    /// </summary>
    public class GeminiEmbeddingService : IEmbeddingService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GeminiEmbeddingService> _logger;
        private readonly string _apiKey;
        private readonly string _embeddingModel;
        private readonly int _timeoutSeconds;
        private const string GEMINI_EMBED_BASE = "https://generativelanguage.googleapis.com/v1beta";

        public GeminiEmbeddingService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<GeminiEmbeddingService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _apiKey = configuration["Gemini:ApiKey"] ?? throw new InvalidOperationException("Gemini:ApiKey is required");
            _embeddingModel = configuration["Embedding:Model"] ?? "models/text-embedding-004";
            _timeoutSeconds = configuration.GetValue<int>("Embedding:TimeoutSeconds", 30);
        }

        /// <summary>
        /// Generate single embedding dari text menggunakan Gemini text-embedding-004
        /// </summary>
        public async Task<byte[]> GenerateEmbeddingAsync(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("[GeminiEmbedding] Empty text provided");
                    return null;
                }

                var embeddings = await GenerateEmbeddingsAsync(new[] { text });
                return embeddings?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError("[GeminiEmbedding] Error generating embedding: {0}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Generate multiple embeddings menggunakan Gemini batchEmbedContents API
        /// </summary>
        public async Task<byte[][]> GenerateEmbeddingsAsync(string[] texts)
        {
            try
            {
                if (texts == null || texts.Length == 0)
                {
                    _logger.LogWarning("[GeminiEmbedding] No texts provided");
                    return Array.Empty<byte[]>();
                }

                // Gemini batchEmbedContents endpoint
                var url = $"{GEMINI_EMBED_BASE}/{_embeddingModel}:batchEmbedContents?key={_apiKey}";

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
                _logger.LogDebug("[GeminiEmbedding] Generating {0} embeddings with {1}", texts.Length, _embeddingModel);

                var response = await _httpClient.PostAsync(url, content, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("[GeminiEmbedding] API error {0}: {1}", response.StatusCode, error);
                    throw new Exception($"Gemini Embedding API returned {response.StatusCode}: {error}");
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

                _logger.LogInformation("[GeminiEmbedding] Generated {0} embeddings successfully", result.Count);
                return result.ToArray();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError("[GeminiEmbedding] HTTP error: {0}", ex.Message);
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("[GeminiEmbedding] Embedding request timeout ({0}s)", _timeoutSeconds);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError("[GeminiEmbedding] Error generating embeddings: {0}", ex.Message);
                throw;
            }
        }
    }
}

