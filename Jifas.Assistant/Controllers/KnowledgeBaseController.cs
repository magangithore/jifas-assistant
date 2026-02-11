using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Jifas.Assistant.Data;
using Jifas.Assistant.Data.Models;
using Jifas.Assistant.Services;
using Microsoft.EntityFrameworkCore;

namespace Jifas.Assistant.Controllers
{
    /// <summary>
    /// Admin controller for managing JIFAS Knowledge Base
    /// Handles CRUD operations for KB documents and Qdrant seeding
    /// </summary>
    [Route("api/kb")]
    [ApiController]
    public class KnowledgeBaseController : ControllerBase
    {
        private readonly JifasAssistantDbContext _db;
        private readonly IKnowledgeBaseService _kbService;
        private readonly ILoggerService _logger;
        private readonly HttpClient _httpClient;

        public KnowledgeBaseController(
            JifasAssistantDbContext db,
            IKnowledgeBaseService kbService,
            ILoggerService logger,
            HttpClient httpClient)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _kbService = kbService ?? throw new ArgumentNullException(nameof(kbService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// Get all knowledge base documents
        /// </summary>
        [HttpGet("documents")]
        public async Task<IActionResult> GetDocuments()
        {
            try
            {
                _logger.LogInformation("[KnowledgeBaseController] Fetching all documents");

                var documents = await _db.KnowledgeBaseDocuments
                    .Where(d => d.IsActive == true)
                    .Select(d => new
                    {
                        d.Id,
                        d.Title,
                        d.Category,
                        d.Tags,
                        d.ViewCount,
                        d.CreatedAt,
                        d.UpdatedAt
                    })
                    .ToListAsync();

                return Ok(documents);
            }
            catch (Exception ex)
            {
                _logger.LogError("[KnowledgeBaseController] Error fetching documents: {0}", ex, ex.Message);
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
                _logger.LogInformation("[KnowledgeBaseController] Fetching document {0}", id);

                var document = await _db.KnowledgeBaseDocuments
                    .FirstOrDefaultAsync(d => d.Id == id);

                if (document == null)
                    return NotFound(new { message = $"Document {id} not found" });

                return Ok(new
                {
                    document.Id,
                    document.Title,
                    document.Content,
                    document.Category,
                    document.Tags,
                    document.ViewCount,
                    document.CreatedAt,
                    document.UpdatedAt,
                    document.IsActive
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("[KnowledgeBaseController] Error fetching document: {0}", ex, ex.Message);
                return StatusCode(500, new { error = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Add a new knowledge base document
        /// </summary>
        [HttpPost("documents")]
        public async Task<IActionResult> AddDocument([FromBody] KBDocumentRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Title))
                    return BadRequest(new { error = "Title is required" });

                if (string.IsNullOrWhiteSpace(request.Content))
                    return BadRequest(new { error = "Content is required" });

                _logger.LogInformation("[KnowledgeBaseController] Adding new document: {0}", request.Title);

                var document = new KnowledgeBaseDocument
                {
                    Title = request.Title,
                    Content = request.Content,
                    Category = request.Category ?? "General",
                    Tags = request.Tags,
                    IsActive = true,
                    ViewCount = 0,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    CreatedBy = "admin",
                    UpdatedBy = "admin"
                };

                _db.KnowledgeBaseDocuments.Add(document);
                await _db.SaveChangesAsync();

                _logger.LogInformation("[KnowledgeBaseController] Document added with ID: {0}", document.Id);

                return Ok(new
                {
                    success = true,
                    documentId = document.Id,
                    message = "Document added successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("[KnowledgeBaseController] Error adding document: {0}", ex, ex.Message);
                return StatusCode(500, new { error = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Update an existing document
        /// </summary>
        [HttpPut("documents/{id:int}")]
        public async Task<IActionResult> UpdateDocument(int id, [FromBody] KBDocumentRequest request)
        {
            try
            {
                if (request == null)
                    return BadRequest(new { error = "Request body is required" });

                _logger.LogInformation("[KnowledgeBaseController] Updating document {0}", id);

                var document = await _db.KnowledgeBaseDocuments.FindAsync(id);
                if (document == null)
                    return NotFound(new { message = $"Document {id} not found" });

                if (!string.IsNullOrWhiteSpace(request.Title))
                    document.Title = request.Title;

                if (!string.IsNullOrWhiteSpace(request.Content))
                    document.Content = request.Content;

                if (!string.IsNullOrWhiteSpace(request.Category))
                    document.Category = request.Category;

                if (!string.IsNullOrWhiteSpace(request.Tags))
                    document.Tags = request.Tags;

                document.UpdatedAt = DateTime.UtcNow;
                document.UpdatedBy = "admin";

                await _db.SaveChangesAsync();

                _logger.LogInformation("[KnowledgeBaseController] Document {0} updated successfully", id);

                return Ok(new
                {
                    success = true,
                    message = "Document updated successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("[KnowledgeBaseController] Error updating document: {0}", ex, ex.Message);
                return StatusCode(500, new { error = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Delete (deactivate) a document
        /// </summary>
        [HttpDelete("documents/{id:int}")]
        public async Task<IActionResult> DeleteDocument(int id)
        {
            try
            {
                _logger.LogInformation("[KnowledgeBaseController] Deleting document {0}", id);

                var document = await _db.KnowledgeBaseDocuments.FindAsync(id);
                if (document == null)
                    return NotFound(new { message = $"Document {id} not found" });

                document.IsActive = false;
                document.UpdatedAt = DateTime.UtcNow;
                document.UpdatedBy = "admin";

                await _db.SaveChangesAsync();

                _logger.LogInformation("[KnowledgeBaseController] Document {0} deleted successfully", id);

                return Ok(new
                {
                    success = true,
                    message = "Document deleted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("[KnowledgeBaseController] Error deleting document: {0}", ex, ex.Message);
                return StatusCode(500, new { error = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Search knowledge base
        /// </summary>
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string query, [FromQuery] int topK = 5)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                    return BadRequest(new { error = "Query is required" });

                _logger.LogInformation("[KnowledgeBaseController] Searching KB for: {0}", query);

                var results = await _kbService.SearchAsync(query, topK);

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
                _logger.LogError("[KnowledgeBaseController] Error searching KB: {0}", ex, ex.Message);
                return StatusCode(500, new { error = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Check Qdrant health
        /// </summary>
        [HttpGet("admin/qdrant-health")]
        public async Task<IActionResult> CheckQdrantHealth()
        {
            try
            {
                var qdrantUrl = "http://localhost:6333";
                var response = await _httpClient.GetAsync($"{qdrantUrl}/health");

                var isHealthy = response.IsSuccessStatusCode;
                return Ok(new
                {
                    healthy = isHealthy,
                    qdrantUrl = qdrantUrl,
                    statusCode = (int)response.StatusCode,
                    message = isHealthy ? "Qdrant is healthy" : "Qdrant is not responding"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("[KnowledgeBaseController] Qdrant health check failed: {0}", ex, ex.Message);
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
        public string Tags { get; set; }
    }
}
