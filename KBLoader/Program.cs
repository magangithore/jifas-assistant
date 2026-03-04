using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    /// FAST Knowledge Base Loader - Direct DB Insert
    /// Bulk insert without waiting for embeddings (embeddings generated in background later)
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            bool autoConfirm = args.Contains("--yes") || args.Contains("-y");
            
            Console.WriteLine("??????????????????????????????????????????????????????");
            Console.WriteLine("?   JIFAS KB Loader - FAST Bulk Insert              ?");
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
                services.AddDbContext<JIFAS_AssistantContext>(options =>
                {
                    var connectionString = configuration["ConnectionStrings:DefaultConnection"];
                    options.UseSqlServer(connectionString);
                });

                var serviceProvider = services.BuildServiceProvider();
                var context = serviceProvider.GetRequiredService<JIFAS_AssistantContext>();
                var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

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
                logger.LogInformation("FAST BULK INSERT MODE");
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

                // Verify
                var count = await context.KnowledgeBaseDocuments.CountAsync();
                logger.LogInformation("");
                logger.LogInformation("???????????????????????????????????????????????????");
                logger.LogInformation($"? COMPLETE: {count} documents in database");
                logger.LogInformation("???????????????????????????????????????????????????");
                logger.LogInformation("");
                logger.LogInformation("Note: Embeddings will be generated by API background job");
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
    }
}

