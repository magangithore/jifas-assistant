using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Jifas.Assistant;
using Jifas.Assistant.Configuration;
using Jifas.Assistant.Services;
using Jifas.Assistant.Utilities;
using Jifas.Assistant.Middleware;
using jifas_assistant.DAL.Models;

var builder = WebApplication.CreateBuilder(args);

// ========================================
// ADD SERVICES TO CONTAINER
// ========================================

// 1. Add Database Context with SQL Server (OLD)
// NOTE: Using JIFAS_AssistantContext from DAL instead
/*
builder.Services.AddDbContext<JifasAssistantDbContext>(options =>
{
    var connectionString = builder.Configuration["ConnectionStrings:DefaultConnection"];
    options.UseSqlServer(connectionString, sqlServerOptions =>
    {
        sqlServerOptions.MigrationsAssembly("Jifas.Assistant");
        sqlServerOptions.EnableRetryOnFailure(maxRetryCount: 5);
    });
});
*/

// 2. Add Configuration Models (Strongly Typed Settings) - ONLY ESSENTIALS
builder.Services.Configure<LocalAISettings>(builder.Configuration.GetSection("LocalAI"));
builder.Services.Configure<KnowledgeBaseSettings>(builder.Configuration.GetSection("KnowledgeBase"));
builder.Services.Configure<ChatSettings>(builder.Configuration.GetSection("Chat"));
builder.Services.Configure<CachingSettings>(builder.Configuration.GetSection("Caching"));
builder.Services.Configure<SuggestionSettings>(builder.Configuration.GetSection("Suggestion"));

// 3. Add AppSettings Helper
builder.Services.AddSingleton(sp => new AppSettings(builder.Configuration));

// 3.5. Add Data Access Layer - Repositories & Unit of Work
// NOTE: Old data layer commented out - keeping for reference
// builder.Services.AddScoped<IChatRepository, ChatRepository>();
// builder.Services.AddScoped<IKnowledgeBaseRepository, KnowledgeBaseRepository>();
// builder.Services.AddScoped<IUnitOfWork, Jifas.Assistant.Data.UnitOfWork.UnitOfWork>();

// 3.6. Add DAL Context for KB Search
builder.Services.AddDbContext<JIFAS_AssistantContext>(options =>
{
    var connectionString = builder.Configuration["ConnectionStrings:DefaultConnection"];
    options.UseSqlServer(connectionString);
});

// 3.7. Add Controllers
builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
        options.SerializerSettings.Formatting = Newtonsoft.Json.Formatting.Indented;
    });

// 5. Add Swagger/OpenAPI Documentation
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() 
    { 
        Title = "JIFAS AI Assistant API",
        Version = "v1.0",
        Description = "Intelligent AI-powered chatbot with Knowledge Base and Local Ollama AI Integration"
    });
});

// 6. Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// 7. Add In-Memory Caching
builder.Services.AddMemoryCache();

// 7.5 Add HttpClient Factory
builder.Services.AddHttpClient();

// 8. Add Application Services (ESSENTIAL ONLY)
// ========== Core Services ==========
builder.Services.AddScoped<ILoggerService, FileLoggerService>();
builder.Services.AddScoped<ICacheService, MemoryCacheService>();
builder.Services.AddScoped<IInputValidator, InputValidator>();
builder.Services.AddScoped<ILocalizationService, LocalizationService>();
builder.Services.AddScoped<IKnowledgeBaseSearchService, KnowledgeBaseSearchService>();
builder.Services.AddScoped<IPromptEngineeringService, PromptEngineeringService>();

// AI Service - USE LOCAL AI INSTEAD OF GEMINI
// Swap comments to switch between LocalAI and Gemini
builder.Services.AddScoped<IGeminiService, LocalAIService>();  // Local AI (Ollama) - ACTIVE
// builder.Services.AddScoped<IGeminiService, GeminiService>();  // Gemini API - DISABLED

// Embedding Service - Ollama qwen3-embedding:4b
builder.Services.AddScoped<IEmbeddingService, OllamaEmbeddingService>();

// Knowledge Base Services
builder.Services.AddScoped<IKnowledgeBaseLoaderService, KnowledgeBaseLoaderService>();
builder.Services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();
builder.Services.AddScoped<IChatHistoryService, ChatHistoryService>();
builder.Services.AddScoped<ITicketService, TicketService>();
builder.Services.AddScoped<ISuggestionService, SuggestionService>();
builder.Services.AddScoped<IHealthCheckService, HealthCheckService>();
builder.Services.AddScoped<IOutOfScopeDetector, OutOfScopeDetector>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IJifasContextService, JifasContextService>();
builder.Services.AddScoped<IKnowledgeBaseContextService, KnowledgeBaseContextService>();

// ========== NEW: Consolidated AI Quality Services (3 files instead of 6) ==========
builder.Services.AddScoped<IQueryUnderstandingService, QueryUnderstandingService>();
builder.Services.AddScoped<IResponseQualityService, ResponseQualityService>();
builder.Services.AddScoped<IConversationIntelligenceService, ConversationIntelligenceService>();
// Register legacy interfaces pointing to consolidated services
builder.Services.AddScoped<IConversationMemoryService>(sp => sp.GetRequiredService<IConversationIntelligenceService>());
builder.Services.AddScoped<IFeedbackLearningService>(sp => sp.GetRequiredService<IConversationIntelligenceService>());

// ChatService - MUST be registered AFTER all its dependencies
builder.Services.AddScoped<IChatService, ChatService>();

// ========== Optional Services ==========
builder.Services.AddSingleton<ICommonQueryCacheService, CommonQueryCacheService>();

// 9. Add Health Checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<JIFAS_AssistantContext>(
        name: "database",
        tags: new[] { "ready" });

// ========================================
// BUILD APP
// ========================================

var app = builder.Build();

// ========================================
// DATABASE INITIALIZATION
// ========================================

// DATABASE INITIALIZATION (SIMPLIFIED)
// NOTE: Using JIFAS_AssistantContext only for RAG KB

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<JIFAS_AssistantContext>();
        
        if (context.Database.IsSqlServer())
        {
            try
            {
                context.Database.Migrate();
                var logger = services.GetService<ILogger<Program>>();
                if (logger != null)
                    logger.LogInformation("Database migration completed successfully.");
            }
            catch (Exception migrateEx)
            {
                var logger = services.GetService<ILogger<Program>>();
                if (logger != null)
                    logger.LogWarning("Database migration skipped. Error: {0}", migrateEx.Message);
            }
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetService<ILogger<Program>>();
        if (logger != null)
            logger.LogError(ex, "An error occurred while initializing the database.");
    }
}

// ========================================
// CONFIGURE HTTP REQUEST PIPELINE
// ========================================

// 0. JWT AUTHENTICATION MIDDLEWARE (Load from appsettings.json - NO hardcoded secrets)
app.UseJwtAuthentication();

// 0.5 REQUEST LOGGING & CORRELATION MIDDLEWARE (ONLY in Production)
if (!app.Environment.IsDevelopment())
{
    app.UseMiddleware<RequestLoggingMiddleware>();
}

// Development-specific middleware
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
    // Only redirect to HTTPS in production
    app.UseHttpsRedirection();
}

// 2. CORS (MUST be before StaticFiles)
app.UseCors("AllowAll");

// 3. Static Files (BEFORE Swagger so bundled files can be served)
app.UseStaticFiles();

// 4. Routing (MUST be before Swagger UI in modern ASP.NET Core)
app.UseRouting();

// 5. Authorization
app.UseAuthorization();

// 6. Map Endpoints (MUST be before Swagger for discovery)
app.MapControllers();
app.MapHealthChecks("/health");

// 7. Swagger MIDDLEWARE (AFTER endpoint mapping for proper discovery)
// Enable Swagger ONLY in Development
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "JIFAS AI Assistant API v1.0");
        c.RoutePrefix = "swagger";  // Serve at /swagger/
        c.DocumentTitle = "JIFAS AI Assistant API Documentation";
    });
}

// 8. Static Files removed - moved earlier in pipeline (before Swagger)

// 9. Root endpoint
app.MapGet("/", () => new 
{ 
    message = "JIFAS AI Assistant API v1.0",
    status = "running",
    documentation = "/swagger"
});

// 10. API info endpoint
app.MapGet("/api", () => new 
{ 
    message = "JIFAS AI Assistant API v1.0",
    status = "running",
    version = "1.0",
    documentation = "/swagger"
});

// ========================================
// RUN APP
// ========================================

app.Run();
