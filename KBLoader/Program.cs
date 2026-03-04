using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Jifas.Assistant.KBLoader
{
    /// <summary>
    /// Console application untuk load Knowledge Base via API endpoint
    /// Requires: Jifas.Assistant API running on http://localhost:5000
    /// Jalankan: dotnet run --project KBLoader/KBLoader.csproj -- --yes
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            // Check for auto-confirm flag
            bool autoConfirm = args.Contains("--yes") || args.Contains("-y");
            string apiBaseUrl = "http://localhost:5000"; // Configurable if needed
            
            Console.WriteLine("??????????????????????????????????????????????????????");
            Console.WriteLine("?   JIFAS Knowledge Base Loader - Via API Endpoint   ?");
            Console.WriteLine("??????????????????????????????????????????????????????");
            Console.WriteLine();

            try
            {
                // Setup services
                var services = new ServiceCollection();
                
                // Find appsettings.json
                var basePath = Directory.GetCurrentDirectory();
                if (!File.Exists(Path.Combine(basePath, "appsettings.json")))
                {
                    basePath = Path.Combine(Directory.GetCurrentDirectory(), "KBLoader");
                }
                
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(basePath);
                
                var appSettingsPath = Path.Combine(basePath, "appsettings.json");
                if (File.Exists(appSettingsPath))
                {
                    configBuilder.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                }
                
                var configuration = configBuilder.Build();

                services.AddSingleton<IConfiguration>(configuration);
                services.AddLogging(builder => 
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                });
                services.AddHttpClient();

                var serviceProvider = services.BuildServiceProvider();
                var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
                var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                logger.LogInformation("Starting Knowledge Base loading process...");
                logger.LogInformation("");
                
                // Check API is running
                logger.LogInformation($"Checking API at {apiBaseUrl}...");
                using (var client = httpClientFactory.CreateClient())
                {
                    try
                    {
                        var response = await client.GetAsync($"{apiBaseUrl}/api/health");
                        if (!response.IsSuccessStatusCode)
                        {
                            logger.LogError($"API returned status {response.StatusCode}. Make sure API is running: dotnet run (from Jifas.Assistant/)");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError($"Cannot connect to API at {apiBaseUrl}. Make sure it's running!");
                        logger.LogError($"Error: {ex.Message}");
                        logger.LogInformation("Start API with: dotnet run (from Jifas.Assistant/)");
                        return;
                    }
                }
                
                logger.LogInformation("? API is responding");
                logger.LogInformation("");

                // Determine KB folder path
                var kbFolderPath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "..",
                    "Jifas.Assistant",
                    "KnowledgeBase"
                );

                kbFolderPath = Path.GetFullPath(kbFolderPath);
                
                logger.LogInformation($"KB Folder Path: {kbFolderPath}");
                logger.LogInformation("");

                if (!Directory.Exists(kbFolderPath))
                {
                    logger.LogError($"Knowledge Base folder not found: {kbFolderPath}");
                    return;
                }

                var allFiles = Directory.GetFiles(kbFolderPath, "*.txt", SearchOption.AllDirectories);
                logger.LogInformation($"Found {allFiles.Length} KB files");
                logger.LogInformation("");

                // Confirm
                logger.LogWarning("??  Existing Knowledge Base will be cleared before loading new data");
                
                string response_str;
                if (autoConfirm)
                {
                    logger.LogInformation("Auto-confirming (--yes flag detected)");
                    response_str = "Y";
                }
                else
                {
                    Console.Write("Continue? (Y/N): ");
                    response_str = Console.ReadLine();
                }
                
                if (response_str?.ToUpper() != "Y")
                {
                    logger.LogInformation("Operation cancelled by user");
                    return;
                }

                logger.LogInformation("");
                logger.LogInformation("???????????????????????????????????????????????????");
                logger.LogInformation("Starting file processing...");
                logger.LogInformation("???????????????????????????????????????????????????");
                logger.LogInformation("");

                int fileCount = 0;
                int totalChunksInserted = 0;
                int totalFilesSuccess = 0;

                using (var client = httpClientFactory.CreateClient())
                {
                    client.DefaultRequestHeaders.Add("Content-Type", "application/json");
                    
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

                            // Create document request
                            var docRequest = new
                            {
                                title = fileName,
                                content = content,
                                category = category,
                                tags = ExtractTags(category, content)
                            };

                            var json = JsonConvert.SerializeObject(docRequest);
                            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                            // Call API
                            var apiResponse = await client.PostAsync($"{apiBaseUrl}/api/kb/documents", httpContent);

                            if (apiResponse.IsSuccessStatusCode)
                            {
                                var responseBody = await apiResponse.Content.ReadAsStringAsync();
                                dynamic result = JsonConvert.DeserializeObject(responseBody);
                                
                                Console.ForegroundColor = ConsoleColor.Green;
                                logger.LogInformation($"              ? Inserted (Document ID: {result.documentId})");
                                Console.ResetColor();
                                
                                totalFilesSuccess++;
                                totalChunksInserted++;
                            }
                            else
                            {
                                var errorBody = await apiResponse.Content.ReadAsStringAsync();
                                Console.ForegroundColor = ConsoleColor.Red;
                                logger.LogWarning($"              ? API Error {apiResponse.StatusCode}: {errorBody}");
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
                }

                logger.LogInformation("");
                logger.LogInformation("???????????????????????????????????????????????????");
                Console.ForegroundColor = ConsoleColor.Green;
                logger.LogInformation("? KNOWLEDGE BASE LOADING COMPLETE!");
                Console.ResetColor();
                logger.LogInformation("???????????????????????????????????????????????????");
                logger.LogInformation("");
                logger.LogInformation($"Summary:");
                logger.LogInformation($"  Files processed:     {fileCount}");
                logger.LogInformation($"  Files inserted:      {totalFilesSuccess}");
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
