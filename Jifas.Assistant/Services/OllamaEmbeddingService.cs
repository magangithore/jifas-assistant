using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Ollama-based embedding service menggunakan qwen3-embedding:4b model
    /// Menghasilkan embedding vector dari Ollama sesuai model yang dikonfigurasi
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
        public async Task<byte[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("Text kosong untuk embedding");
                    return Array.Empty<byte>();
                }

                var embeddings = await GenerateEmbeddingsAsync(new[] { text }, cancellationToken);
                return embeddings.FirstOrDefault() ?? Array.Empty<byte>();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Embedding request cancelled by caller.");
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning($"Embedding request timeout ({_timeoutSeconds}s).");
                throw;
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
        public async Task<byte[][]> GenerateEmbeddingsAsync(string[] texts, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

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

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                _logger.LogDebug($"Calling Ollama embedding API: {url}");
                _logger.LogDebug($"Model: {_embeddingModel}, Texts count: {texts.Length}");

                var response = await _httpClient.PostAsync(url, content, linkedCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(linkedCts.Token);
                    _logger.LogError($"Ollama API error: {response.StatusCode} - {errorContent}");
                    throw new Exception($"Ollama API returned {response.StatusCode}");
                }

                var responseContent = await response.Content.ReadAsStringAsync(linkedCts.Token);
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Embedding request cancelled by caller.");
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning($"Embedding request timeout ({_timeoutSeconds}s).");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error generating embeddings: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Generate embedding as float[] for direct use in semantic search
        /// </summary>
        public async Task<float[]> GenerateEmbeddingAsFloatArrayAsync(string text, CancellationToken cancellationToken = default)
        {
            try
            {
                var bytes = await GenerateEmbeddingAsync(text, cancellationToken);
                return bytes?.ToFloatArray() ?? Array.Empty<float>();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Float embedding request cancelled by caller.");
                return Array.Empty<float>();
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning($"Float embedding request timeout ({_timeoutSeconds}s).");
                return Array.Empty<float>();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error generating float embedding: {ex.Message}");
                return Array.Empty<float>();
            }
        }
    }
}
