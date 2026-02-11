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
    /// Result of KB seeding operation
    /// </summary>
    public class KBSeedingResult
    {
        public bool Success { get; set; }
        public string FileName { get; set; }
        public int DocumentId { get; set; }
        public string Message { get; set; }
        public Exception Exception { get; set; }
    }

    /// <summary>
    /// Interface for KB seeding operations
    /// </summary>
    public interface IKBSeedingService
    {
        Task<List<KBSeedingResult>> SeedKnowledgeBaseAsync(string kbFolderPath);
        Task<KBSeedingResult> SeedDocumentAsync(string filePath);
        Task<bool> ClearKnowledgeBaseAsync();
    }

    /// <summary>
    /// Simple service untuk seed knowledge base documents dari MD files ke database
    /// 
    /// FLOW:
    /// 1. Read MD file dari knowledge-base/ folder
    /// 2. Generate embedding using GeminiEmbeddingService
    /// 3. Simpan document + embedding ke KnowledgeBaseDocuments table (SQL Server)
    /// 4. Optional: Push ke Qdrant untuk vector similarity search
    ///
    /// STORAGE:
    /// ? SQL Server: Full content + embedding (JSON)
    /// ? Qdrant: Vector embeddings only (for fast search)
    /// </summary>
    public class KBSeedingService : IKBSeedingService
    {
        private readonly ILoggerService _logger;
        private readonly IConfiguration _configuration;
        private readonly JifasAssistantDbContext _db;
        private readonly IEmbeddingService _embeddingService;

        public KBSeedingService(
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
        /// Seed all MD files dari knowledge-base folder
        /// </summary>
        public async Task<List<KBSeedingResult>> SeedKnowledgeBaseAsync(string kbFolderPath)
        {
            var results = new List<KBSeedingResult>();

            try
            {
                if (string.IsNullOrWhiteSpace(kbFolderPath))
                {
                    kbFolderPath = _configuration["KnowledgeBase:FolderPath"] ?? "./knowledge-base";
                }

                if (!Directory.Exists(kbFolderPath))
                {
                    _logger.LogWarning("[KBSeedingService] Knowledge base folder not found: {0}", kbFolderPath);
                    return results;
                }

                _logger.LogInformation("[KBSeedingService] Starting KB seeding from: {0}", kbFolderPath);

                var mdFiles = Directory.GetFiles(kbFolderPath, "*.md", SearchOption.AllDirectories);
                _logger.LogInformation("[KBSeedingService] Found {0} MD files", mdFiles.Length);

                foreach (var filePath in mdFiles)
                {
                    var result = await SeedDocumentAsync(filePath);
                    results.Add(result);

                    if (result.Success)
                    {
                        _logger.LogInformation("[KBSeedingService] ? {0}", result.FileName);
                    }
                    else
                    {
                        _logger.LogWarning("[KBSeedingService] ?? {0}: {1}", result.FileName, result.Message);
                    }
                }

                _logger.LogInformation("[KBSeedingService] Done. Success: {0}/{1}",
                    results.Count(r => r.Success), results.Count);

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError("[KBSeedingService] Error: {0}", ex, ex.Message);
                return results;
            }
        }

        /// <summary>
        /// Seed single MD document
        /// 1. Read file -> 2. Generate embedding -> 3. Save to DB
        /// </summary>
        public async Task<KBSeedingResult> SeedDocumentAsync(string filePath)
        {
            var result = new KBSeedingResult
            {
                FileName = Path.GetFileName(filePath),
                Success = false
            };

            try
            {
                if (!File.Exists(filePath))
                {
                    result.Message = "File not found";
                    return result;
                }

                var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(content))
                {
                    result.Message = "Empty file";
                    return result;
                }

                var title = Path.GetFileNameWithoutExtension(filePath);
                var category = GetCategory(filePath);

                _logger.LogInformation("[KBSeedingService] Processing {0}...", title);

                // Generate embedding using GeminiEmbeddingService
                var embedding = await _embeddingService.GenerateEmbeddingAsync(content);

                if (embedding == null || embedding.Count == 0)
                {
                    result.Message = "Embedding failed";
                    _logger.LogWarning("[KBSeedingService] Cannot generate embedding for {0}", title);
                    return result;
                }

                // Create & save document to KnowledgeBaseDocuments table
                var document = new KnowledgeBaseDocument
                {
                    Title = title,
                    Content = content,
                    Category = category,
                    Tags = GetTags(title),
                    Embedding = JsonConvert.SerializeObject(embedding),
                    EmbeddingDimensions = embedding.Count,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    ViewCount = 0,
                    RelevanceScore = 1.0,
                    CreatedBy = "System",
                    UpdatedBy = "System"
                };

                _db.KnowledgeBaseDocuments.Add(document);
                await _db.SaveChangesAsync();

                result.DocumentId = document.Id;
                result.Success = true;
                result.Message = "Saved to database";

                _logger.LogInformation("[KBSeedingService] ? {0} (ID: {1}, Embedding: {2} dims)",
                    title, document.Id, embedding.Count);

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                result.Exception = ex;
                _logger.LogError("[KBSeedingService] Error processing {0}: {1}", ex, result.FileName, ex.Message);
                return result;
            }
        }

        /// <summary>
        /// Clear all KB documents
        /// </summary>
        public async Task<bool> ClearKnowledgeBaseAsync()
        {
            try
            {
                _logger.LogWarning("[KBSeedingService] Clearing KB...");
                var count = await _db.KnowledgeBaseDocuments.CountAsync();
                _db.KnowledgeBaseDocuments.RemoveRange(_db.KnowledgeBaseDocuments);
                await _db.SaveChangesAsync();
                _logger.LogInformation("[KBSeedingService] Cleared {0} documents", count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[KBSeedingService] Clear error: {0}", ex, ex.Message);
                return false;
            }
        }

        private string GetCategory(string filePath)
        {
            var f = Path.GetFileName(filePath).ToLower();
            if (f.Contains("master")) return "Master Data";
            if (f.Contains("guide") || f.Contains("user")) return "User Guide";
            if (f.Contains("invoice")) return "Invoice";
            if (f.Contains("payment")) return "Payment";
            if (f.Contains("report")) return "Reports";
            if (f.Contains("troubleshoot") || f.Contains("faq")) return "Troubleshooting";
            return "General";
        }

        private string GetTags(string fileName)
        {
            var tags = new List<string> { "jifas", "kb" };
            var f = fileName.ToLower();
            if (f.Contains("invoice")) tags.Add("invoice");
            if (f.Contains("payment")) tags.Add("payment");
            if (f.Contains("guide")) tags.Add("guide");
            return string.Join(",", tags);
        }
    }
}
