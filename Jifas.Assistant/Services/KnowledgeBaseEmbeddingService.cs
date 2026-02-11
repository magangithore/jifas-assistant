using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// Service for embedding Knowledge Base documents and uploading to database
    /// Handles: Read MD files ? Generate embeddings ? Insert to DB
    /// </summary>
    public interface IKnowledgeBaseEmbeddingService
    {
        Task<bool> ClearOldKnowledgeBaseAsync();
        Task<List<EmbeddingResult>> ProcessAndUploadKnowledgeBaseAsync(string knowledgeBasePath);
        Task<EmbeddingResult> ProcessSingleFileAsync(string filePath);
    }

    /// <summary>
    /// Result of embedding process
    /// </summary>
    public class EmbeddingResult
    {
        public bool Success { get; set; }
        public string FileName { get; set; } = string.Empty;
        public int DocumentId { get; set; }
        public string Message { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
    }

    /// <summary>
    /// Main service implementation for Knowledge Base embedding
    /// Reads MD files, generates embeddings using Gemini, and stores in database
    /// </summary>
    public class KnowledgeBaseEmbeddingService : IKnowledgeBaseEmbeddingService
    {
        private readonly ILoggerService _logger;
        private readonly IConfiguration _configuration;
        private readonly JifasAssistantDbContext _db;
        private readonly IEmbeddingService _embeddingService;

        public KnowledgeBaseEmbeddingService(
            ILoggerService logger,
            IConfiguration configuration,
            JifasAssistantDbContext db,
            IEmbeddingService embeddingService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        }

        /// <summary>
        /// Clear old knowledge base data from database
        /// </summary>
        public async Task<bool> ClearOldKnowledgeBaseAsync()
        {
            try
            {
                _logger.LogInformation("[KnowledgeBaseEmbeddingService] Clearing old knowledge base data");

                // Get count before deletion
                var docCountBefore = await _db.KnowledgeBaseDocuments.CountAsync();
                _logger.LogInformation("[KnowledgeBaseEmbeddingService] Before: {0} documents", docCountBefore);

                // Delete all documents
                _db.KnowledgeBaseDocuments.RemoveRange(_db.KnowledgeBaseDocuments);
                await _db.SaveChangesAsync();

                _logger.LogInformation("[KnowledgeBaseEmbeddingService] Knowledge base cleared successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[KnowledgeBaseEmbeddingService] Error clearing knowledge base: {0}", ex, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Process all MD files in knowledge base directory and upload to database
        /// </summary>
        public async Task<List<EmbeddingResult>> ProcessAndUploadKnowledgeBaseAsync(string knowledgeBasePath)
        {
            var results = new List<EmbeddingResult>();

            try
            {
                if (!Directory.Exists(knowledgeBasePath))
                {
                    _logger.LogInformation("[KnowledgeBaseEmbeddingService] Knowledge base path not found: {0}", knowledgeBasePath);
                    return results;
                }

                _logger.LogInformation("[KnowledgeBaseEmbeddingService] Processing knowledge base at: {0}", knowledgeBasePath);

                // Get all MD files
                var mdFiles = Directory.GetFiles(knowledgeBasePath, "*.md", SearchOption.AllDirectories);
                _logger.LogInformation("[KnowledgeBaseEmbeddingService] Found {0} MD files", mdFiles.Length);

                // Process each file
                foreach (var filePath in mdFiles)
                {
                    var result = await ProcessSingleFileAsync(filePath);
                    results.Add(result);
                }

                _logger.LogInformation("[KnowledgeBaseEmbeddingService] Completed processing {0} files", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError("[KnowledgeBaseEmbeddingService] Error processing knowledge base: {0}", ex, ex.Message);
                return results;
            }
        }

        /// <summary>
        /// Process single MD file: read content, generate embedding, insert to database
        /// </summary>
        public async Task<EmbeddingResult> ProcessSingleFileAsync(string filePath)
        {
            var result = new EmbeddingResult
            {
                FileName = Path.GetFileName(filePath),
                Success = false
            };

            try
            {
                _logger.LogInformation("[KnowledgeBaseEmbeddingService] Processing file: {0}", result.FileName);

                // Validate file exists
                if (!File.Exists(filePath))
                {
                    result.Message = "File not found";
                    return result;
                }

                // Read file content
                var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(content))
                {
                    result.Message = "File is empty";
                    return result;
                }

                _logger.LogInformation("[KnowledgeBaseEmbeddingService] Read {0} characters from file", content.Length);

                // Determine category from file path
                var category = GetCategoryFromPath(filePath);
                var title = Path.GetFileNameWithoutExtension(filePath);

                // Generate embedding for entire document
                var embedding = await _embeddingService.GenerateEmbeddingAsync(content);

                if (embedding == null || embedding.Count == 0)
                {
                    _logger.LogWarning("[KnowledgeBaseEmbeddingService] Failed to generate embedding for document");
                    result.Message = "Failed to generate embedding";
                    return result;
                }

                // Create and save document with embedding
                var document = new KnowledgeBaseDocument
                {
                    Title = title,
                    Content = content,
                    Category = category,
                    Embedding = JsonConvert.SerializeObject(embedding),
                    EmbeddingDimensions = embedding.Count,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    ViewCount = 0,
                    RelevanceScore = 1.0
                };

                _db.KnowledgeBaseDocuments.Add(document);
                await _db.SaveChangesAsync();

                result.DocumentId = document.Id;
                result.Success = true;
                result.Message = $"Successfully processed document with embedding";

                _logger.LogInformation("[KnowledgeBaseEmbeddingService] ? File processed: {0} (Document ID: {1})", result.FileName, result.DocumentId);
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                result.Exception = ex;
                _logger.LogError("[KnowledgeBaseEmbeddingService] Error processing file {0}: {1}", ex, result.FileName, ex.Message);
                return result;
            }
        }

        /// <summary>
        /// Detect category from file path
        /// </summary>
        private string GetCategoryFromPath(string filePath)
        {
            var fileName = Path.GetFileName(filePath).ToLower();

            if (fileName.Contains("master"))
                return "Master Data";

            if (fileName.Contains("guide") || fileName.Contains("user"))
                return "User Guide";

            if (fileName.Contains("invoice"))
                return "Invoice";

            if (fileName.Contains("payment"))
                return "Payment";

            if (fileName.Contains("pum"))
                return "PUM";

            return "General";
        }
    }
}
