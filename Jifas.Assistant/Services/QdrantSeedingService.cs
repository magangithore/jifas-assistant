using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Jifas.Assistant.Data;
using Jifas.Assistant.Data.Models;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Interface for Qdrant seeding service
    /// </summary>
    public interface IQdrantSeedingService
    {
        Task<QdrantSeedingResult> SeedAllDocumentsAsync();
        Task<bool> SeedDocumentAsync(int documentId);
        Task<QdrantSeedingResult> ReseedAllDocumentsAsync();
    }

    /// <summary>
    /// Result object for Qdrant seeding operations
    /// Tracks success, document count, points created, and errors
    /// </summary>
    public class QdrantSeedingResult
    {
        public bool Success { get; set; }
        public int DocumentsProcessed { get; set; }
        public int DocumentsFailed { get; set; }
        public int PointsCreated { get; set; }
        public string Message { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }

        public TimeSpan Duration => CompletedAt - StartedAt;

        public override string ToString()
        {
            return $"Seeding Result: {PointsCreated} points created from {DocumentsProcessed} documents in {Duration.TotalSeconds:F2}s (Failed: {DocumentsFailed})";
        }
    }

    /// <summary>
    /// Service to seed (index) Knowledge Base documents to Qdrant vector database
    /// 
    /// Batch loads documents from SQL Server and indexes them as vectors in Qdrant.
    /// Supports seeding all documents, single documents, and reseeding (clear + repopulate).
    /// Compatible with .NET 10 and uses proper dependency injection.
    /// </summary>
    public class QdrantSeedingService : IQdrantSeedingService
    {
        private readonly ILoggerService _logger;
        private readonly IConfiguration _configuration;
        private readonly JifasAssistantDbContext _db;
        private readonly IEmbeddingService _embeddingService;
        private readonly HttpClient _httpClient;
        private readonly string _qdrantUrl;
        private readonly string _collectionName;
        private const int BATCH_SIZE = 10;

        /// <summary>
        /// Initialize Qdrant seeding service with dependency injection
        /// </summary>
        public QdrantSeedingService(
            ILoggerService logger,
            IConfiguration configuration,
            JifasAssistantDbContext db,
            IEmbeddingService embeddingService,
            HttpClient httpClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

            // Read Qdrant configuration with defaults
            _qdrantUrl = _configuration.GetValue("Qdrant:Url", "http://localhost:6333");
            _collectionName = _configuration.GetValue("Qdrant:CollectionName", "jifas_kb");

            _logger.LogInformation("[QdrantSeedingService] Initialized - URL: {0}, Collection: {1}", 
                _qdrantUrl, _collectionName);
        }

        /// <summary>
        /// Seed all active knowledge base documents to Qdrant
        /// Processes documents in batches to manage memory and API rate limits
        /// </summary>
        public async Task<QdrantSeedingResult> SeedAllDocumentsAsync()
        {
            var result = new QdrantSeedingResult
            {
                StartedAt = DateTime.UtcNow
            };

            try
            {
                _logger.LogInformation("[QdrantSeedingService] Starting to seed all documents");

                // Get all active documents from database
                var documents = await _db.KnowledgeBaseDocuments
                    .Where(d => d.IsActive == true)
                    .OrderBy(d => d.Id)
                    .ToListAsync();

                if (documents.Count == 0)
                {
                    _logger.LogWarning("[QdrantSeedingService] No active documents found to seed");
                    result.Success = false;
                    result.Message = "No active documents found";
                    result.CompletedAt = DateTime.UtcNow;
                    return result;
                }

                _logger.LogInformation("[QdrantSeedingService] Found {0} documents to seed", documents.Count);

                var failedDocs = new List<int>();
                int pointsCreated = 0;

                // Process documents in batches
                for (int i = 0; i < documents.Count; i += BATCH_SIZE)
                {
                    var batch = documents.Skip(i).Take(BATCH_SIZE).ToList();

                    foreach (var doc in batch)
                    {
                        try
                        {
                            var success = await SeedDocumentAsync(doc.Id);
                            if (success)
                            {
                                pointsCreated++;
                            }
                            else
                            {
                                failedDocs.Add(doc.Id);
                                result.Errors.Add($"Failed to seed document {doc.Id}: {doc.Title}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("[QdrantSeedingService] Failed to seed document {0}: {1}", doc.Id, ex.Message);
                            failedDocs.Add(doc.Id);
                            result.Errors.Add($"Document {doc.Id}: {ex.Message}");
                        }
                    }

                    var progressPercent = (int)((i + batch.Count) / (double)documents.Count * 100);
                    _logger.LogDebug("[QdrantSeedingService] Progress: {0}% ({1}/{2} documents)", 
                        progressPercent, i + batch.Count, documents.Count);
                }

                result.Success = failedDocs.Count == 0;
                result.DocumentsProcessed = documents.Count;
                result.DocumentsFailed = failedDocs.Count;
                result.PointsCreated = pointsCreated;
                result.Message = $"Successfully seeded {pointsCreated} documents ({failedDocs.Count} failed)";
                result.CompletedAt = DateTime.UtcNow;

                _logger.LogInformation("[QdrantSeedingService] Seeding completed: {0}", result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantSeedingService] Error seeding documents: {0}", ex, ex.Message);
                result.Success = false;
                result.Message = ex.Message;
                result.CompletedAt = DateTime.UtcNow;
                return result;
            }
        }

        /// <summary>
        /// Seed a specific document to Qdrant
        /// Generates embedding and upserts point to collection
        /// </summary>
        public async Task<bool> SeedDocumentAsync(int documentId)
        {
            try
            {
                var document = await _db.KnowledgeBaseDocuments
                    .FirstOrDefaultAsync(d => d.Id == documentId && d.IsActive == true);

                if (document == null)
                {
                    _logger.LogWarning("[QdrantSeedingService] Document {0} not found or not active", documentId);
                    return false;
                }

                // Generate embedding for document
                var textToEmbed = $"{document.Title} {document.Content}".Trim();
                if (string.IsNullOrWhiteSpace(textToEmbed))
                {
                    _logger.LogWarning("[QdrantSeedingService] Document {0} has no text to embed", documentId);
                    return false;
                }

                var embedding = await _embeddingService.GenerateEmbeddingAsync(textToEmbed);

                if (embedding == null || embedding.Count == 0)
                {
                    _logger.LogWarning("[QdrantSeedingService] Failed to generate embedding for document {0}", documentId);
                    return false;
                }

                // Create point for Qdrant
                var point = new
                {
                    id = documentId,
                    vector = embedding,
                    payload = new
                    {
                        document_id = documentId,
                        title = document.Title,
                        content = document.Content,
                        category = document.Category,
                        created_at = document.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        updated_at = document.UpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss")
                    }
                };

                // Upsert point to Qdrant
                var success = await UpsertPointToQdrantAsync(point);

                if (success)
                {
                    _logger.LogDebug("[QdrantSeedingService] Successfully seeded document {0}", documentId);
                }
                else
                {
                    _logger.LogWarning("[QdrantSeedingService] Failed to upsert point for document {0} to Qdrant", documentId);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantSeedingService] Error seeding document {0}: {1}", ex, documentId, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Clear all vectors from Qdrant collection and reseed all documents
        /// </summary>
        public async Task<QdrantSeedingResult> ReseedAllDocumentsAsync()
        {
            var result = new QdrantSeedingResult
            {
                StartedAt = DateTime.UtcNow
            };

            try
            {
                _logger.LogInformation("[QdrantSeedingService] Starting reseed operation");

                // Delete all points from collection
                var deleteSuccess = await DeleteAllPointsAsync();
                if (!deleteSuccess)
                {
                    _logger.LogWarning("[QdrantSeedingService] Failed to clear collection for reseed");
                    result.Success = false;
                    result.Message = "Failed to clear existing collection";
                    result.CompletedAt = DateTime.UtcNow;
                    return result;
                }

                _logger.LogInformation("[QdrantSeedingService] Collection cleared, starting reseed");

                // Reseed all documents
                var seedResult = await SeedAllDocumentsAsync();

                result.Success = seedResult.Success;
                result.DocumentsProcessed = seedResult.DocumentsProcessed;
                result.DocumentsFailed = seedResult.DocumentsFailed;
                result.PointsCreated = seedResult.PointsCreated;
                result.Message = $"Reseed completed: {seedResult.Message}";
                result.Errors.AddRange(seedResult.Errors);
                result.CompletedAt = DateTime.UtcNow;

                _logger.LogInformation("[QdrantSeedingService] Reseed completed: {0}", result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantSeedingService] Error during reseed: {0}", ex, ex.Message);
                result.Success = false;
                result.Message = ex.Message;
                result.CompletedAt = DateTime.UtcNow;
                return result;
            }
        }

        /// <summary>
        /// Upsert a single point to Qdrant collection
        /// </summary>
        private async Task<bool> UpsertPointToQdrantAsync(object point)
        {
            try
            {
                var url = $"{_qdrantUrl.TrimEnd('/')}/collections/{_collectionName}/points?wait=true";
                var payload = new { points = new[] { point } };
                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PutAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("[QdrantSeedingService] Qdrant API error ({0}): {1}", 
                        response.StatusCode, errorContent);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantSeedingService] Error upserting point to Qdrant: {0}", ex, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Delete all points from Qdrant collection
        /// </summary>
        private async Task<bool> DeleteAllPointsAsync()
        {
            try
            {
                var url = $"{_qdrantUrl.TrimEnd('/')}/collections/{_collectionName}/points/delete";
                var payload = new { filter = new { must = new object[0] } };
                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("[QdrantSeedingService] Failed to delete points ({0}): {1}", 
                        response.StatusCode, errorContent);
                    return false;
                }

                _logger.LogInformation("[QdrantSeedingService] Successfully cleared Qdrant collection");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantSeedingService] Error deleting points from Qdrant: {0}", ex, ex.Message);
                return false;
            }
        }
    }
}
