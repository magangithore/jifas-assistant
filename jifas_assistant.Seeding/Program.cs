using System;
using System.IO;
using System.Threading.Tasks;
using jifas_assistant.DAL.Models;
using jifas_assistant.DAL.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace jifas_assistant.Seeding;

/// <summary>
/// Console application untuk seed KB files dengan Gemini embeddings
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("?? JIFAS Knowledge Base Seeding Tool");
        Console.WriteLine("====================================\n");

        // Load configuration
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        // Get settings
        var connectionString = config["ConnectionStrings:DefaultConnection"] 
            ?? "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=JIFAS_Assistant;Integrated Security=True;Encrypt=True";
        
        var geminiApiKey = config["Gemini:ApiKey"] 
            ?? throw new Exception("Gemini:ApiKey not found in appsettings.json");
        
        var geminiModel = config["Gemini:Model"] ?? "embedding-001";
        
        var kbFolderPath = args.Length > 0 ? args[0] : "./knowledge-base";

        Console.WriteLine($"Connection String: {connectionString}");
        Console.WriteLine($"KB Folder: {kbFolderPath}");
        Console.WriteLine($"Gemini Model: {geminiModel}\n");

        // Database context
        var options = new DbContextOptionsBuilder<JIFAS_AssistantContext>()
            .UseSqlServer(connectionString)
            .Options;

        using (var context = new JIFAS_AssistantContext(options))
        {
            // Check database connection
            try
            {
                await context.Database.OpenConnectionAsync();
                Console.WriteLine("? Database connection: OK");
                context.Database.CloseConnection();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Database connection failed: {ex.Message}");
                return;
            }

            // Check API key
            if (string.IsNullOrWhiteSpace(geminiApiKey))
            {
                Console.WriteLine("? Gemini API key not configured");
                return;
            }
            Console.WriteLine("? Gemini API key: Configured\n");

            // Create seeding service
            var embeddingService = new GeminiEmbeddingService(geminiApiKey, geminiModel);
            var seedingService = new KnowledgeBaseSeedingService(context, embeddingService);

            // Run seeding
            Console.WriteLine($"Starting seeding...\n");
            var result = await seedingService.SeedKnowledgeBaseAsync(kbFolderPath);

            // Display results
            Console.WriteLine("\n?? Seeding Results:");
            Console.WriteLine("===================");
            Console.WriteLine($"Total files:       {result.TotalFiles}");
            Console.WriteLine($"Success:           {result.SuccessCount}");
            Console.WriteLine($"Failed:            {result.FailedCount}");
            Console.WriteLine($"Total chunks:      {result.TotalChunks}");

            if (result.FailedFiles.Count > 0)
            {
                Console.WriteLine("\n? Failed files:");
                foreach (var failed in result.FailedFiles)
                {
                    Console.WriteLine($"  - {Path.GetFileName(failed.Key)}: {failed.Value}");
                }
            }

            if (result.Success)
            {
                Console.WriteLine($"\n? {result.Message}");
            }
            else
            {
                Console.WriteLine($"\n??  {result.Message}");
            }
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}

