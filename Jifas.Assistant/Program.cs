using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Jifas.Assistant;
using Jifas.Assistant.Configuration;
using Jifas.Assistant.Services;
using Jifas.Assistant.Utilities;
using Jifas.Assistant.Hubs;
using jifas_assistant.DAL.Models;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel for long-running AI requests (Ollama can take 60-180s)
builder.Services.Configure<KestrelServerOptions>(options =>
{
    // Overall request timeout: 5 minutes (Ollama main + suggestions combined)
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(5);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(5);
});

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
builder.Services.Configure<OllamaSettings>(builder.Configuration.GetSection("Ollama"));
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
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

    options.UseSqlServer(connectionString);
});

// 3.7. Add Controllers
builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
        options.SerializerSettings.Formatting = Newtonsoft.Json.Formatting.Indented;
    });

// 4. Authentication & Authorization
var jwtEnabled = builder.Configuration.GetValue<bool>("Jwt:Enabled");
var jwtAuthority = builder.Configuration["Jwt:Authority"];
var jwtAudience = builder.Configuration["Jwt:Audience"];
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"];

if (jwtEnabled)
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = builder.Configuration.GetValue<bool>("Jwt:RequireHttpsMetadata");
            options.Authority = !string.IsNullOrWhiteSpace(jwtAuthority) ? jwtAuthority : null;
            options.Audience = jwtAudience;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = builder.Configuration.GetValue<bool>("Jwt:ValidateIssuer", true),
                ValidateAudience = builder.Configuration.GetValue<bool>("Jwt:ValidateAudience", true),
                ValidateLifetime = builder.Configuration.GetValue<bool>("Jwt:ValidateLifetime", true),
                ValidateIssuerSigningKey = true,
                ValidAudience = jwtAudience,
                ClockSkew = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("Jwt:ClockSkewSeconds", 30))
            };

            if (!string.IsNullOrWhiteSpace(jwtAuthority))
                options.TokenValidationParameters.ValidIssuer = jwtAuthority;

            if (!string.IsNullOrWhiteSpace(jwtSigningKey))
                options.TokenValidationParameters.IssuerSigningKey =
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey));
        });
}
else
{
    builder.Services.AddAuthentication();
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IAuthorizationHandler, AdminAccessAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("KnowledgeBaseAdmin", policy =>
        policy.AddRequirements(new AdminAccessRequirement()));
});

// 5. Add Swagger/OpenAPI Documentation
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() 
    { 
        Title = "JIFAS AI Assistant API",
        Version = "v1.0",
        Description = "Intelligent AI-powered chatbot with Knowledge Base and Local Ollama (qwen3:8b) Integration"
    });
});

// 6. Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("ConfiguredCors", policy =>
    {
        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>()?
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .ToArray() ?? Array.Empty<string>();

        if (allowedOrigins.Length == 0)
        {
            // Dev: allow any origin but still support credentials (needed for SignalR)
            // Use SetIsOriginAllowed instead of AllowAnyOrigin to allow AllowCredentials
            policy.SetIsOriginAllowed(_ => true)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
        else
        {
            // Production: explicit origins + credentials for SignalR
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
    });
});

// 7. Add In-Memory Caching
builder.Services.AddMemoryCache();

// 7.1 Add SignalR for real-time monitoring dashboard
builder.Services.AddSignalR();

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

// AI Service - Ollama qwen3:8b (typed HttpClient + scoped interface registration)
builder.Services.AddHttpClient<OllamaAIService>().Services
    .AddScoped<IOllamaService, OllamaAIService>();

// Embedding Service - Ollama qwen3-embedding:4b (typed HttpClient registration)
builder.Services.AddHttpClient<OllamaEmbeddingService>().Services
    .AddScoped<IEmbeddingService, OllamaEmbeddingService>();

// Knowledge Base Services
builder.Services.AddScoped<IKnowledgeBaseLoaderService, KnowledgeBaseLoaderService>();
builder.Services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();
builder.Services.AddScoped<IChatHistoryService, ChatHistoryService>();

// Ticket Service - Jira integration with typed HttpClient + SSL bypass for corporate network
builder.Services.AddHttpClient<TicketService>(client =>
{
    // Use Atlassian API gateway (required for Jira Cloud v3 with CloudId)
    client.BaseAddress = new Uri("https://api.atlassian.com");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    // Bypass corporate SSL inspection (same as Playwright jiraClient.js)
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
}).Services.AddScoped<ITicketService, TicketService>();

builder.Services.AddScoped<ISuggestionService, SuggestionService>();
builder.Services.AddScoped<IHealthCheckService, HealthCheckService>();
builder.Services.AddScoped<IOutOfScopeDetector, OutOfScopeDetector>();
builder.Services.AddScoped<IJifasContextService, JifasContextService>();
builder.Services.AddScoped<IKnowledgeBaseContextService, KnowledgeBaseContextService>();

// ========== NEW: Consolidated AI Quality Services (3 files instead of 6) ==========
builder.Services.AddScoped<IQueryUnderstandingService, QueryUnderstandingService>();
builder.Services.AddScoped<IResponseQualityService, ResponseQualityService>();
builder.Services.AddScoped<IConversationIntelligenceService, ConversationIntelligenceService>();
// Register legacy interfaces pointing to consolidated services
builder.Services.AddScoped<IConversationMemoryService>(sp => sp.GetRequiredService<IConversationIntelligenceService>());
builder.Services.AddScoped<IFeedbackLearningService>(sp => sp.GetRequiredService<IConversationIntelligenceService>());

// User long-term memory (persistent per-user profile)
builder.Services.AddScoped<IUserMemoryService, UserMemoryService>();

// AI Monitoring Service (persist + broadcast metrics)
builder.Services.AddScoped<IMonitoringService, MonitoringService>();

// ChatService - MUST be registered AFTER all its dependencies
builder.Services.AddScoped<IChatService, ChatService>();

// Embedding cache warmup - pre-loads all chunk embeddings on startup (eliminates cold-start latency)
builder.Services.AddHostedService<EmbeddingWarmupService>();

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

app.UseAuthentication();

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
app.UseCors("ConfiguredCors");

// 3. Static Files (BEFORE Swagger so bundled files can be served)
app.UseStaticFiles();

// 4. Routing (MUST be before Swagger UI in modern ASP.NET Core)
app.UseRouting();

// 5. Authorization
app.UseAuthorization();

// 6. Map Endpoints (MUST be before Swagger for discovery)
app.MapControllers();
app.MapHealthChecks("/health");
app.MapHub<MonitoringHub>("/hubs/monitoring");  // SignalR real-time monitoring

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
