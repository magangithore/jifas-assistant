using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using Jifas.Chatbot.DAL;
using Newtonsoft.Json;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Service to seed (index) Knowledge Base documents to Qdrant vector database
    /// Batch loads documents from SQL Server and indexes them as vectors
    /// </summary>
    public class QdrantSeedingResult
    {
        public int TotalDocuments { get; set; }
        public int TotalChunks { get; set; }
        public int SuccessfullyIndexed { get; set; }
        public int Failed { get; set; }
        public List<string> Errors { get; set; }
        public TimeSpan Duration { get; set; }

        public QdrantSeedingResult()
        {
            Errors = new List<string>();
        }

        public override string ToString()
        {
            return $"Seeding Result: {SuccessfullyIndexed}/{TotalChunks} chunks indexed from {TotalDocuments} documents in {Duration.TotalSeconds:F2}s (Failed: {Failed})";
        }
    }

    /// <summary>
    /// Implementation of KB seeding service
    /// </summary>
    public class QdrantSeedingService : IQdrantSeedingService
    {
        private readonly JIFAS_AssistantEntities _db;
        private readonly IQdrantVectorService _qdrantVectorService;
        private readonly IEmbeddingService _embeddingService;
        private readonly ILoggerService _logger;

        public QdrantSeedingService()
        {
            _db = new JIFAS_AssistantEntities();
            _qdrantVectorService = new QdrantVectorService(new GeminiEmbeddingService());
            
            // Initialize embedding service (same as KnowledgeBaseService)
            var geminiKey = System.Configuration.ConfigurationManager.AppSettings["Gemini:ApiKey"];
            if (!string.IsNullOrEmpty(geminiKey))
            {
                _embeddingService = new GeminiEmbeddingService();
            }
            else
            {
                _embeddingService = new OpenAIEmbeddingService();
            }
            
            _logger = LoggerFactory.GetLogger();
        }

        public QdrantSeedingService(JIFAS_AssistantEntities db, IQdrantVectorService qdrantVectorService, IEmbeddingService embeddingService)
        {
            _db = db;
            _qdrantVectorService = qdrantVectorService;
            _embeddingService = embeddingService;
            _logger = LoggerFactory.GetLogger();
        }

        /// <summary>
        /// Seed all knowledge base documents to Qdrant
        /// Loads chunks from SQL Server and indexes them as vectors in Qdrant
        /// </summary>
        public async Task<QdrantSeedingResult> SeedAllDocumentsAsync()
        {
            var startTime = DateTime.Now;
            var result = new QdrantSeedingResult();

            try
            {
                _logger.LogInformation("[QdrantSeedingService] Starting KB seeding...");

                // Get all active documents
                var documents = await _db.KnowledgeBaseDocuments
                    .Where(d => d.IsActive == true)
                    .ToListAsync();

                result.TotalDocuments = documents.Count;
                _logger.LogInformation("[QdrantSeedingService] Found {0} active documents", documents.Count);

                if (documents.Count == 0)
                {
                    _logger.LogWarning("[QdrantSeedingService] No active documents found");
                    result.Duration = DateTime.Now - startTime;
                    return result;
                }

                // Get all chunks for active documents
                var documentIds = documents.Select(d => d.Id).ToList();
                var chunks = await _db.KnowledgeBaseChunks
                    .Where(c => documentIds.Contains(c.DocumentId))
                    .Include(c => c.KnowledgeBaseDocuments)
                    .ToListAsync();

                result.TotalChunks = chunks.Count;
                _logger.LogInformation("[QdrantSeedingService] Found {0} total chunks to index", chunks.Count);

                // Process chunks in batches
                var batchSize = 50;
                for (int i = 0; i < chunks.Count; i += batchSize)
                {
                    var batch = chunks.Skip(i).Take(batchSize).ToList();
                    await ProcessChunkBatchAsync(batch, result);

                    // Log progress
                    var progressPercent = (int)((i + batch.Count) / (double)chunks.Count * 100);
                    _logger.LogInformation("[QdrantSeedingService] Progress: {0}% ({1}/{2} chunks)", 
                        progressPercent, i + batch.Count, chunks.Count);
                }

                result.Duration = DateTime.Now - startTime;
                _logger.LogInformation("[QdrantSeedingService] Seeding completed: {0}", result);
                return result;
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Errors.Add($"Seeding error: {ex.Message}");
                _logger.LogError("[QdrantSeedingService] Seeding failed: " + ex.Message);
                result.Duration = DateTime.Now - startTime;
                return result;
            }
        }

        /// <summary>
        /// Seed specific document to Qdrant
        /// </summary>
        public async Task<bool> SeedDocumentAsync(int documentId)
        {
            try
            {
                _logger.LogInformation("[QdrantSeedingService] Seeding document ID: {0}", documentId);

                var chunks = await _db.KnowledgeBaseChunks
                    .Where(c => c.DocumentId == documentId)
                    .Include(c => c.KnowledgeBaseDocuments)
                    .ToListAsync();

                if (chunks.Count == 0)
                {
                    _logger.LogWarning("[QdrantSeedingService] No chunks found for document {0}", documentId);
                    return false;
                }

                foreach (var chunk in chunks)
                {
                    // Generate embedding for chunk content
                    var embedding = await _embeddingService.GenerateEmbeddingAsync(chunk.Content);
                    
                    if (embedding == null || embedding.Count == 0)
                    {
                        _logger.LogWarning("[QdrantSeedingService] Failed to generate embedding for chunk {0}", chunk.Id);
                        continue;
                    }

                    // Prepare metadata
                    var metadata = new Dictionary<string, object>
                    {
                        { "document_id", chunk.DocumentId },
                        { "chunk_index", chunk.ChunkIndex },
                        { "title", chunk.KnowledgeBaseDocuments?.Title ?? "Unknown" },
                        { "category", chunk.KnowledgeBaseDocuments?.Category ?? "Uncategorized" },
                        { "content", chunk.Content },
                        { "created_at", chunk.CreatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "" },
                        { "updated_at", chunk.KnowledgeBaseDocuments?.UpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "" }
                    };

                    // Index to Qdrant
                    var chunkPointId = GeneratePointId(chunk.DocumentId, chunk.ChunkIndex);
                    await _qdrantVectorService.IndexDocumentAsync(chunkPointId.ToString(), embedding, metadata);
                }

                _logger.LogInformation("[QdrantSeedingService] Successfully seeded document {0} ({1} chunks)", documentId, chunks.Count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantSeedingService] Error seeding document " + documentId + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Clear all vectors from Qdrant and reseed from scratch
        /// </summary>
        public async Task<QdrantSeedingResult> ReseedAllDocumentsAsync()
        {
            try
            {
                _logger.LogInformation("[QdrantSeedingService] Starting RESEED (delete + recreate)...");

                // Delete existing collection
                _logger.LogInformation("[QdrantSeedingService] Deleting existing Qdrant collection...");
                await _qdrantVectorService.DeleteCollectionAsync();

                // Wait a moment for deletion to complete
                await Task.Delay(2000);

                // Reinitialize collection
                _logger.LogInformation("[QdrantSeedingService] Creating new Qdrant collection...");
                await _qdrantVectorService.InitializeCollectionAsync();

                // Wait for collection to be ready
                await Task.Delay(2000);

                // Seed all documents
                return await SeedAllDocumentsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError("[QdrantSeedingService] Reseed failed: " + ex.Message);
                return new QdrantSeedingResult
                {
                    Errors = new List<string> { ex.Message },
                    Duration = TimeSpan.Zero
                };
            }
        }

        /// <summary>
        /// Process a batch of chunks, generating embeddings and indexing to Qdrant
        /// </summary>
        private async Task ProcessChunkBatchAsync(List<KnowledgeBaseChunks> chunks, QdrantSeedingResult result)
        {
            foreach (var chunk in chunks)
            {
                try
                {
                    // Generate embedding for chunk content
                    var embedding = await _embeddingService.GenerateEmbeddingAsync(chunk.Content);
                    
                    if (embedding == null || embedding.Count == 0)
                    {
                        result.Failed++;
                        result.Errors.Add($"Failed to generate embedding for chunk {chunk.Id}");
                        _logger.LogWarning("[QdrantSeedingService] Failed to generate embedding for chunk {0}", chunk.Id);
                        continue;
                    }

                    // Prepare metadata payload for Qdrant
                    var metadata = new Dictionary<string, object>
                    {
                        { "document_id", chunk.DocumentId },
                        { "chunk_id", chunk.Id },
                        { "chunk_index", chunk.ChunkIndex },
                        { "title", chunk.KnowledgeBaseDocuments?.Title ?? "Unknown" },
                        { "category", chunk.KnowledgeBaseDocuments?.Category ?? "Uncategorized" },
                        { "content", chunk.Content },
                        { "created_at", chunk.CreatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "" },
                        { "updated_at", chunk.KnowledgeBaseDocuments?.UpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "" },
                        { "token_count", chunk.TokenCount ?? 0 }
                    };

                    // Generate unique point ID (combination of document ID and chunk index)
                    var pointId = GeneratePointId(chunk.DocumentId, chunk.ChunkIndex);

                    // Index to Qdrant
                    await _qdrantVectorService.IndexDocumentAsync(pointId.ToString(), embedding, metadata);
                    
                    result.SuccessfullyIndexed++;

                    // Optional: Update SQL embedding vector for reference
                    if (string.IsNullOrEmpty(chunk.EmbeddingVector))
                    {
                        chunk.EmbeddingVector = JsonConvert.SerializeObject(embedding);
                    }
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add($"Chunk {chunk.Id} error: {ex.Message}");
                    _logger.LogWarning("[QdrantSeedingService] Failed to index chunk {0}: {1}", chunk.Id, ex.Message);
                }
            }

            // Save any embedding vector updates to SQL (optional)
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[QdrantSeedingService] Warning: Could not save embeddings to SQL: {0}", ex.Message);
                // Don't fail seeding if SQL save fails - Qdrant indexing is what matters
            }
        }

        /// <summary>
        /// Generate unique point ID from document ID and chunk index
        /// Ensures consistency across reseeding
        /// </summary>
        private long GeneratePointId(int documentId, int chunkIndex)
        {
            // Example: Doc 1, Chunk 0 = 100000
            //         Doc 1, Chunk 1 = 100001
            //         Doc 2, Chunk 0 = 200000
            return (long)documentId * 100000 + chunkIndex;
        }
    }
}
