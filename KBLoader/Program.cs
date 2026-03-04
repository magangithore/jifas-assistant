using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using jifas_assistant.DAL.Models;

namespace Jifas.Assistant.KBLoader
{
    /// <summary>
    /// Console application untuk load Knowledge Base langsung ke SQL Server
    /// Jalankan: dotnet run --project KBLoader/KBLoader.csproj
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("??????????????????????????????????????????????????????");
            Console.WriteLine("?   JIFAS Knowledge Base Loader - Direct DB Insert   ?");
            Console.WriteLine("??????????????????????????????????????????????????????");
            Console.WriteLine();

            try
            {
                // Setup DI
                var services = new ServiceCollection();
                
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json")
                    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"}.json", optional: true)
                    .Build();

                services.AddSingleton(configuration);
                services.AddLogging(builder => 
                {
                    builder.AddConsole();
                    builder.AddDebug();
                    builder.SetMinimumLevel(LogLevel.Information);
                });

                services.AddDbContext<JIFAS_AssistantContext>(options =>
                {
                    var connectionString = configuration["ConnectionStrings:DefaultConnection"];
                    options.UseSqlServer(connectionString);
                });

                services.AddScoped<IEmbeddingService, OllamaEmbeddingService>();
                services.AddHttpClient();

                var serviceProvider = services.BuildServiceProvider();
                
                // Get services
                var context = serviceProvider.GetRequiredService<JIFAS_AssistantContext>();
                var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
                var embeddingService = serviceProvider.GetRequiredService<IEmbeddingService>();

                logger.LogInformation("Starting Knowledge Base loading process...");
                logger.LogInformation("");

                // Determine KB folder path
                var kbFolderPath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "..",
                    "..",
                    "Jifas.Assistant",
                    "KnowledgeBase"
                );

                // Normalize path
                kbFolderPath = Path.GetFullPath(kbFolderPath);
                
                logger.LogInformation($"KB Folder Path: {kbFolderPath}");
                logger.LogInformation("");

                // Check folder exists
                if (!Directory.Exists(kbFolderPath))
                {
                    logger.LogError($"Knowledge Base folder not found: {kbFolderPath}");
                    return;
                }

                // Get all files
                var allFiles = Directory.GetFiles(kbFolderPath, "*.txt", SearchOption.AllDirectories);
                logger.LogInformation($"Found {allFiles.Length} KB files");
                logger.LogInformation("");

                // Confirm clear
                logger.LogWarning("??  Existing Knowledge Base will be cleared before loading new data");
                Console.Write("Continue? (Y/N): ");
                var response = Console.ReadLine();
                
                if (response?.ToUpper() != "Y")
                {
                    logger.LogInformation("Operation cancelled by user");
                    return;
                }

                logger.LogInformation("");
                logger.LogInformation("Clearing existing Knowledge Base documents...");
                
                // Clear existing
                var existingDocs = await context.KnowledgeBaseDocuments.ToListAsync();
                if (existingDocs.Any())
                {
                    logger.LogInformation($"Deleting {existingDocs.Count} existing documents...");
                    context.KnowledgeBaseDocuments.RemoveRange(existingDocs);
                    await context.SaveChangesAsync();
                    logger.LogInformation("? Existing documents cleared");
                }

                logger.LogInformation("");
                logger.LogInformation("???????????????????????????????????????????????????");
                logger.LogInformation("Starting file processing...");
                logger.LogInformation("???????????????????????????????????????????????????");
                logger.LogInformation("");

                int fileCount = 0;
                int totalChunksInserted = 0;
                int totalEmbeddingsGenerated = 0;
                int totalEmbeddingErrors = 0;

                foreach (var filePath in allFiles)
                {
                    fileCount++;
                    try
                    {
                        var content = File.ReadAllText(filePath, Encoding.UTF8);
                        var category = ExtractCategoryFromPath(filePath, kbFolderPath);
                        var fileName = Path.GetFileNameWithoutExtension(filePath);

                        Console.ForegroundColor = ConsoleColor.Yellow;
                        logger.LogInformation($"[{fileCount:D2}/{allFiles.Length:D2}] {category,-15} | {fileName}");
                        Console.ResetColor();

                        // Chunk document
                        var chunks = ChunkDocument(filePath, content, category, logger);
                        logger.LogInformation($"              ? Split into {chunks.Count} chunks");

                        // Insert chunks
                        int chunksInsertedForFile = 0;
                        int embeddingsGeneratedForFile = 0;
                        int embeddingErrorsForFile = 0;

                        foreach (var chunk in chunks)
                        {
                            try
                            {
                                string embeddingBase64 = null;

                                // Generate embedding
                                try
                                {
                                    var embedding = await embeddingService.GenerateEmbeddingAsync(chunk.Content);
                                    if (embedding != null)
                                    {
                                        embeddingBase64 = Convert.ToBase64String(embedding);
                                        embeddingsGeneratedForFile++;
                                        totalEmbeddingsGenerated++;
                                    }
                                }
                                catch (Exception embEx)
                                {
                                    logger.LogWarning($"              ??  Embedding failed: {embEx.Message}");
                                    embeddingErrorsForFile++;
                                    totalEmbeddingErrors++;
                                }

                                // Insert to DB
                                var dbChunk = new KnowledgeBaseDocuments
                                {
                                    Title = chunk.Title,
                                    Content = chunk.Content,
                                    Category = chunk.Category,
                                    Tags = chunk.Tags,
                                    FilePath = chunk.FilePath,
                                    Embedding = embeddingBase64,
                                    EmbeddingDimensions = chunk.EmbeddingDimensions,
                                    IsActive = chunk.IsActive,
                                    CreatedAt = chunk.CreatedAt,
                                    UpdatedAt = chunk.UpdatedAt,
                                    ViewCount = chunk.ViewCount,
                                    RelevanceScore = chunk.RelevanceScore,
                                    CreatedBy = chunk.CreatedBy
                                };

                                context.KnowledgeBaseDocuments.Add(dbChunk);
                                chunksInsertedForFile++;
                            }
                            catch (Exception chunkEx)
                            {
                                logger.LogWarning($"              ??  Chunk error: {chunkEx.Message}");
                            }
                        }

                        // Save all chunks for this file
                        if (chunksInsertedForFile > 0)
                        {
                            await context.SaveChangesAsync();
                            totalChunksInserted += chunksInsertedForFile;

                            Console.ForegroundColor = ConsoleColor.Green;
                            logger.LogInformation($"              ? Inserted {chunksInsertedForFile} chunks (embeddings: {embeddingsGeneratedForFile})");
                            Console.ResetColor();
                        }
                    }
                    catch (Exception fileEx)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        logger.LogError($"              ? Error: {fileEx.Message}");
                        Console.ResetColor();
                    }
                }

                logger.LogInformation("");
                logger.LogInformation("???????????????????????????????????????????????????");
                Console.ForegroundColor = ConsoleColor.Green;
                logger.LogInformation("? KNOWLEDGE BASE LOADING COMPLETE!");
                Console.ResetColor();
                logger.LogInformation("???????????????????????????????????????????????????");
                logger.LogInformation("");
                logger.LogInformation($"Summary:");
                logger.LogInformation($"  Files processed:          {fileCount}");
                logger.LogInformation($"  Total chunks inserted:    {totalChunksInserted}");
                logger.LogInformation($"  Embeddings generated:     {totalEmbeddingsGenerated}");
                logger.LogInformation($"  Embedding errors:         {totalEmbeddingErrors}");
                logger.LogInformation("");

                // Verify data
                var count = await context.KnowledgeBaseDocuments.CountAsync();
                logger.LogInformation($"Verification: {count} documents in database");
                logger.LogInformation("");

                Console.ForegroundColor = ConsoleColor.Cyan;
                logger.LogInformation("Ready for RAG queries!");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"? Fatal Error: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                Console.ResetColor();
                Environment.Exit(1);
            }
        }

        static string ExtractCategoryFromPath(string filePath, string basePath)
        {
            try
            {
                var relativePath = filePath.Replace(basePath, "").TrimStart('\\', '/');
                var parts = relativePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                return parts.Length > 1 ? parts[parts.Length - 2] : "General";
            }
            catch
            {
                return "General";
            }
        }

        static List<KnowledgeBaseChunk> ChunkDocument(
            string filePath,
            string content,
            string category,
            ILogger<Program> logger)
        {
            var chunks = new List<KnowledgeBaseChunk>();
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var title = ExtractTitle(content, fileName);

            var paragraphs = SplitIntoParagraphs(content);
            int chunkSequence = 1;

            foreach (var paragraph in paragraphs)
            {
                if (string.IsNullOrWhiteSpace(paragraph))
                    continue;

                var cleanContent = CleanContent(paragraph);
                if (cleanContent.Length < 50)
                    continue;

                chunks.Add(new KnowledgeBaseChunk
                {
                    Title = $"{title} - Part {chunkSequence}",
                    Content = cleanContent,
                    Category = category,
                    Tags = ExtractTags(category, paragraph),
                    FilePath = filePath,
                    EmbeddingDimensions = 1024,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    ViewCount = 0,
                    RelevanceScore = 0.0,
                    CreatedBy = "KBLoader",
                    UpdatedBy = "KBLoader"
                });

                chunkSequence++;
            }

            return chunks;
        }

        static string ExtractTitle(string content, string fileName)
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

        static List<string> SplitIntoParagraphs(string content)
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
                    if (!Regex.IsMatch(trimmed, @"^[=\-*]{5,}$") && !string.IsNullOrWhiteSpace(trimmed))
                    {
                        paragraphs.Add(trimmed);
                    }
                }
                return paragraphs;
            }
            catch
            {
                return content.Split(new[] { ". " }, StringSplitOptions.None)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            }
        }

        static string CleanContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return content;

            content = content.Trim();
            content = Regex.Replace(content, @"\s+", " ");
            content = Regex.Replace(content, @"\n\s*\n", "\n");
            content = Regex.Replace(content, @"\n", " ");
            return content;
        }

        static string ExtractTags(string category, string content)
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
                        tags.Add(term);
                }
                tags = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch { }

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
