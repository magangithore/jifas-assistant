using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Pgvector.EntityFrameworkCore;
using Jifas.Assistant;
using Jifas.Assistant.Configuration;
using Jifas.Assistant.Services;
using Jifas.Assistant.Utilities;
using Jifas.Assistant.Hubs;
using jifas_assistant.DAL.Models;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using HealthChecks.Redis;

var builder = WebApplication.CreateBuilder(args);

// Request AI bisa lama karena menunggu Ollama dan proses RAG.
// Timeout Kestrel dinaikkan agar request valid tidak putus di tengah jalan.
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(5);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(5);

    var maxBodySize = builder.Configuration.GetValue<long?>("API:MaxRequestBodySize");
    if (maxBodySize.HasValue && maxBodySize.Value > 0)
    {
        options.Limits.MaxRequestBodySize = maxBodySize.Value;
    }
});

// ========================================
// REGISTRASI SERVICE APLIKASI
// ========================================

// Bind appsettings ke class configuration agar dependency injection lebih mudah dibaca.
builder.Services.Configure<OllamaSettings>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<KnowledgeBaseSettings>(builder.Configuration.GetSection("KnowledgeBase"));
builder.Services.Configure<ChatSettings>(builder.Configuration.GetSection("Chat"));
builder.Services.Configure<CachingSettings>(builder.Configuration.GetSection("Caching"));
builder.Services.Configure<SuggestionSettings>(builder.Configuration.GetSection("Suggestion"));

// Helper ringan untuk membaca configuration di service lama yang belum memakai IOptions.
builder.Services.AddSingleton(sp => new AppSettings(builder.Configuration));

// DbContext utama untuk Knowledge Base, RAG, pgvector, dan audit data chatbot.
// AddDbContextFactory dipakai karena service berjalan paralel dan butuh context sendiri.
// DbContextOptions harus Singleton agar compatible dengan IDbContextFactory singleton.
builder.Services.AddDbContextFactory<JIFAS_AssistantContext>((sp, options) =>
{
    ConfigureJifasDbContext(options, builder.Configuration);
});

// Controller API memakai Newtonsoft agar kontrak response lama tetap kompatibel.
builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
        options.SerializerSettings.Formatting = Newtonsoft.Json.Formatting.Indented;
    });

// Kompresi response membantu dashboard dan payload JSON besar tanpa mengubah kontrak API.
if (builder.Configuration.GetValue<bool>("Performance:EnableCompressionResponse"))
{
    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
        options.Providers.Add<BrotliCompressionProvider>();
        options.Providers.Add<GzipCompressionProvider>();
        options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
        {
            "application/json",
            "application/problem+json",
            "text/plain"
        });
    });

    builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    {
        options.Level = CompressionLevel.Fastest;
    });

    builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    {
        options.Level = CompressionLevel.Fastest;
    });
}

// JWT bisa dimatikan untuk development, tetapi production tetap disiapkan untuk validasi token.
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

// Swagger hanya ditampilkan di Development supaya endpoint internal tidak terekspos di Production.
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() 
    { 
        Title = "JIFAS AI Assistant API",
        Version = "v1.0",
        Description = "Intelligent AI-powered chatbot with Knowledge Base and Local Ollama (qwen3:8b) Integration"
    });
});

// CORS: jika origin kosong, mode development memperbolehkan semua origin untuk memudahkan test lokal.
// Jika origin diisi, hanya origin tersebut yang boleh akses API.
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
            if (builder.Environment.IsDevelopment())
            {
                // Development boleh longgar agar Postman/frontend lokal cepat dites.
                policy.SetIsOriginAllowed(_ => true)
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            }
            else
            {
                // Production tanpa daftar origin berarti akses browser lintas domain ditutup.
                // API server-to-server dan same-origin static dashboard tetap berjalan normal.
                policy.WithOrigins(Array.Empty<string>());
            }
        }
        else
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
    });
});

// Cache utama diarahkan ke Redis jika config tersedia.
// Memory cache tetap diregistrasikan sebagai fallback lokal jika Redis sementara gagal.
builder.Services.AddMemoryCache();
var redisConnectionString =
    builder.Configuration.GetConnectionString("Redis") ??
    builder.Configuration["Redis:ConnectionString"];
var useRedisCache =
    builder.Configuration.GetValue<bool>("Caching:UseRedis") &&
    !string.IsNullOrWhiteSpace(redisConnectionString);

if (useRedisCache)
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = builder.Configuration["Caching:RedisInstanceName"] ?? "JIFAS:";
    });
}

// SignalR dipakai dashboard monitoring real-time.
builder.Services.AddSignalR();

// HttpClient factory mencegah socket exhaustion ketika sering memanggil Ollama/Jira.
builder.Services.AddHttpClient();

// Service inti aplikasi.
builder.Services.AddScoped<ILoggerService, FileLoggerService>();
builder.Services.AddScoped<ICacheService>(sp =>
{
    if (useRedisCache)
    {
        return ActivatorUtilities.CreateInstance<RedisCacheService>(sp);
    }

    return ActivatorUtilities.CreateInstance<MemoryCacheService>(sp);
});
builder.Services.AddScoped<IInputValidator, InputValidator>();
builder.Services.AddScoped<ILocalizationService, LocalizationService>();
builder.Services.AddScoped<IKnowledgeBaseSearchService, KnowledgeBaseSearchService>();
builder.Services.AddScoped<IPromptEngineeringService, PromptEngineeringService>();

// Service LLM utama ke Ollama.
builder.Services.AddHttpClient<OllamaAIService>().Services
    .AddScoped<IOllamaService, OllamaAIService>();

// Service embedding untuk pgvector/RAG.
builder.Services.AddHttpClient<OllamaEmbeddingService>().Services
    .AddScoped<IEmbeddingService, OllamaEmbeddingService>();

// Service Knowledge Base dan riwayat percakapan.
builder.Services.AddScoped<IKnowledgeBaseLoaderService, KnowledgeBaseLoaderService>();
builder.Services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();
builder.Services.AddScoped<IChatHistoryService, ChatHistoryService>();

// Integrasi ticket Jira.
// Validasi SSL normal dipakai default; bypass hanya aktif jika environment eksplisit meminta.
builder.Services.AddHttpClient<TicketService>(client =>
{
    client.BaseAddress = new Uri("https://api.atlassian.com");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    ServerCertificateCustomValidationCallback =
        builder.Configuration.GetValue<bool>("Jira:BypassSslValidation")
            ? HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            : null
}).Services.AddScoped<ITicketService, TicketService>();

builder.Services.AddScoped<IHealthCheckService, HealthCheckService>();
builder.Services.AddScoped<IOutOfScopeDetector, OutOfScopeDetector>();
builder.Services.AddScoped<IJifasContextService, JifasContextService>();
builder.Services.AddScoped<IKnowledgeBaseContextService, KnowledgeBaseContextService>();
builder.Services.AddScoped<IAssistantCommandService, AssistantCommandService>();

// Service kualitas jawaban: memahami query, mengecek kualitas response, dan belajar dari percakapan.
builder.Services.AddScoped<IQueryUnderstandingService, QueryUnderstandingService>();
builder.Services.AddScoped<IResponseQualityService, ResponseQualityService>();
builder.Services.AddScoped<IConversationIntelligenceService, ConversationIntelligenceService>();
builder.Services.AddScoped<IAdaptiveContextPackService, AdaptiveContextPackService>();
builder.Services.AddScoped<IAiLearningService, AiLearningService>();
// Interface lama diarahkan ke service baru agar kode existing tetap kompatibel.
builder.Services.AddScoped<IConversationMemoryService>(sp => sp.GetRequiredService<IConversationIntelligenceService>());
builder.Services.AddScoped<IFeedbackLearningService>(sp => sp.GetRequiredService<IConversationIntelligenceService>());

// Profil memori user jangka panjang.
builder.Services.AddScoped<IUserMemoryService, UserMemoryService>();

// Monitoring menyimpan metrik dan broadcast ke dashboard.
builder.Services.AddScoped<IMonitoringService, MonitoringService>();

// Cross-session conversation memory — track last session per user for persistent context
builder.Services.AddScoped<ICrossSessionContextService, CrossSessionContextService>();

// ChatService diregistrasikan terakhir karena bergantung pada service-service di atas.
builder.Services.AddScoped<IChatService, ChatService>();

// Warmup embedding saat startup agar pencarian KB pertama tidak terlalu lambat.
builder.Services.AddHostedService<EmbeddingWarmupService>();
// Scheduler AI Learning memproses kandidat dan publish knowledge approved secara periodik.
builder.Services.AddHostedService<AiLearningSchedulerService>();

var ollamaHealthBaseUrl =
    builder.Configuration["Ollama:BaseUrl"] ??
    builder.Configuration["Ollama:Url"] ??
    "http://localhost:11434";
var ollamaHealthUrl = new Uri(new Uri(ollamaHealthBaseUrl.TrimEnd('/') + "/"), "api/tags");

// Health check untuk dependency utama. URL Ollama mengikuti konfigurasi runtime,
// bukan localhost container, agar Docker health mencerminkan server AI yang benar.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<JIFAS_AssistantContext>(
        name: "database",
        tags: new[] { "ready" })
    .AddUrlGroup(
        ollamaHealthUrl,
        name: "ollama",
        tags: new[] { "ready" })
    .AddRedis(
        redisConnectionString ?? "localhost:6379",
        name: "redis",
        tags: new[] { "ready" });

// ========================================
// STARTUP VALIDATION
// ========================================

if (!builder.Environment.IsDevelopment())
{
    var connStr = builder.Configuration["ConnectionStrings:DefaultConnection"];
    if (string.IsNullOrWhiteSpace(connStr))
        throw new InvalidOperationException("Production requires ConnectionStrings:DefaultConnection to be set via environment variable or secrets.");

    var adminKey = builder.Configuration["Admin:ApiKey"];
    if (string.IsNullOrWhiteSpace(adminKey))
        throw new InvalidOperationException("Production requires Admin:ApiKey to be set. Use environment variable Admin__ApiKey.");

    if (builder.Configuration.GetValue<bool>("Jwt:Enabled"))
    {
        var signingKey = builder.Configuration["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
            throw new InvalidOperationException("Production requires Jwt:SigningKey (min 32 chars) when JWT is enabled.");
    }
}

// ========================================
// BUILD APLIKASI
// ========================================

var app = builder.Build();

// ========================================
// INISIALISASI DATABASE
// ========================================

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<JIFAS_AssistantContext>();
        
        if (context.Database.IsNpgsql())
        {
            // Bootstrap PostgreSQL/pgvector memakai script resmi yang ikut dipublish ke image Docker.
            ExecutePostgresBootstrapScript(context);

            // Jalankan migrasi jika tersedia. Jika database lama sudah dibuat manual,
            // bootstrap idempotent di bawah tetap menjaga tabel runtime tersedia.
            try
            {
                context.Database.Migrate();
            }
            catch (Exception migrateEx)
            {
                var migrationLogger = services.GetService<ILogger<Program>>();
                migrationLogger?.LogWarning(migrateEx, "EF migration skipped; continuing with idempotent PostgreSQL bootstrap.");
            }

            var logger = services.GetService<ILogger<Program>>();
            if (logger != null)
                logger.LogInformation("PostgreSQL database initialization completed successfully.");
        }
        else
        {
            try
            {
                // Fallback untuk provider non-PostgreSQL jika masih dipakai di environment lama.
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
// PIPELINE HTTP
// ========================================

app.UseAuthentication();

if (builder.Configuration.GetValue<bool>("Performance:EnableCompressionResponse"))
{
    app.UseResponseCompression();
}

// Header keamanan dasar untuk mengurangi risiko clickjacking, MIME sniffing, dan referrer leak.
app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Referrer-Policy", "no-referrer");
    context.Response.Headers.TryAdd("X-XSS-Protection", "0");

    if (!app.Environment.IsDevelopment())
    {
        context.Response.Headers.TryAdd(
            "Content-Security-Policy",
            "default-src 'self'; script-src 'self' https://cdn.jsdelivr.net 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self' ws: wss: https://cdn.jsdelivr.net; frame-ancestors 'none';");
    }

    await next();
});

// Request logging lama bisa mengganggu Content-Length di Docker.
// Karena itu hanya dinyalakan jika config Logging:EnableRequestLogging=true.
if (builder.Configuration.GetValue<bool>("Logging:EnableRequestLogging"))
{
    app.UseMiddleware<RequestLoggingMiddleware>();
}

// Development menampilkan detail exception; production memakai handler standar.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();

    // Docker lokal berjalan di HTTP port 8888, jadi redirect HTTPS dibuat configurable.
    if (builder.Configuration.GetValue<bool>("Https:EnableRedirection", true))
    {
        app.UseHttpsRedirection();
    }
}

// CORS harus berada sebelum endpoint agar request frontend dan SignalR diterima.
app.UseCors("ConfiguredCors");

// Static files untuk asset dashboard/monitoring.
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

// Endpoint utama API, health check Docker, dan hub monitoring.
app.MapControllers();
app.MapHealthChecks("/health");
app.MapHub<MonitoringHub>("/hubs/monitoring");

// Swagger hanya untuk development agar production tetap ringkas.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "JIFAS AI Assistant API v1.0");
        c.RoutePrefix = "swagger";
        c.DocumentTitle = "JIFAS AI Assistant API Documentation";
    });
}

// Endpoint informasi sederhana untuk cek cepat dari browser.
app.MapGet("/", () => new 
{ 
    message = "JIFAS AI Assistant API v1.0",
    status = "running",
    documentation = "/swagger"
});

app.MapGet("/api", () => new 
{ 
    message = "JIFAS AI Assistant API v1.0",
    status = "running",
    version = "1.0",
    documentation = "/swagger"
});

// Handler error production yang tidak membocorkan stack trace atau secret configuration.
app.Map("/error", (HttpContext httpContext) =>
{
    var exceptionFeature = httpContext.Features.Get<IExceptionHandlerFeature>();
    var logger = httpContext.RequestServices.GetRequiredService<ILogger<Program>>();

    if (exceptionFeature?.Error != null)
    {
        logger.LogError(exceptionFeature.Error, "Unhandled exception. TraceId: {TraceId}", httpContext.TraceIdentifier);
    }

    return Results.Problem(
        title: "Terjadi kesalahan pada server.",
        detail: "Silakan coba lagi. Jika masalah berulang, hubungi IT Help Desk JIFAS dengan Trace ID ini.",
        statusCode: StatusCodes.Status500InternalServerError,
        extensions: new Dictionary<string, object?>
        {
            ["traceId"] = httpContext.TraceIdentifier
        });
});

// ========================================
// RUN APP
// ========================================

app.Run();

static void ConfigureJifasDbContext(DbContextOptionsBuilder options, IConfiguration configuration)
{
    var connectionString = configuration["ConnectionStrings:DefaultConnection"];
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

    options.UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.UseVector());
}

static void ExecutePostgresBootstrapScript(JIFAS_AssistantContext context)
{
    var candidatePaths = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "Database", "Initialize-PostgresPgvector.sql"),
        Path.Combine(Directory.GetCurrentDirectory(), "Database", "Initialize-PostgresPgvector.sql"),
        Path.Combine(Directory.GetCurrentDirectory(), "Jifas.Assistant", "Database", "Initialize-PostgresPgvector.sql")
    };

    var scriptPath = candidatePaths.FirstOrDefault(File.Exists);
    if (string.IsNullOrWhiteSpace(scriptPath))
        throw new FileNotFoundException("PostgreSQL bootstrap script not found.", "Initialize-PostgresPgvector.sql");

    var sql = File.ReadAllText(scriptPath);
    context.Database.ExecuteSqlRaw(sql);
}
