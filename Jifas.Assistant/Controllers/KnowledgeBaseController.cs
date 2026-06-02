using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Jifas.Assistant.Services;
using Jifas.Assistant.Utilities;
using jifas_assistant.DAL.Models;

namespace Jifas.Assistant.Controllers
{
    /// <summary>
    /// Admin controller for managing JIFAS Knowledge Base
    /// </summary>
    [ApiController]
    [Route("api/kb")]
    public class KnowledgeBaseController : ControllerBase
    {
        private readonly JIFAS_AssistantContext _db;
        private readonly IEmbeddingService _embeddingService;
        private readonly ILoggerService _loggerService;
        private readonly IKnowledgeBaseService _knowledgeBaseService;

        public KnowledgeBaseController(
            JIFAS_AssistantContext db,
            IEmbeddingService embeddingService,
            ILoggerService loggerService,
            IKnowledgeBaseService knowledgeBaseService)
        {
            _db = db;
            _embeddingService = embeddingService;
            _loggerService = loggerService;
            _knowledgeBaseService = knowledgeBaseService;
        }

        /// <summary>
        /// Get all knowledge base documents
        /// </summary>
        [HttpGet("documents")]
        public async Task<IActionResult> GetDocuments()
        {
            try
            {
                var documents = await _db.KnowledgeBaseDocuments
                    .Where(d => d.IsActive == true)
                    .Select(d => new
                    {
                        d.Id,
                        d.Title,
                        d.Category,
                        d.Tags,
                        d.CreatedAt,
                        d.UpdatedAt,
                        ChunkCount = d.KnowledgeBaseChunks.Count
                    })
                    .ToListAsync();

                return Ok(documents);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Get a specific document by ID
        /// </summary>
        [HttpGet("documents/{id:int}")]
        public async Task<IActionResult> GetDocument(int id)
        {
            try
            {
                var document = await _db.KnowledgeBaseDocuments
                    .Include(d => d.KnowledgeBaseChunks)
                    .FirstOrDefaultAsync(d => d.Id == id);

                if (document == null)
                    return NotFound();

                return Ok(new
                {
                    document.Id,
                    document.Title,
                    document.Content,
                    document.Category,
                    document.Tags,
                    document.CreatedAt,
                    document.UpdatedAt,
                    document.IsActive,
                    Chunks = document.KnowledgeBaseChunks.Select(c => new
                    {
                        c.Id,
                        c.ChunkIndex,
                        c.Content,
                        c.EmbeddingDimensions
                    })
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Add a new knowledge base document
        /// </summary>
        [Authorize(Policy = "KnowledgeBaseAdmin")]
        [HttpPost("documents")]
        public async Task<IActionResult> AddDocument([FromBody] KBDocumentRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest("Title is required");
            }

            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return BadRequest("Content is required");
            }

            try
            {
                var document = new KnowledgeBaseDocuments
                {
                    Title = request.Title,
                    Content = request.Content,
                    Category = request.Category ?? "General",
                    Tags = request.Tags,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _db.KnowledgeBaseDocuments.Add(document);
                await _db.SaveChangesAsync();

                // Create chunks from content
                await CreateChunksAsync(document.Id, request.Content);

                return Ok(new
                {
                    success = true,
                    documentId = document.Id,
                    message = "Document added successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Update an existing document
        /// </summary>
        [Authorize(Policy = "KnowledgeBaseAdmin")]
        [HttpPut("documents/{id:int}")]
        public async Task<IActionResult> UpdateDocument(int id, [FromBody] KBDocumentRequest request)
        {
            try
            {
                var document = await _db.KnowledgeBaseDocuments.FindAsync(id);
                if (document == null)
                    return NotFound();

                if (!string.IsNullOrWhiteSpace(request.Title))
                    document.Title = request.Title;

                if (!string.IsNullOrWhiteSpace(request.Content))
                {
                    document.Content = request.Content;
                    
                    // Recreate chunks
                    var oldChunks = _db.KnowledgeBaseChunks.Where(c => c.DocumentId == id);
                    _db.KnowledgeBaseChunks.RemoveRange(oldChunks);
                    await CreateChunksAsync(id, request.Content);
                }

                if (!string.IsNullOrWhiteSpace(request.Category))
                    document.Category = request.Category;

                if (!string.IsNullOrWhiteSpace(request.Tags))
                    document.Tags = request.Tags;

                document.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Document updated successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Delete (deactivate) a document
        /// </summary>
        [Authorize(Policy = "KnowledgeBaseAdmin")]
        [HttpDelete("documents/{id:int}")]
        public async Task<IActionResult> DeleteDocument(int id)
        {
            try
            {
                var document = await _db.KnowledgeBaseDocuments.FindAsync(id);
                if (document == null)
                    return NotFound();

                document.IsActive = false;
                document.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Document deleted successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Search knowledge base
        /// </summary>
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string query, [FromQuery] int topK = 5)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest("Query is required");
            }

            try
            {
                var results = await _knowledgeBaseService.SearchAsync(query, topK);

                return Ok(results.Select(r => new
                {
                    r.DocumentId,
                    r.Title,
                    r.Content,
                    r.Category,
                    r.Score
                }));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Create chunks from document content and generate embeddings
        /// </summary>
        private async Task CreateChunksAsync(int documentId, string content, int chunkSize = 500, int overlap = 50)
        {
            System.Diagnostics.Debug.WriteLine($"[KB Upload] Creating chunks for document {documentId}...");

            var chunks = new List<KnowledgeBaseChunks>();
            var paragraphs = content.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            int chunkIndex = 0;
            var currentChunk = "";

            foreach (var paragraph in paragraphs)
            {
                if ((currentChunk + paragraph).Length > chunkSize && !string.IsNullOrEmpty(currentChunk))
                {
                    chunks.Add(new KnowledgeBaseChunks
                    {
                        DocumentId = documentId,
                        ChunkIndex = chunkIndex++,
                        Content = currentChunk.Trim(),
                        CreatedAt = DateTime.UtcNow
                    });

                    // Keep overlap
                    var words = currentChunk.Split(' ');
                    currentChunk = string.Join(" ", words.Skip(Math.Max(0, words.Length - overlap))) + "\n\n";
                }

                currentChunk += paragraph + "\n\n";
            }

            // Add remaining content
            if (!string.IsNullOrWhiteSpace(currentChunk))
            {
                chunks.Add(new KnowledgeBaseChunks
                {
                    DocumentId = documentId,
                    ChunkIndex = chunkIndex,
                    Content = currentChunk.Trim(),
                    CreatedAt = DateTime.UtcNow
                });
            }

            _db.KnowledgeBaseChunks.AddRange(chunks);
            await _db.SaveChangesAsync();

            System.Diagnostics.Debug.WriteLine($"[KB Upload] Created {chunks.Count} chunks, now generating embeddings...");

            // Generate embeddings for each chunk
            try
            {
                int successCount = 0;

                foreach (var chunk in chunks)
                {
                    try
                    {
                        var embedding = await _embeddingService.GenerateEmbeddingAsFloatArrayAsync(chunk.Content);
                        
                        if (embedding != null && embedding.Length > 0)
                        {
                            chunk.Embedding = EmbeddingSerializer.Serialize(embedding);
                            chunk.EmbeddingVector = new Vector(embedding);
                            chunk.EmbeddingDimensions = embedding.Length;
                            successCount++;
                            System.Diagnostics.Debug.WriteLine($"[KB Upload] ? Chunk {chunk.ChunkIndex}: {embedding.Length}-dim embedding");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[KB Upload] ? Chunk {chunk.ChunkIndex}: Failed to generate embedding");
                        }

                        // Small delay to avoid rate limits
                        await Task.Delay(100);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[KB Upload] Error generating embedding for chunk {chunk.ChunkIndex}: {ex.Message}");
                    }
                }

                // Re-fetch chunks from database to ensure they're tracked and update embeddings
                var chunkIds = chunks.Select(c => c.Id).ToList();
                var trackedChunks = await _db.KnowledgeBaseChunks
                    .Where(c => chunkIds.Contains(c.Id))
                    .ToListAsync();

                // Update embeddings for each tracked chunk
                foreach (var trackedChunk in trackedChunks)
                {
                    var sourceChunk = chunks.FirstOrDefault(c => c.Id == trackedChunk.Id);
                    if (sourceChunk?.Embedding != null)
                    {
                        trackedChunk.Embedding = sourceChunk.Embedding;
                        trackedChunk.EmbeddingVector = sourceChunk.EmbeddingVector;
                        trackedChunk.EmbeddingDimensions = sourceChunk.EmbeddingDimensions;
                    }
                }

                await _db.SaveChangesAsync();
                System.Diagnostics.Debug.WriteLine($"[KB Upload] ? Generated embeddings for {successCount}/{chunks.Count()} chunks");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[KB Upload] Error in embedding generation: {ex.Message}");
                // Continue even if embedding generation fails - chunks are still created
            }
        }

        /// <summary>
        /// Get Knowledge Base statistics
        /// </summary>
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            try
            {
                var docCount = await _db.KnowledgeBaseDocuments.CountAsync(d => d.IsActive == true);
                var chunkCount = await _db.KnowledgeBaseChunks.CountAsync();
                var chunksWithEmbeddings = await _db.KnowledgeBaseChunks.CountAsync(c => c.Embedding != null);

                return Ok(new
                {
                    totalDocuments = docCount,
                    totalChunks = chunkCount,
                    chunksWithEmbeddings = chunksWithEmbeddings,
                    embeddingCoverage = chunkCount > 0 ? Math.Round((double)chunksWithEmbeddings / chunkCount * 100, 2) + "%" : "0%",
                    lastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Error getting stats: {ex.Message}" });
            }
        }

        /// <summary>
        /// Generate embeddings for chunks with NULL embeddings
        /// </summary>
        [Authorize(Policy = "KnowledgeBaseAdmin")]
        [HttpPost("generate-embeddings")]
        public async Task<IActionResult> GenerateEmbeddings()
        {
            try
            {
                // Get all chunks with NULL embeddings
                var nullEmbeddingChunks = await _db.KnowledgeBaseChunks
                    .Where(c => c.Embedding == null || c.Embedding == "")
                    .ToListAsync();

                if (nullEmbeddingChunks.Count == 0)
                {
                    return Ok(new
                    {
                        message = "No chunks with NULL embeddings found",
                        processedCount = 0,
                        successCount = 0
                    });
                }

                System.Diagnostics.Debug.WriteLine($"[KB Repair] Processing {nullEmbeddingChunks.Count} chunks with NULL embeddings...");

                int successCount = 0;
                var failedChunks = new List<int>();

                foreach (var chunk in nullEmbeddingChunks)
                {
                    try
                    {
                        var embedding = await _embeddingService.GenerateEmbeddingAsFloatArrayAsync(chunk.Content);

                        if (embedding != null && embedding.Length > 0)
                        {
                            chunk.Embedding = EmbeddingSerializer.Serialize(embedding);
                            chunk.EmbeddingVector = new Vector(embedding);
                            chunk.EmbeddingDimensions = embedding.Length;
                            chunk.UpdatedAt = DateTime.UtcNow;
                            successCount++;
                            System.Diagnostics.Debug.WriteLine($"[KB Repair] ? Chunk {chunk.Id}: {embedding.Length}-dim embedding generated");
                        }
                        else
                        {
                            failedChunks.Add(chunk.Id);
                            System.Diagnostics.Debug.WriteLine($"[KB Repair] ? Chunk {chunk.Id}: Failed to generate embedding (null response)");
                        }

                        // Small delay to avoid rate limits
                        await Task.Delay(100);
                    }
                    catch (Exception ex)
                    {
                        failedChunks.Add(chunk.Id);
                        System.Diagnostics.Debug.WriteLine($"[KB Repair] Error generating embedding for chunk {chunk.Id}: {ex.Message}");
                    }
                }

                // Save all changes
                if (successCount > 0)
                {
                    await _db.SaveChangesAsync();
                    System.Diagnostics.Debug.WriteLine($"[KB Repair] ? Successfully generated {successCount}/{nullEmbeddingChunks.Count} embeddings");
                }

                return Ok(new
                {
                    message = "Embedding generation completed",
                    totalChunksProcessed = nullEmbeddingChunks.Count,
                    successCount = successCount,
                    failedCount = failedChunks.Count,
                    failedChunkIds = failedChunks,
                    successRate = nullEmbeddingChunks.Count > 0 ? Math.Round((double)successCount / nullEmbeddingChunks.Count * 100, 2) + "%" : "0%"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Error generating embeddings: {ex.Message}", details = ex.InnerException?.Message });
            }
        }
    }

    /// <summary>
    /// Request model for adding/updating KB documents
    /// </summary>
    public class KBDocumentRequest
    {
        public string? Title { get; set; }
        public string? Content { get; set; }
        public string? Category { get; set; }
        public string? Tags { get; set; }
    }
}
