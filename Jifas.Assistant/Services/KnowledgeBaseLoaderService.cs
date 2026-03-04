using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using jifas_assistant.DAL.Models;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Service untuk loading Knowledge Base files dari folder, chunking, embedding, dan insert ke SQL Server
    /// </summary>
    public interface IKnowledgeBaseLoaderService
    {
        Task<int> LoadAllKnowledgeBasesAsync(string kbFolderPath);
        Task<List<KnowledgeBaseChunk>> ChunkDocumentAsync(string filePath, string content, string category);
    }

    public class KnowledgeBaseLoaderService : IKnowledgeBaseLoaderService
    {
        private readonly JIFAS_AssistantContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<KnowledgeBaseLoaderService> _logger;
        private readonly IEmbeddingService _embeddingService;

        public KnowledgeBaseLoaderService(
            JIFAS_AssistantContext context,
            IConfiguration configuration,
            ILogger<KnowledgeBaseLoaderService> logger,
            IEmbeddingService embeddingService = null)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
            _embeddingService = embeddingService;
        }

        /// <summary>
        /// Load semua file dari Knowledge Base folder secara recursive
        /// </summary>
        public async Task<int> LoadAllKnowledgeBasesAsync(string kbFolderPath)
        {
            try
            {
                if (!Directory.Exists(kbFolderPath))
                {
                    _logger.LogError($"Knowledge Base folder tidak ditemukan: {kbFolderPath}");
                    return 0;
                }

                var allFiles = Directory.GetFiles(kbFolderPath, "*.txt", SearchOption.AllDirectories);
                _logger.LogInformation($"Ditemukan {allFiles.Length} file Knowledge Base");

                int totalChunksInserted = 0;
                int fileCount = 0;

                foreach (var filePath in allFiles)
                {
                    fileCount++;
                    try
                    {
                        var content = File.ReadAllText(filePath, Encoding.UTF8);
                        
                        // Extract category dari path (folder name)
                        var category = ExtractCategoryFromPath(filePath, kbFolderPath);
                        
                        _logger.LogInformation($"[{fileCount}/{allFiles.Length}] Processing: {filePath}");

                        // Chunk the document
                        var chunks = await ChunkDocumentAsync(filePath, content, category);
                        _logger.LogInformation($"  ? Chunked into {chunks.Count} chunks");

                        // Insert chunks
                        foreach (var chunk in chunks)
                        {
                            try
                            {
                                // Convert ke model database
                                var dbChunk = new KnowledgeBaseDocuments
                                {
                                    Title = chunk.Title,
                                    Content = chunk.Content,
                                    Category = chunk.Category,
                                    Tags = chunk.Tags,
                                    FilePath = chunk.FilePath,
                                    Embedding = chunk.EmbeddingBase64,
                                    EmbeddingDimensions = chunk.EmbeddingDimensions,
                                    IsActive = chunk.IsActive,
                                    CreatedAt = chunk.CreatedAt,
                                    UpdatedAt = chunk.UpdatedAt,
                                    ViewCount = chunk.ViewCount,
                                    RelevanceScore = chunk.RelevanceScore,
                                    CreatedBy = chunk.CreatedBy
                                };

                                _context.KnowledgeBaseDocuments.Add(dbChunk);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"  ? Error processing chunk: {ex.Message}");
                                continue;
                            }
                        }

                        await _context.SaveChangesAsync();
                        totalChunksInserted += chunks.Count;
                        _logger.LogInformation($"  ? Inserted {chunks.Count} chunks");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"  ? Error processing file {filePath}: {ex.Message}");
                        continue;
                    }
                }

                _logger.LogInformation($"\n? Knowledge Base Loading Complete!");
                _logger.LogInformation($"   Total Files: {fileCount}");
                _logger.LogInformation($"   Total Chunks Inserted: {totalChunksInserted}");

                return totalChunksInserted;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading knowledge base: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Chunk document berdasarkan paragraph (recommended strategy)
        /// </summary>
        public async Task<List<KnowledgeBaseChunk>> ChunkDocumentAsync(
            string filePath, 
            string content, 
            string category)
        {
            var chunks = new List<KnowledgeBaseChunk>();

            try
            {
                // Get file info
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                
                // Parse title dari file (first heading atau filename)
                var title = ExtractTitle(content, fileName);

                // Split ke paragraf (double newline atau section headers)
                var paragraphs = SplitIntoParagraphs(content);
                _logger.LogDebug($"  Document '{fileName}' split into {paragraphs.Count} paragraphs");

                int chunkSequence = 1;

                foreach (var paragraph in paragraphs)
                {
                    // Skip empty paragraphs
                    if (string.IsNullOrWhiteSpace(paragraph))
                        continue;

                    var cleanContent = CleanContent(paragraph);
                    
                    // Skip jika chunk terlalu pendek (< 50 characters)
                    if (cleanContent.Length < 50)
                        continue;

                    // Generate embedding jika service tersedia
                    string embeddingBase64 = null;
                    
                    if (_embeddingService != null)
                    {
                        try
                        {
                            var embedding = await _embeddingService.GenerateEmbeddingAsync(cleanContent);
                            if (embedding != null)
                            {
                                embeddingBase64 = Convert.ToBase64String(embedding);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"    Warning: Failed to generate embedding for chunk: {ex.Message}");
                        }
                    }

                    var chunk = new KnowledgeBaseChunk
                    {
                        Title = $"{title} - Part {chunkSequence}",
                        Content = cleanContent,
                        Category = category,
                        Tags = ExtractTags(category, paragraph),
                        FilePath = filePath,
                        EmbeddingBase64 = embeddingBase64,
                        EmbeddingDimensions = _configuration.GetValue<int>("Embedding:Dimensions"),
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        ViewCount = 0,
                        RelevanceScore = 0.0,
                        CreatedBy = "KBLoader",
                        UpdatedBy = "KBLoader"
                    };

                    chunks.Add(chunk);
                    chunkSequence++;
                }

                return chunks;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error chunking document {filePath}: {ex.Message}");
                throw;
            }
        }

        private string ExtractCategoryFromPath(string filePath, string basePath)
        {
            try
            {
                var relativePath = filePath.Replace(basePath, "").TrimStart('\\', '/');
                var parts = relativePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                
                if (parts.Length > 1)
                {
                    return parts[parts.Length - 2];
                }
                
                return "General";
            }
            catch
            {
                return "General";
            }
        }

        private string ExtractTitle(string content, string fileName)
        {
            try
            {
                var lines = content.Split('\n');
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("=") || trimmed.StartsWith("#"))
                    {
                        return Regex.Replace(trimmed, @"^[=#]+\s*|\s*[=#]+$", "").Trim();
                    }
                    if (trimmed.Length > 20 && !trimmed.StartsWith("-"))
                    {
                        return trimmed;
                    }
                }

                return fileName;
            }
            catch
            {
                return fileName;
            }
        }

        private List<string> SplitIntoParagraphs(string content)
        {
            var paragraphs = new List<string>();

            try
            {
                content = Regex.Replace(content, @"\r\n", "\n");
                content = Regex.Replace(content, @"\n\s*\n\s*\n", "\n\n");

                var lines = content.Split(new[] { "\n\n" }, StringSplitOptions.None);

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    
                    if (Regex.IsMatch(trimmed, @"^[=\-*]{5,}$"))
                        continue;

                    if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        paragraphs.Add(trimmed);
                    }
                }

                return paragraphs;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Warning splitting paragraphs: {ex.Message}");
                return content.Split(new[] { ". " }, StringSplitOptions.None)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            }
        }

        private string CleanContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return content;

            content = content.Trim();
            content = Regex.Replace(content, @"\s+", " ");
            content = Regex.Replace(content, @"\n\s*\n", "\n");
            content = Regex.Replace(content, @"\n", " ");

            return content;
        }

        private string ExtractTags(string category, string content)
        {
            var tags = new List<string> { category };

            try
            {
                var jifasTerms = new[] { 
                    "INVOICE", "PUM", "PAYMENT", "RECEIVING", "BUDGET", "COA", 
                    "APPROVAL", "VENDOR", "DEPARTMENT", "DIVISI", "COMPANY",
                    "POSTING", "JURNAL", "LAPORAN", "CASH", "BANK", "PAJAK",
                    "PPN", "PPH", "TRANSFER", "CEK", "KONSOLIDASI", "MASTER", "REPORT"
                };

                var contentUpper = content.ToUpper();
                foreach (var term in jifasTerms)
                {
                    if (contentUpper.Contains(term))
                    {
                        tags.Add(term);
                    }
                }

                tags = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch
            {
                // Silent fail
            }

            return string.Join(";", tags);
        }
    }

    public class KnowledgeBaseChunk
    {
        public string Title { get; set; }
        public string Content { get; set; }
        public string Category { get; set; }
        public string Tags { get; set; }
        public string FilePath { get; set; }
        public string EmbeddingBase64 { get; set; }
        public int EmbeddingDimensions { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int ViewCount { get; set; }
        public double RelevanceScore { get; set; }
        public string CreatedBy { get; set; }
        public string UpdatedBy { get; set; }
    }
}
