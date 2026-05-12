using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System;

namespace Jifas.Assistant.Configuration
{
    // ========================================
    // CONFIGURATION MODELS (Strongly Typed)
    // ========================================

    /// <summary>
    /// Ollama Local AI Settings
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
    /// OpenAI API Settings (Optional)
    /// </summary>
    public class OpenAISettings
    {
        public string ApiKey { get; set; }
    }

    /// <summary>
    /// Azure OpenAI Settings (Optional)
    /// </summary>
    public class AzureOpenAISettings
    {
        public string Endpoint { get; set; }
        public string Key { get; set; }
    }

    /// <summary>
    /// Support Configuration
    /// </summary>
    public class SupportSettings
    {
        public string HelpDeskEmail { get; set; }
        public string HelpDeskPhone { get; set; }
        public string Department { get; set; }
    }

    /// <summary>
    /// Suggestion Configuration
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
    /// Knowledge Base Configuration
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
    /// Chat Configuration
    /// </summary>
    public class ChatSettings
    {
        public string DefaultErrorMessage { get; set; }
        public string EmptyMessageError { get; set; }
        public string NoKBMatchMessage { get; set; }
        public string OutOfScopeMessage { get; set; }
    }

    /// <summary>
    /// Caching Configuration
    /// </summary>
    public class CachingSettings
    {
        public int DefaultDurationMinutes { get; set; }
        public int ResponseDurationHours { get; set; }
        public bool EnableKBCache { get; set; }
        public bool EnableResponseCache { get; set; }
        public int KBDocumentCacheDurationMinutes { get; set; }
        public int KBSearchCacheDurationMinutes { get; set; }
        public int ResponseCacheDurationHours { get; set; }
    }

    /// <summary>
    /// API Configuration
    /// </summary>
    public class ApiSettings
    {
        public int RequestTimeout { get; set; }
        public int MaxRequestBodySize { get; set; }
    }

    /// <summary>
    /// Qdrant Vector Database Configuration
    /// </summary>
    public class QdrantSettings
    {
        public bool Enabled { get; set; }
        public string Url { get; set; }
        public string CollectionName { get; set; }
        public string ApiKey { get; set; }
        public int EmbeddingDimensions { get; set; }
    }

    /// <summary>
    /// Search Configuration
    /// </summary>
    public class SearchSettings
    {
        public bool UseQdrant { get; set; }
        public int QdrantTopK { get; set; }
    }

    /// <summary>
    /// Metrics Configuration
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
    /// Health Check Configuration
    /// </summary>
    public class HealthCheckSettings
    {
        public bool EnableDetailedStatus { get; set; }
        public int CheckInterval { get; set; }
    }

    /// <summary>
    /// Performance Configuration
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
    /// Optimization Configuration
    /// </summary>
    public class OptimizationSettings
    {
        public bool EnableOption1ExpandedCache { get; set; }
    }

    // ========================================
    // CONFIGURATION PROVIDER HELPER
    // ========================================

    /// <summary>
    /// Helper class untuk access configuration dengan mudah
    /// </summary>
    public class AppSettings
    {
        private readonly IConfiguration _configuration;

        public AppSettings(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// Get Ollama Settings
        /// Usage: var model = settings.Ollama.Model;
        /// </summary>
        public OllamaSettings Ollama => _configuration.GetSection("Ollama").Get<OllamaSettings>() ?? new OllamaSettings();

        /// <summary>
        /// Get OpenAI Settings (Optional)
        /// </summary>
        public OpenAISettings OpenAI => _configuration.GetSection("OpenAI").Get<OpenAISettings>() ?? new OpenAISettings();

        /// <summary>
        /// Get Azure OpenAI Settings (Optional)
        /// </summary>
        public AzureOpenAISettings AzureOpenAI => _configuration.GetSection("Azure:OpenAI").Get<AzureOpenAISettings>() ?? new AzureOpenAISettings();

        /// <summary>
        /// Get Support Settings
        /// </summary>
        public SupportSettings Support => _configuration.GetSection("Support").Get<SupportSettings>() ?? new SupportSettings();

        /// <summary>
        /// Get Suggestion Settings
        /// </summary>
        public SuggestionSettings Suggestion => _configuration.GetSection("Suggestion").Get<SuggestionSettings>() ?? new SuggestionSettings();

        /// <summary>
        /// Get Knowledge Base Settings
        /// </summary>
        public KnowledgeBaseSettings KnowledgeBase => _configuration.GetSection("KnowledgeBase").Get<KnowledgeBaseSettings>() ?? new KnowledgeBaseSettings();

        /// <summary>
        /// Get Chat Settings
        /// </summary>
        public ChatSettings Chat => _configuration.GetSection("Chat").Get<ChatSettings>() ?? new ChatSettings();

        /// <summary>
        /// Get Caching Settings
        /// </summary>
        public CachingSettings Caching => _configuration.GetSection("Caching").Get<CachingSettings>() ?? new CachingSettings();

        /// <summary>
        /// Get API Settings
        /// </summary>
        public ApiSettings API => _configuration.GetSection("API").Get<ApiSettings>() ?? new ApiSettings();

        /// <summary>
        /// Get Qdrant Settings
        /// </summary>
        public QdrantSettings Qdrant => _configuration.GetSection("Qdrant").Get<QdrantSettings>() ?? new QdrantSettings();

        /// <summary>
        /// Get Search Settings
        /// </summary>
        public SearchSettings Search => _configuration.GetSection("Search").Get<SearchSettings>() ?? new SearchSettings();

        /// <summary>
        /// Get Metrics Settings
        /// </summary>
        public MetricsSettings Metrics => _configuration.GetSection("Metrics").Get<MetricsSettings>() ?? new MetricsSettings();

        /// <summary>
        /// Get Health Check Settings
        /// </summary>
        public HealthCheckSettings HealthCheck => _configuration.GetSection("HealthCheck").Get<HealthCheckSettings>() ?? new HealthCheckSettings();

        /// <summary>
        /// Get Performance Settings
        /// </summary>
        public PerformanceSettings Performance => _configuration.GetSection("Performance").Get<PerformanceSettings>() ?? new PerformanceSettings();

        /// <summary>
        /// Get Optimization Settings
        /// </summary>
        public OptimizationSettings Optimization => _configuration.GetSection("Optimization").Get<OptimizationSettings>() ?? new OptimizationSettings();

        /// <summary>
        /// Get Connection String
        /// </summary>
        public string DefaultConnection => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
    }
}
