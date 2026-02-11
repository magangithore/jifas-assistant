using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Jifas.Assistant.Configuration;
using Jifas.Assistant.Data;
using Jifas.Assistant.Data.Repositories;
using Jifas.Assistant.Data.UnitOfWork;

var builder = WebApplication.CreateBuilder(args);

// ========================================
// ADD SERVICES TO CONTAINER
// ========================================

// 1. Add Database Context with SQL Server
builder.Services.AddDbContext<JifasAssistantDbContext>(options =>
{
    var connectionString = builder.Configuration["ConnectionStrings:DefaultConnection"];
    options.UseSqlServer(connectionString, sqlServerOptions =>
    {
        sqlServerOptions.MigrationsAssembly("Jifas.Assistant");
        sqlServerOptions.EnableRetryOnFailure(maxRetryCount: 5);
    });
});

// 2. Add Configuration Models (Strongly Typed Settings)
builder.Services.Configure<GeminiSettings>(builder.Configuration.GetSection("Gemini"));
builder.Services.Configure<OpenAISettings>(builder.Configuration.GetSection("OpenAI"));
builder.Services.Configure<AzureOpenAISettings>(builder.Configuration.GetSection("Azure:OpenAI"));
builder.Services.Configure<QdrantSettings>(builder.Configuration.GetSection("Qdrant"));
builder.Services.Configure<KnowledgeBaseSettings>(builder.Configuration.GetSection("KnowledgeBase"));
builder.Services.Configure<ChatSettings>(builder.Configuration.GetSection("Chat"));
builder.Services.Configure<CachingSettings>(builder.Configuration.GetSection("Caching"));
builder.Services.Configure<ApiSettings>(builder.Configuration.GetSection("API"));
builder.Services.Configure<SupportSettings>(builder.Configuration.GetSection("Support"));
builder.Services.Configure<SuggestionSettings>(builder.Configuration.GetSection("Suggestion"));
builder.Services.Configure<SearchSettings>(builder.Configuration.GetSection("Search"));
builder.Services.Configure<MetricsSettings>(builder.Configuration.GetSection("Metrics"));
builder.Services.Configure<HealthCheckSettings>(builder.Configuration.GetSection("HealthCheck"));
builder.Services.Configure<PerformanceSettings>(builder.Configuration.GetSection("Performance"));
builder.Services.Configure<OptimizationSettings>(builder.Configuration.GetSection("Optimization"));

// 3. Add AppSettings Helper
builder.Services.AddSingleton(sp => new AppSettings(builder.Configuration));

// 3.5. Add Data Access Layer - Repositories & Unit of Work
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddScoped<IKnowledgeBaseRepository, KnowledgeBaseRepository>();
builder.Services.AddScoped<IUnitOfWork, Jifas.Assistant.Data.UnitOfWork.UnitOfWork>();

// 4. Add Controllers & JSON Options
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

// 8. Add Health Checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<JifasAssistantDbContext>(
        name: "database",
        tags: new[] { "ready" });

// ========================================
// BUILD APP
// ========================================

var app = builder.Build();

// ========================================
// DATABASE INITIALIZATION
// ========================================

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<JifasAssistantDbContext>();
        
        if (context.Database.IsSqlServer())
        {
            // Apply any pending migrations
            context.Database.Migrate();
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetService<ILogger<Program>>();
        if (logger != null)
            logger.LogError(ex, "An error occurred while migrating the database.");
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
