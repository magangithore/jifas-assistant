using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Jifas.Assistant.Configuration;
using Jifas.Assistant.Services;
using Jifas.Assistant.Utilities;
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
builder.Services.Configure<GeminiSettings>(builder.Configuration.GetSection("Gemini"));
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

// 3.7. Add Knowledge Base Search Service (RAG)
builder.Services.AddScoped<IKnowledgeBaseSearchService, KnowledgeBaseSearchService>();
// NOTE: IKnowledgeBaseSearchService already registered above, no duplicate needed
builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
        options.SerializerSettings.Formatting = Newtonsoft.Json.Formatting.Indented;
    });

// 5. Add Swagger/OpenAPI Documentation
builder.Services.AddSwaggerGen();

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
builder.Services.AddScoped<IInputValidator, InputValidator>();  // ? INPUT VALIDATION (CRITICAL)
builder.Services.AddScoped<IKnowledgeBaseSearchService, KnowledgeBaseSearchService>();  // ? KB SEARCH (MUST BE BEFORE PROMPT ENGINE)
builder.Services.AddScoped<IPromptEngineeringService, PromptEngineeringService>();  // ? INTELLIGENT PROMPT ENGINE
builder.Services.AddScoped<IGeminiService, GeminiService>();  // ? ENHANCED GEMINI WITH PROMPT ENGINE
builder.Services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();
builder.Services.AddScoped<IEmbeddingService, GeminiEmbeddingService>();
builder.Services.AddScoped<IChatHistoryService, ChatHistoryService>();  // ? CHAT HISTORY PERSISTENCE
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<ITicketService, TicketService>();
builder.Services.AddScoped<ISuggestionService, SuggestionService>();
builder.Services.AddScoped<IHealthCheckService, HealthCheckService>();
builder.Services.AddScoped<IOutOfScopeDetector, OutOfScopeDetector>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IJifasContextService, JifasContextService>();

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

// Development-specific middleware
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "JIFAS AI Assistant API v1.0");
        c.RoutePrefix = "api-docs";
    });
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

// 1. HTTPS Redirect (skip in Docker or development)
if (!app.Environment.IsEnvironment("Docker"))
{
    app.UseHttpsRedirection();
}

// 2. CORS
app.UseCors("AllowAll");

// 3. Static Files
app.UseStaticFiles();

// 4. Routing
app.UseRouting();

// 5. Authorization
app.UseAuthorization();

// 6. Map Endpoints
app.MapControllers();
app.MapHealthChecks("/health");

// 7. Root endpoint
app.MapGet("/", () => new 
{ 
    message = "JIFAS AI Assistant API v1.0",
    status = "running",
    documentation = "/api-docs"
});

// ========================================
// RUN APP
// ========================================

app.Run();
