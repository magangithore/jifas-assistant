using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace Jifas.Assistant.Data
{
    /// <summary>
    /// Design-time DbContext factory for migrations
    /// Used by EF Core tools to create DbContext instances during migrations
    /// </summary>
    public class JifasAssistantDbContextFactory : IDesignTimeDbContextFactory<JifasAssistantDbContext>
    {
        public JifasAssistantDbContext CreateDbContext(string[] args)
        {
            // Build configuration from appsettings.json
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"}.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            // Create DbContext options
            var optionsBuilder = new DbContextOptionsBuilder<JifasAssistantDbContext>();
            var connectionString = configuration["ConnectionStrings:DefaultConnection"];

            optionsBuilder.UseSqlServer(connectionString, sqlServerOptions =>
            {
                sqlServerOptions.MigrationsAssembly("Jifas.Assistant");
                sqlServerOptions.EnableRetryOnFailure(maxRetryCount: 5);
            });

            return new JifasAssistantDbContext(optionsBuilder.Options);
        }
    }
}
