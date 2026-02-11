using Jifas.DAL;
using Jifas.Chatbot.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Jifas.Assistant.Controllers
{
    /// <summary>
    /// Admin controller for managing JIFAS Knowledge Base
    /// </summary>
    [Route("api/kb")]
    [ApiController]
    public class KnowledgeBaseController : ControllerBase
    {
        private readonly JifasAssistantDbContext _db;

        public KnowledgeBaseController(JifasAssistantDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Get all knowledge base documents
        /// </summary>
        [HttpGet("documents")]
        public async Task<ActionResult> GetDocuments()
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
                        d.Department,
                        d.Tags,
                        d.Version,
                        d.CreatedAt,
                        d.UpdatedAt,
                        ChunkCount = d.KnowledgeBaseChunks.Count
                    })
                    .ToListAsync();

                return Ok(documents);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Get a specific document by ID
        /// </summary>
        [HttpGet("documents/{id:int}")]
        public async Task<ActionResult> GetDocument(int id)
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
                    document.Department,
                    document.Tags,
                    document.SourceFile,
                    document.Version,
                    document.CreatedAt,
                    document.UpdatedAt,
                    document.IsActive,
                    Chunks = document.KnowledgeBaseChunks.Select(c => new
                    {
                        c.Id,
                        c.ChunkIndex,
                        c.Content,
                        c.TokenCount
                    })
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Add a new knowledge base document
        /// </summary>
        [HttpPost("documents")]
        public async Task<ActionResult> AddDocument([FromBody] KBDocumentRequest request)
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
                    Department = request.Department ?? "JIFAS",
                    Tags = request.Tags,
                    SourceFile = request.SourceFile,
                    Version = request.Version ?? "1.0",
                    IsActive = true,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
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
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Update an existing document
        /// </summary>
        [HttpPut("documents/{id:int}")]
        public async Task<ActionResult> UpdateDocument(int id, [FromBody] KBDocumentRequest request)
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

                if (!string.IsNullOrWhiteSpace(request.Department))
                    document.Department = request.Department;

                if (!string.IsNullOrWhiteSpace(request.Tags))
                    document.Tags = request.Tags;

                document.UpdatedAt = DateTime.Now;

                await _db.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Document updated successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Delete (deactivate) a document
        /// </summary>
        [HttpDelete("documents/{id:int}")]
        public async Task<ActionResult> DeleteDocument(int id)
        {
            try
            {
                var document = await _db.KnowledgeBaseDocuments.FindAsync(id);
                if (document == null)
                    return NotFound();

                document.IsActive = false;
                document.UpdatedAt = DateTime.Now;

                await _db.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Document deleted successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Search knowledge base
        /// </summary>
        [HttpGet("search")]
        public async Task<ActionResult> Search([FromQuery] string query, [FromQuery] int topK = 5)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest("Query is required");
            }

            try
            {
                var kbService = new Services.KnowledgeBaseService(_db);
                var results = await kbService.SearchAsync(query, topK);

                return Ok(results.Select(r => new
                {
                    r.DocumentId,
                    r.Title,
                    r.Content,
                    r.Category,
                    r.Department,
                    r.Score
                }));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
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
                        TokenCount = currentChunk.Split(' ').Length,
                        CreatedAt = DateTime.Now
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
                    TokenCount = currentChunk.Split(' ').Length,
                    CreatedAt = DateTime.Now
                });
            }

            _db.KnowledgeBaseChunks.AddRange(chunks);
            await _db.SaveChangesAsync();

            System.Diagnostics.Debug.WriteLine($"[KB Upload] Created {chunks.Count} chunks, now generating embeddings...");

            // Generate embeddings for each chunk
            try
            {
                // Use Gemini if available, fallback to OpenAI
                IEmbeddingService embeddingService;
                
                // TODO: Get API key from configuration (appsettings.json)
                var hasGeminiKey = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Gemini__ApiKey"));
                
                if (hasGeminiKey)
                {
                    embeddingService = new GeminiEmbeddingService();
                    System.Diagnostics.Debug.WriteLine("[KB Upload] Using Gemini Embeddings");
                }
                else
                {
                    embeddingService = new OpenAIEmbeddingService();
                    System.Diagnostics.Debug.WriteLine("[KB Upload] Using OpenAI Embeddings");
                }
                
                int successCount = 0;

                foreach (var chunk in chunks)
                {
                    try
                    {
                        var embedding = await embeddingService.GenerateEmbeddingAsync(chunk.Content);
                        
                        if (embedding != null && embedding.Count > 0)
                        {
                            chunk.EmbeddingVector = JsonConvert.SerializeObject(embedding);
                            successCount++;
                            System.Diagnostics.Debug.WriteLine($"[KB Upload] ? Chunk {chunk.ChunkIndex}: {embedding.Count}-dim embedding");
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

                await _db.SaveChangesAsync();
                System.Diagnostics.Debug.WriteLine($"[KB Upload] ? Generated embeddings for {successCount}/{chunks.Count} chunks");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[KB Upload] Error in embedding generation: {ex.Message}");
                // Continue even if embedding generation fails - chunks are still created
            }
        }

        /// <summary>
        /// Seed all KB documents to Qdrant vector database
        /// Admin endpoint - seeds all active documents from SQL to Qdrant
        /// </summary>
        [HttpPost("admin/seed-qdrant")]
        public async Task<ActionResult> SeedQdrantKB()
        {
            try
            {
                var seedingService = new QdrantSeedingService();
                var result = await seedingService.SeedAllDocumentsAsync();

                return Ok(new
                {
                    success = result.Failed == 0,
                    totalDocuments = result.TotalDocuments,
                    totalChunks = result.TotalChunks,
                    successfullyIndexed = result.SuccessfullyIndexed,
                    failed = result.Failed,
                    durationSeconds = result.Duration.TotalSeconds,
                    errors = result.Errors,
                    message = result.ToString()
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Seeding error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Reseed all KB documents to Qdrant (delete collection + recreate + seed)
        /// Admin endpoint - WARNING: This deletes the existing Qdrant collection!
        /// </summary>
        [HttpPost("admin/reseed-qdrant")]
        public async Task<ActionResult> ReseedQdrantKB()
        {
            try
            {
                var seedingService = new QdrantSeedingService();
                var result = await seedingService.ReseedAllDocumentsAsync();

                return Ok(new
                {
                    success = result.Failed == 0,
                    totalDocuments = result.TotalDocuments,
                    totalChunks = result.TotalChunks,
                    successfullyIndexed = result.SuccessfullyIndexed,
                    failed = result.Failed,
                    durationSeconds = result.Duration.TotalSeconds,
                    errors = result.Errors,
                    message = result.ToString()
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Reseed error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Seed a specific document to Qdrant
        /// Admin endpoint
        /// </summary>
        [HttpPost("admin/seed-document/{id:int}")]
        public async Task<ActionResult> SeedDocumentQdrant(int id)
        {
            try
            {
                var seedingService = new QdrantSeedingService();
                var success = await seedingService.SeedDocumentAsync(id);

                return Ok(new
                {
                    success = success,
                    documentId = id,
                    message = success ? $"Document {id} seeded to Qdrant" : $"Failed to seed document {id}"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Error seeding document: {ex.Message}" });
            }
        }

        /// <summary>
        /// Check Qdrant health
        /// </summary>
        [HttpGet("admin/qdrant-health")]
        public async Task<ActionResult> CheckQdrantHealth()
        {
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    var qdrantUrl = Environment.GetEnvironmentVariable("Qdrant__Url") ?? "http://localhost:6333";
                    var response = await client.GetAsync($"{qdrantUrl}/health");
                    
                    var isHealthy = response.IsSuccessStatusCode;
                    return Ok(new
                    {
                        healthy = isHealthy,
                        qdrantUrl = qdrantUrl,
                        statusCode = (int)response.StatusCode,
                        message = isHealthy ? "Qdrant is healthy" : "Qdrant is not responding"
                    });
                }
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    healthy = false,
                    message = $"Qdrant health check failed: {ex.Message}"
                });
            }
        }
    }

    /// <summary>
    /// Request model for adding/updating KB documents
    /// </summary>
    public class KBDocumentRequest
    {
        public string Title { get; set; }
        public string Content { get; set; }
        public string Category { get; set; }
        public string Department { get; set; }
        public string Tags { get; set; }
        public string SourceFile { get; set; }
        public string Version { get; set; }
    }
}
