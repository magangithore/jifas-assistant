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
using Jifas.Assistant.Services;

namespace Jifas.Assistant.KBLoader
{
    /// <summary>
    /// FAST Knowledge Base Loader - Direct DB Insert with Chunking
    /// Phase 1: Insert documents
    /// Phase 2: Generate document embeddings
    /// Phase 3: Chunk documents and generate chunk embeddings
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            bool autoConfirm = args.Contains("--yes") || args.Contains("-y");
            
            Console.WriteLine("??????????????????????????????????????????????????????");
            Console.WriteLine("?   JIFAS KB Loader - FAST Bulk Insert + Chunking   ?");
            Console.WriteLine("??????????????????????????????????????????????????????");
            Console.WriteLine();

            try
            {
                var services = new ServiceCollection();
                var basePath = Directory.GetCurrentDirectory();
                if (!File.Exists(Path.Combine(basePath, "appsettings.json")))
                {
                    basePath = Path.Combine(Directory.GetCurrentDirectory(), "KBLoader");
                }
                
                var configBuilder = new ConfigurationBuilder().SetBasePath(basePath);
                if (File.Exists(Path.Combine(basePath, "appsettings.json")))
                {
                    configBuilder.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                }
                
                var configuration = configBuilder.Build();

                services.AddSingleton<IConfiguration>(configuration);
                services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
                services.AddHttpClient();
                services.AddDbContext<JIFAS_AssistantContext>(options =>
                {
                    var connectionString = configuration["ConnectionStrings:DefaultConnection"];
                    options.UseSqlServer(connectionString);
                });
                services.AddScoped<IEmbeddingService, OllamaEmbeddingService>();

                var serviceProvider = services.BuildServiceProvider();
                var context = serviceProvider.GetRequiredService<JIFAS_AssistantContext>();
                var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
                var embeddingService = serviceProvider.GetRequiredService<IEmbeddingService>();

                logger.LogInformation("Starting Knowledge Base bulk loading...");
                logger.LogInformation("");

                // Get KB folder
                var kbFolderPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "Jifas.Assistant", "KnowledgeBase");
                kbFolderPath = Path.GetFullPath(kbFolderPath);
                
                logger.LogInformation($"KB Folder: {kbFolderPath}");

                if (!Directory.Exists(kbFolderPath))
                {
                    logger.LogError($"KB folder not found: {kbFolderPath}");
                    return;
                }

                var allFiles = Directory.GetFiles(kbFolderPath, "*.txt", SearchOption.AllDirectories);
                logger.LogInformation($"Found {allFiles.Length} KB files");
                logger.LogInformation("");

                // Confirm
                logger.LogWarning("??  Will clear existing KB documents");
                
                if (!autoConfirm)
                {
                    Console.Write("Continue? (Y/N): ");
                    var response = Console.ReadLine();
                    if (response?.ToUpper() != "Y") return;
                }

                logger.LogInformation("");
                logger.LogInformation("???????????????????????????????????????????????????");
                logger.LogInformation("PHASE 1: BULK INSERT DOCUMENTS");
                logger.LogInformation("???????????????????????????????????????????????????");
                logger.LogInformation("");

                // FAST: Batch delete
                logger.LogInformation("Clearing existing KB...");
                var existingDocs = await context.KnowledgeBaseDocuments.ToListAsync();
                if (existingDocs.Any())
                {
                    context.KnowledgeBaseDocuments.RemoveRange(existingDocs);
                    await context.SaveChangesAsync();
                }

                // FAST: Batch insert all documents
                var docsToInsert = new List<KnowledgeBaseDocuments>();

                int fileCount = 0;
                foreach (var filePath in allFiles)
                {
                    fileCount++;
                    try
                    {
                        var content = File.ReadAllText(filePath, Encoding.UTF8);
                        var category = ExtractCategory(filePath, kbFolderPath);
                        var fileName = Path.GetFileNameWithoutExtension(filePath);

                        Console.ForegroundColor = ConsoleColor.Yellow;
                        logger.LogInformation($"[{fileCount:D2}/{allFiles.Length}] {category,-15} | {fileName}");
                        Console.ResetColor();

                        // Create document (no chunking, no embeddings here!)
                        var doc = new KnowledgeBaseDocuments
                        {
                            Title = fileName,
                            Content = content,
                            Category = category,
                            Tags = ExtractTags(category, content),
                            FilePath = filePath,
                            Embedding = null,
                            EmbeddingDimensions = 0,
                            IsActive = true,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                            ViewCount = 0,
                            RelevanceScore = 0,
                            CreatedBy = "KBLoader"
                        };

                        docsToInsert.Add(doc);
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        logger.LogError($"              ? Error: {ex.Message}");
                        Console.ResetColor();
                    }
                }

                // FAST: Single bulk insert
                logger.LogInformation("");
                logger.LogInformation($"Inserting {docsToInsert.Count} documents (BULK)...");
                
                context.KnowledgeBaseDocuments.AddRange(docsToInsert);
                await context.SaveChangesAsync();

                Console.ForegroundColor = ConsoleColor.Green;
                logger.LogInformation($"? {docsToInsert.Count} documents inserted in seconds!");
                Console.ResetColor();

                // PHASE 2: Generate embeddings for documents
                logger.LogInformation("");
                logger.LogInformation("???????????????????????????????????????????????????");
                logger.LogInformation("PHASE 2: EMBEDDING GENERATION (DOCUMENTS)");
                logger.LogInformation("???????????????????????????????????????????????????");
                logger.LogInformation("");
                
                int embeddingCount = 0;
                int embeddingErrors = 0;
                foreach (var doc in docsToInsert)
                {
                    try
                    {
                        var embedding = await embeddingService.GenerateEmbeddingAsync(doc.Content);
                        if (embedding != null && embedding.Length > 0)
                        {
                            doc.Embedding = Convert.ToBase64String(embedding);
                            doc.EmbeddingDimensions = embedding.Length;
                            embeddingCount++;
                            
                            if (embeddingCount % 10 == 0 || embeddingCount == docsToInsert.Count)
                            {
                                logger.LogInformation($"  [{embeddingCount}/{docsToInsert.Count}] {doc.Title}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        logger.LogWarning($"  ??  Embedding failed for {doc.Title}: {ex.Message}");
                        Console.ResetColor();
                        embeddingErrors++;
                    }
                }
                
                await context.SaveChangesAsync();
                logger.LogInformation("");
                Console.ForegroundColor = ConsoleColor.Green;
                logger.LogInformation($"? Generated {embeddingCount} embeddings (Errors: {embeddingErrors})");
                Console.ResetColor();

                // PHASE 3: Chunk documents
                logger.LogInformation("");
                logger.LogInformation("???????????????????????????????????????????????????");
                logger.LogInformation("PHASE 3: CHUNKING DOCUMENTS");
                logger.LogInformation("???????????????????????????????????????????????????");
                logger.LogInformation("");

                // Clear existing chunks
                logger.LogInformation("Clearing existing chunks...");
                var existingChunks = await context.KnowledgeBaseChunks.ToListAsync();
                if (existingChunks.Any())
                {
                    context.KnowledgeBaseChunks.RemoveRange(existingChunks);
                    await context.SaveChangesAsync();
                }

                var allDocuments = await context.KnowledgeBaseDocuments.ToListAsync();
                var chunksToInsert = new List<KnowledgeBaseChunks>();
                int totalChunks = 0;
                int chunksWithEmbedding = 0;

                logger.LogInformation($"Chunking {allDocuments.Count} documents...");
                logger.LogInformation("");

                int docIndex = 0;
                foreach (var document in allDocuments)
                {
                    docIndex++;
                    try
                    {
                        var chunks = SplitIntoChunks(document.Content, chunkSize: 1000, overlapSize: 100);
                        
                        for (int i = 0; i < chunks.Count; i++)
                        {
                            var chunk = chunks[i];
                            if (string.IsNullOrWhiteSpace(chunk)) continue;

                            var chunkObj = new KnowledgeBaseChunks
                            {
                                DocumentId = document.Id,
                                ChunkIndex = i,
                                Content = chunk,
                                StartCharPos = GetCharPosition(document.Content, chunk, i),
                                EndCharPos = GetCharPosition(document.Content, chunk, i) + chunk.Length,
                                CreatedAt = DateTime.Now,
                                UpdatedAt = DateTime.Now,
                                Embedding = null,
                                EmbeddingDimensions = 0
                            };

                            // Generate embedding for chunk
                            try
                            {
                                var chunkEmbedding = await embeddingService.GenerateEmbeddingAsync(chunk);
                                if (chunkEmbedding != null && chunkEmbedding.Length > 0)
                                {
                                    chunkObj.Embedding = Convert.ToBase64String(chunkEmbedding);
                                    chunkObj.EmbeddingDimensions = chunkEmbedding.Length;
                                    chunksWithEmbedding++;
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning($"  ??  Chunk embedding failed: {ex.Message}");
                            }

                            chunksToInsert.Add(chunkObj);
                            totalChunks++;
                        }

                        if (docIndex % 5 == 0 || docIndex == allDocuments.Count)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            logger.LogInformation($"  [{docIndex}/{allDocuments.Count}] {document.Title} ? {chunks.Count} chunks");
                            Console.ResetColor();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        logger.LogError($"              ? Error chunking {document.Title}: {ex.Message}");
                        Console.ResetColor();
                    }
                }

                // Bulk insert chunks
                logger.LogInformation("");
                logger.LogInformation($"Inserting {chunksToInsert.Count} chunks (BULK)...");
                if (chunksToInsert.Any())
                {
                    context.KnowledgeBaseChunks.AddRange(chunksToInsert);
                    await context.SaveChangesAsync();
                }

                Console.ForegroundColor = ConsoleColor.Green;
                logger.LogInformation($"? {chunksToInsert.Count} chunks inserted!");
                Console.ResetColor();

                // Final Verify
                var finalDocCount = await context.KnowledgeBaseDocuments.CountAsync();
                var finalEmbeddedCount = await context.KnowledgeBaseDocuments.CountAsync(d => d.Embedding != null);
                var finalChunkCount = await context.KnowledgeBaseChunks.CountAsync();
                var finalChunksEmbeddedCount = await context.KnowledgeBaseChunks.CountAsync(c => c.Embedding != null);
                
                logger.LogInformation("");
                logger.LogInformation("???????????????????????????????????????????????????");
                logger.LogInformation("? KNOWLEDGE BASE LOADING COMPLETE!");
                logger.LogInformation("???????????????????????????????????????????????????");
                logger.LogInformation("");
                logger.LogInformation($"  Documents:         {finalDocCount}");
                logger.LogInformation($"  Doc Embeddings:    {finalEmbeddedCount}/{finalDocCount}");
                logger.LogInformation($"  Chunks:            {finalChunkCount}");
                logger.LogInformation($"  Chunk Embeddings:  {finalChunksEmbeddedCount}/{finalChunkCount}");
                logger.LogInformation("");
                Console.ForegroundColor = ConsoleColor.Cyan;
                logger.LogInformation("  Ready for RAG queries!");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"? Fatal Error: {ex.Message}");
                Console.WriteLine($"Stack: {ex.StackTrace}");
                Console.ResetColor();
                Environment.Exit(1);
            }
        }

        static string ExtractCategory(string filePath, string basePath)
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

        static List<string> SplitIntoChunks(string content, int chunkSize = 500, int overlapSize = 50)
        {
            var chunks = new List<string>();
            if (string.IsNullOrWhiteSpace(content)) return chunks;

            content = Regex.Replace(content, @"\s+", " ");
            var sentences = content.Split(new[] { ".", "!", "?" }, StringSplitOptions.RemoveEmptyEntries);

            var currentChunk = new StringBuilder();
            
            foreach (var sentence in sentences)
            {
                var trimmed = sentence.Trim();
                if (trimmed.Length == 0) continue;

                if (currentChunk.Length + trimmed.Length + 1 > chunkSize && currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    
                    // Calculate overlap
                    var words = currentChunk.ToString().Split(' ');
                    var overlapText = string.Join(" ", words.TakeLast(overlapSize / 10).ToArray());
                    currentChunk = new StringBuilder(overlapText);
                }

                currentChunk.Append(trimmed).Append(". ");
            }

            if (currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
            }

            return chunks;
        }

        static int GetCharPosition(string fullContent, string chunkContent, int chunkIndex)
        {
            int position = 0;
            int foundCount = 0;

            while (foundCount < chunkIndex && position < fullContent.Length)
            {
                position = fullContent.IndexOf(chunkContent, position + 1);
                if (position == -1) return 0;
                foundCount++;
            }

            return position >= 0 ? position : 0;
        }
    }
}

