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
    }

    /// <summary>
    /// Interface for KB seeding operations
    /// </summary>
    public interface IKBSeedingService
    {
        Task<List<KBSeedingResult>> SeedKnowledgeBaseAsync(string kbFolderPath = null);
        Task<KBSeedingResult> SeedDocumentAsync(string filePath);
        Task<bool> ClearKnowledgeBaseAsync();
    }

    /// <summary>
    /// SIMPLIFIED KB Seeding Service
    /// 
    /// FLOW:
    /// 1. Read .txt/.md files dari knowledge-base/ folder (recursive)
    /// 2. Generate embedding using GeminiEmbeddingService
    /// 3. Simpan document + embedding ke KnowledgeBaseDocuments table
    /// 4. Auto-detect category dari folder name (master/ ? "Master Data", etc)
    ///
    /// STORAGE:
    /// ? SQL Server ONLY: Full content + embedding (JSON)
    /// ? NO Qdrant: Removed - keep it simple!
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
        /// Seed all .txt/.md files dari knowledge-base folder
        /// </summary>
        public async Task<List<KBSeedingResult>> SeedKnowledgeBaseAsync(string kbFolderPath = null)
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
                    _logger.LogWarning("[KB] Folder not found: {0}", kbFolderPath);
                    return results;
                }

                _logger.LogInformation("[KB] Starting seeding from: {0}", kbFolderPath);

                // Get both .txt AND .md files (recursive)
                var allFiles = Directory.GetFiles(kbFolderPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".txt") || f.EndsWith(".md"))
                    .ToArray();

                _logger.LogInformation("[KB] Found {0} files to seed", allFiles.Length);

                // Process each file
                foreach (var filePath in allFiles)
                {
                    var result = await SeedDocumentAsync(filePath);
                    results.Add(result);

                    if (result.Success)
                    {
                        _logger.LogInformation("[KB] ? {0}", result.FileName);
                    }
                    else
                    {
                        _logger.LogWarning("[KB] ? {0}: {1}", result.FileName, result.Message);
                    }
                }

                var successCount = results.Count(r => r.Success);
                _logger.LogInformation("[KB] Done: {0}/{1} success", successCount, results.Count);

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError("[KB] Seeding error: {0}", ex, ex.Message);
                return results;
            }
        }

        /// <summary>
        /// Seed single document: Read ? Embed ? Save
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

                // Read file content
                var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(content))
                {
                    result.Message = "Empty file";
                    return result;
                }

                var title = Path.GetFileNameWithoutExtension(filePath);
                var category = GetCategoryFromPath(filePath);
                var tags = GetTags(title, category);

                _logger.LogInformation("[KB] Processing: {0} ({1})...", title, category);

                // Generate embedding using Gemini
                var embedding = await _embeddingService.GenerateEmbeddingAsync(content);
                if (embedding == null || embedding.Count == 0)
                {
                    result.Message = "Embedding generation failed";
                    return result;
                }

                // Check if document already exists
                var existingDoc = await _db.KnowledgeBaseDocuments
                    .FirstOrDefaultAsync(x => x.Title == title && x.Category == category);

                if (existingDoc != null)
                {
                    // Update existing
                    existingDoc.Content = content;
                    existingDoc.Embedding = JsonConvert.SerializeObject(embedding);
                    existingDoc.EmbeddingDimensions = embedding.Count;
                    existingDoc.Tags = tags;
                    existingDoc.UpdatedAt = DateTime.UtcNow;
                    existingDoc.UpdatedBy = "System";

                    _db.KnowledgeBaseDocuments.Update(existingDoc);
                    result.DocumentId = existingDoc.Id;
                    result.Message = "Updated";
                }
                else
                {
                    // Create new
                    var document = new KnowledgeBaseDocument
                    {
                        Title = title,
                        Content = content,
                        Category = category,
                        Tags = tags,
                        Embedding = JsonConvert.SerializeObject(embedding),
                        EmbeddingDimensions = embedding.Count,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        CreatedBy = "System",
                        UpdatedBy = "System"
                    };

                    _db.KnowledgeBaseDocuments.Add(document);
                    result.Message = "Created";
                }

                // Save to database
                await _db.SaveChangesAsync();
                result.Success = true;

                _logger.LogInformation("[KB] ? {0} ({1} dims, {2})", title, embedding.Count, result.Message);

                return result;
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
                _logger.LogError("[KB] Error: {0}", ex, ex.Message);
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
                _logger.LogWarning("[KB] Clearing all documents...");
                var count = _db.KnowledgeBaseDocuments.Count();
                _db.KnowledgeBaseDocuments.RemoveRange(_db.KnowledgeBaseDocuments);
                await _db.SaveChangesAsync();
                _logger.LogInformation("[KB] Cleared {0} documents", count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[KB] Clear error: {0}", ex, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Extract category from folder path
        /// Example: C:\kb\master\file.txt ? "Master Data"
        /// </summary>
        private string GetCategoryFromPath(string filePath)
        {
            var folderName = Path.GetDirectoryName(filePath)
                ?.Split(Path.DirectorySeparatorChar)
                .LastOrDefault()
                ?.ToLower() ?? "";

            // Map folder names to categories
            return folderName switch
            {
                var f when f.Contains("master") => "Master Data",
                var f when f.Contains("pum") => "PUM",
                var f when f.Contains("invoice") => "Invoice",
                var f when f.Contains("payment") => "Payment",
                var f when f.Contains("guide") => "User Guide",
                var f when f.Contains("report") => "Reports",
                var f when f.Contains("faq") || f.Contains("troubleshoot") => "Troubleshooting",
                _ => "General"
            };
        }

        /// <summary>
        /// Generate tags from title and category
        /// </summary>
        private string GetTags(string title, string category)
        {
            var tags = new List<string> { "jifas", "kb" };

            // Add category as tag (lowercase, replace spaces)
            tags.Add(category.ToLower().Replace(" ", "_"));

            // Add tags from title
            var titleLower = title.ToLower();
            if (titleLower.Contains("invoice")) tags.Add("invoice");
            if (titleLower.Contains("payment")) tags.Add("payment");
            if (titleLower.Contains("guide")) tags.Add("guide");
            if (titleLower.Contains("master")) tags.Add("master");

            return string.Join(",", tags.Distinct());
        }
    }
}
