using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Jifas.Chatbot.DAL;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Service untuk embedding Knowledge Base documents dan upload ke database
    /// Handles: Read MD files ? Chunk content ? Generate embeddings ? Insert to DB
    /// </summary>
    public interface IKnowledgeBaseEmbeddingService
    {
        Task<bool> ClearOldKnowledgeBaseAsync();
        Task<List<EmbeddingResult>> ProcessAndUploadKnowledgeBaseAsync(string knowledgeBasePath);
        Task<EmbeddingResult> ProcessSingleFileAsync(string filePath);
    }

    /// <summary>
    /// Result dari embedding process
    /// </summary>
    public class EmbeddingResult
    {
        public bool Success { get; set; }
        public string FileName { get; set; }
        public int DocumentId { get; set; }
        public int ChunksInserted { get; set; }
        public string Message { get; set; }
        public Exception Exception { get; set; }
    }

    /// <summary>
    /// Chunk of text untuk embedding
    /// </summary>
    public class TextChunk
    {
        public int Index { get; set; }
        public string Text { get; set; }
        public int StartPosition { get; set; }
        public int EndPosition { get; set; }
    }

    /// <summary>
    /// Main service implementation
    /// </summary>
    public class KnowledgeBaseEmbeddingService : IKnowledgeBaseEmbeddingService
    {
        private readonly JIFAS_AssistantEntities _db;
        private readonly ILoggerService _logger;
        private readonly string _geminiApiKey;
        private readonly string _geminiBaseUrl;
        private readonly int _chunkSize;
        private readonly int _chunkOverlap;
        private readonly HttpClient _httpClient;

        // Configuration
        private const string GEMINI_EMBEDDING_URL = "https://generativelanguage.googleapis.com/v1beta/models/embedding-001:embedContent?key=";
        private const int DEFAULT_CHUNK_SIZE = 500;
        private const int DEFAULT_CHUNK_OVERLAP = 50;

        public KnowledgeBaseEmbeddingService()
        {
            _db = new JIFAS_AssistantEntities();
            _logger = LoggerFactory.GetLogger();
            _geminiApiKey = System.Configuration.ConfigurationManager.AppSettings["Gemini:ApiKey"] ?? "";
            _geminiBaseUrl = System.Configuration.ConfigurationManager.AppSettings["Gemini:BaseUrl"] ?? "";
            _chunkSize = int.TryParse(System.Configuration.ConfigurationManager.AppSettings["KnowledgeBase:ChunkSize"] ?? "", out var size) ? size : DEFAULT_CHUNK_SIZE;
            _chunkOverlap = int.TryParse(System.Configuration.ConfigurationManager.AppSettings["KnowledgeBase:ChunkOverlap"] ?? "", out var overlap) ? overlap : DEFAULT_CHUNK_OVERLAP;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        /// <summary>
        /// Clear old knowledge base data from database
        /// </summary>
        public async Task<bool> ClearOldKnowledgeBaseAsync()
        {
            try
            {
                _logger.LogInformation("[KnowledgeBaseEmbeddingService] Clearing old knowledge base data...");

                // Get count before deletion
                var docCountBefore = _db.KnowledgeBaseDocuments.Count();
                var chunkCountBefore = _db.KnowledgeBaseChunks.Count();

                _logger.LogInformation($"[KnowledgeBaseEmbeddingService] Before: {docCountBefore} documents, {chunkCountBefore} chunks");

                // Delete chunks first (foreign key constraint)
                _db.KnowledgeBaseChunks.RemoveRange(_db.KnowledgeBaseChunks);
                await _db.SaveChangesAsync();
                _logger.LogInformation("[KnowledgeBaseEmbeddingService] Deleted all chunks");

                // Delete documents
                _db.KnowledgeBaseDocuments.RemoveRange(_db.KnowledgeBaseDocuments);
                await _db.SaveChangesAsync();
                _logger.LogInformation("[KnowledgeBaseEmbeddingService] Deleted all documents");

                _logger.LogInformation("[KnowledgeBaseEmbeddingService] Knowledge base cleared successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[KnowledgeBaseEmbeddingService] Error clearing knowledge base: {ex.Message}");
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
                    _logger.LogError($"[KnowledgeBaseEmbeddingService] Knowledge base path not found: {knowledgeBasePath}");
                    return results;
                }

                _logger.LogInformation($"[KnowledgeBaseEmbeddingService] Processing knowledge base at: {knowledgeBasePath}");

                // Get all MD files
                var mdFiles = Directory.GetFiles(knowledgeBasePath, "*.md", SearchOption.AllDirectories);
                _logger.LogInformation($"[KnowledgeBaseEmbeddingService] Found {mdFiles.Length} MD files");

                // Process each file
                foreach (var filePath in mdFiles)
                {
                    var result = await ProcessSingleFileAsync(filePath);
                    results.Add(result);
                }

                _logger.LogInformation($"[KnowledgeBaseEmbeddingService] Completed processing {results.Count} files");
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[KnowledgeBaseEmbeddingService] Error processing knowledge base: {ex.Message}");
                return results;
            }
        }

        /// <summary>
        /// Process single MD file
        /// </summary>
        public async Task<EmbeddingResult> ProcessSingleFileAsync(string filePath)
        {
            var result = new EmbeddingResult
            {
                FileName = Path.GetFileName(filePath),
                Success = false,
                ChunksInserted = 0
            };

            try
            {
                _logger.LogInformation($"[KnowledgeBaseEmbeddingService] Processing file: {result.FileName}");

                // Read file content
                if (!File.Exists(filePath))
                {
                    result.Message = "File not found";
                    return result;
                }

                // .NET Framework 4.8 doesn't have ReadAllTextAsync, use synchronous version
                var content = File.ReadAllText(filePath, Encoding.UTF8);
                _logger.LogInformation($"[KnowledgeBaseEmbeddingService] Read {content.Length} characters");

                // Determine module and category
                var module = GetModuleFromPath(filePath);
                var category = GetCategoryFromPath(filePath);

                // Insert document metadata
                // Map to actual entity properties: Id, Title, Content, Category, SourceFile, Version, CreatedAt, UpdatedAt
                var document = new KnowledgeBaseDocuments
                {
                    Title = Path.GetFileNameWithoutExtension(filePath),
                    Content = content,
                    Category = category,
                    Department = module,
                    SourceFile = filePath,
                    Version = "1",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    IsActive = true
                };

                _db.KnowledgeBaseDocuments.Add(document);
                await _db.SaveChangesAsync();
                result.DocumentId = document.Id;


                _logger.LogInformation($"[KnowledgeBaseEmbeddingService] Inserted document metadata (ID: {document.Id})");

                // Split content into chunks
                var chunks = SplitIntoChunks(content);
                _logger.LogInformation($"[KnowledgeBaseEmbeddingService] Split into {chunks.Count} chunks");

                // Generate embeddings and insert chunks
                var insertedChunks = 0;
                foreach (var chunk in chunks)
                {
                    try
                    {
                        // Generate embedding
                        var embedding = await GenerateEmbeddingAsync(chunk.Text);

                        if (embedding == null || embedding.Length == 0)
                        {
                            _logger.LogWarning($"[KnowledgeBaseEmbeddingService] No embedding generated for chunk {chunk.Index}");
                            continue;
                        }

                        // Create chunk record
                        // Map to actual entity properties: Id, DocumentId, ChunkIndex, Content, EmbeddingVector, TokenCount, CreatedAt
                        var dbChunk = new KnowledgeBaseChunks
                        {
                            DocumentId = document.Id,
                            Content = chunk.Text,
                            EmbeddingVector = JsonConvert.SerializeObject(embedding),
                            ChunkIndex = chunk.Index,
                            TokenCount = chunk.Text.Split(' ').Length,
                            CreatedAt = DateTime.Now
                        };

                        _db.KnowledgeBaseChunks.Add(dbChunk);
                        insertedChunks++;

                        // Save every 5 chunks to avoid memory issues
                        if (insertedChunks % 5 == 0)
                        {
                            await _db.SaveChangesAsync();
                            _logger.LogInformation($"[KnowledgeBaseEmbeddingService] Saved {insertedChunks} chunks");
                        }

                        // Rate limiting
                        await Task.Delay(100);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[KnowledgeBaseEmbeddingService] Error processing chunk {chunk.Index}: {ex.Message}");
                    }
                }

                // Save remaining chunks
                if (insertedChunks % 5 != 0)
                {
                    await _db.SaveChangesAsync();
                }

                result.ChunksInserted = insertedChunks;
                result.Success = true;
                result.Message = $"Successfully processed {insertedChunks} chunks";

                _logger.LogInformation($"[KnowledgeBaseEmbeddingService] ? File processed: {result.FileName} ({result.ChunksInserted} chunks)");
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                result.Exception = ex;
                _logger.LogError($"[KnowledgeBaseEmbeddingService] Error processing file {result.FileName}: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// Generate embedding for text using Gemini API
        /// </summary>
        private async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(_geminiApiKey))
                {
                    return null;
                }

                // Prepare request
                var requestBody = new
                {
                    content = new
                    {
                        parts = new[] { new { text = text } }
                    }
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Call API
                var url = $"{GEMINI_EMBEDDING_URL}{_geminiApiKey}";
                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning($"[KnowledgeBaseEmbeddingService] Embedding API error: {response.StatusCode} - {errorContent}");
                    return null;
                }

                // Parse response
                var responseContent = await response.Content.ReadAsStringAsync();
                var responseObject = JObject.Parse(responseContent);

                var embeddingValues = responseObject["embedding"]?["values"];
                if (embeddingValues == null)
                {
                    return null;
                }

                // Convert to float array
                var embedding = embeddingValues.ToObject<float[]>();
                return embedding;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[KnowledgeBaseEmbeddingService] Error generating embedding: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Split content into overlapping chunks
        /// </summary>
        private List<TextChunk> SplitIntoChunks(string content)
        {
            var chunks = new List<TextChunk>();

            if (string.IsNullOrWhiteSpace(content))
            {
                return chunks;
            }

            int startIndex = 0;
            int chunkIndex = 0;

            while (startIndex < content.Length)
            {
                int endIndex = Math.Min(startIndex + _chunkSize, content.Length);
                string chunkText = content.Substring(startIndex, endIndex - startIndex).Trim();

                if (chunkText.Length > 0)
                {
                    chunks.Add(new TextChunk
                    {
                        Index = chunkIndex,
                        Text = chunkText,
                        StartPosition = startIndex,
                        EndPosition = endIndex
                    });

                    chunkIndex++;
                }

                // Move to next position with overlap
                startIndex += (_chunkSize - _chunkOverlap);
            }

            return chunks;
        }

        /// <summary>
        /// Detect module from file path
        /// </summary>
        private string GetModuleFromPath(string filePath)
        {
            var fileName = Path.GetFileName(filePath).ToLower();

            if (fileName.Contains("userguidehalf") || fileName.Contains("jifas.md"))
                return "General";
            
            if (filePath.Contains("Master"))
                return "Master Data";

            return "General";
        }

        /// <summary>
        /// Detect category from file path
        /// </summary>
        private string GetCategoryFromPath(string filePath)
        {
            if (filePath.Contains("Master"))
                return "Master Data";
            
            if (filePath.Contains("userguidehalf"))
                return "User Guide";

            return "General";
        }
    }
}
