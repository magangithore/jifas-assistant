using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System;

namespace Jifas.Assistant.Configuration
{
    // ========================================
    // MODEL CONFIGURATION (Strongly Typed)
    // ========================================

    /// <summary>
    /// Konfigurasi LLM lokal dari Ollama.
    /// </summary>
    public class OllamaSettings
    {
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "qwen3:8b";
        public string BaseUrl { get; set; } = "http://10.0.12.54:11434";
        public float Temperature { get; set; } = 0.3f;
        public float TopP { get; set; } = 0.85f;
        public int TopK { get; set; } = 40;
        public int MaxOutputTokens { get; set; } = 2048;
        public int TimeoutSeconds { get; set; } = 180;
    }

    /// <summary>
    /// Konfigurasi OpenAI opsional jika suatu saat provider diganti.
    /// </summary>
    public class OpenAISettings
    {
        public string ApiKey { get; set; } = string.Empty;
    }

    /// <summary>
    /// Konfigurasi Azure OpenAI opsional.
    /// </summary>
    public class AzureOpenAISettings
    {
        public string Endpoint { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
    }

    /// <summary>
    /// Kontak support yang dipakai saat chatbot harus mengarahkan user ke IT.
    /// </summary>
    public class SupportSettings
    {
        public string HelpDeskEmail { get; set; } = string.Empty;
        public string HelpDeskPhone { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
    }

    /// <summary>
    /// Konfigurasi suggestion lama untuk kompatibilitas config.
    /// </summary>
    public class SuggestionSettings
    {
        public int MaxSuggestions { get; set; }
        public int MinLength { get; set; }
        public int MaxLength { get; set; }
        public bool EnableCaching { get; set; }
        public int CacheDurationMinutes { get; set; }
    }

    /// <summary>
    /// Konfigurasi pencarian Knowledge Base.
    /// </summary>
    public class KnowledgeBaseSettings
    {
        public int CacheDurationMinutes { get; set; }
        public int MaxDocumentsPerSearch { get; set; }
        public double MinRelevanceScore { get; set; }
        public bool UseQdrant { get; set; }
        public int QdrantTopK { get; set; }
        public double MinConfidenceScore { get; set; }
        public int TopKResults { get; set; }
    }

    /// <summary>
    /// Template pesan error dan guardrail chatbot.
    /// </summary>
    public class ChatSettings
    {
        public string DefaultErrorMessage { get; set; } = string.Empty;
        public string EmptyMessageError { get; set; } = string.Empty;
        public string NoKBMatchMessage { get; set; } = string.Empty;
        public string OutOfScopeMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Konfigurasi cache jawaban, KB, dan Redis.
    /// </summary>
    public class CachingSettings
    {
        public bool UseRedis { get; set; }
        public string RedisInstanceName { get; set; } = "JIFAS:";
        public int DefaultDurationMinutes { get; set; }
        public int ResponseDurationHours { get; set; }
        public bool EnableKBCache { get; set; }
        public bool EnableResponseCache { get; set; }
        public int KBDocumentCacheDurationMinutes { get; set; }
        public int KBSearchCacheDurationMinutes { get; set; }
        public int ResponseCacheDurationHours { get; set; }
    }

    /// <summary>
    /// Batasan umum request API.
    /// </summary>
    public class ApiSettings
    {
        public int RequestTimeout { get; set; }
        public int MaxRequestBodySize { get; set; }
    }

    /// <summary>
    /// Konfigurasi Qdrant lama. Saat ini search utama memakai PostgreSQL pgvector.
    /// </summary>
    public class QdrantSettings
    {
        public bool Enabled { get; set; }
        public string Url { get; set; } = string.Empty;
        public string CollectionName { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public int EmbeddingDimensions { get; set; }
    }

    /// <summary>
    /// Konfigurasi engine search.
    /// </summary>
    public class SearchSettings
    {
        public bool UseQdrant { get; set; }
        public int QdrantTopK { get; set; }
    }

    /// <summary>
    /// Konfigurasi penyimpanan metrik monitoring.
    /// </summary>
    public class MetricsSettings
    {
        public bool EnableTracking { get; set; }
        public bool TrackSuggestionDisplay { get; set; }
        public bool TrackSuggestionClick { get; set; }
        public bool TrackUserFeedback { get; set; }
        public int CacheDurationMinutes { get; set; }
    }

    /// <summary>
    /// Konfigurasi health check.
    /// </summary>
    public class HealthCheckSettings
    {
        public bool EnableDetailedStatus { get; set; }
        public int CheckInterval { get; set; }
    }

    /// <summary>
    /// Konfigurasi threshold performa aplikasi.
    /// </summary>
    public class PerformanceSettings
    {
        public bool EnableMonitoring { get; set; }
        public int SlowOperationThresholdMs { get; set; }
        public int MaxCacheSize { get; set; }
        public bool EnableCompressionResponse { get; set; }
        public int CompressionThresholdBytes { get; set; }
    }

    /// <summary>
    /// Konfigurasi fitur optimasi eksperimental.
    /// </summary>
    public class OptimizationSettings
    {
        public bool EnableOption1ExpandedCache { get; set; }
    }

    // ========================================
    // HELPER PEMBACA CONFIGURATION
    // ========================================

    /// <summary>
    /// Helper untuk membaca configuration di service yang belum memakai IOptions.
    /// </summary>
    public class AppSettings
    {
        private readonly IConfiguration _configuration;

        public AppSettings(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// Ambil konfigurasi Ollama.
        /// </summary>
        public OllamaSettings Ollama => _configuration.GetSection("Ollama").Get<OllamaSettings>() ?? new OllamaSettings();

        /// <summary>
        /// Ambil konfigurasi OpenAI opsional.
        /// </summary>
        public OpenAISettings OpenAI => _configuration.GetSection("OpenAI").Get<OpenAISettings>() ?? new OpenAISettings();

        /// <summary>
        /// Ambil konfigurasi Azure OpenAI opsional.
        /// </summary>
        public AzureOpenAISettings AzureOpenAI => _configuration.GetSection("Azure:OpenAI").Get<AzureOpenAISettings>() ?? new AzureOpenAISettings();

        /// <summary>
        /// Ambil konfigurasi kontak support.
        /// </summary>
        public SupportSettings Support => _configuration.GetSection("Support").Get<SupportSettings>() ?? new SupportSettings();

        /// <summary>
        /// Ambil konfigurasi suggestion lama.
        /// </summary>
        public SuggestionSettings Suggestion => _configuration.GetSection("Suggestion").Get<SuggestionSettings>() ?? new SuggestionSettings();

        /// <summary>
        /// Ambil konfigurasi Knowledge Base.
        /// </summary>
        public KnowledgeBaseSettings KnowledgeBase => _configuration.GetSection("KnowledgeBase").Get<KnowledgeBaseSettings>() ?? new KnowledgeBaseSettings();

        /// <summary>
        /// Ambil konfigurasi pesan chatbot.
        /// </summary>
        public ChatSettings Chat => _configuration.GetSection("Chat").Get<ChatSettings>() ?? new ChatSettings();

        /// <summary>
        /// Ambil konfigurasi cache.
        /// </summary>
        public CachingSettings Caching => _configuration.GetSection("Caching").Get<CachingSettings>() ?? new CachingSettings();

        /// <summary>
        /// Ambil konfigurasi API.
        /// </summary>
        public ApiSettings API => _configuration.GetSection("API").Get<ApiSettings>() ?? new ApiSettings();

        /// <summary>
        /// Ambil konfigurasi Qdrant lama.
        /// </summary>
        public QdrantSettings Qdrant => _configuration.GetSection("Qdrant").Get<QdrantSettings>() ?? new QdrantSettings();

        /// <summary>
        /// Ambil konfigurasi search.
        /// </summary>
        public SearchSettings Search => _configuration.GetSection("Search").Get<SearchSettings>() ?? new SearchSettings();

        /// <summary>
        /// Ambil konfigurasi metrics.
        /// </summary>
        public MetricsSettings Metrics => _configuration.GetSection("Metrics").Get<MetricsSettings>() ?? new MetricsSettings();

        /// <summary>
        /// Ambil konfigurasi health check.
        /// </summary>
        public HealthCheckSettings HealthCheck => _configuration.GetSection("HealthCheck").Get<HealthCheckSettings>() ?? new HealthCheckSettings();

        /// <summary>
        /// Ambil konfigurasi performance.
        /// </summary>
        public PerformanceSettings Performance => _configuration.GetSection("Performance").Get<PerformanceSettings>() ?? new PerformanceSettings();

        /// <summary>
        /// Ambil konfigurasi optimization.
        /// </summary>
        public OptimizationSettings Optimization => _configuration.GetSection("Optimization").Get<OptimizationSettings>() ?? new OptimizationSettings();

        /// <summary>
        /// Ambil connection string database utama.
        /// </summary>
        public string DefaultConnection => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
    }
}
