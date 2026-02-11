using Jifas.Assistant.Configuration;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace Jifas.Assistant.Services.Examples
{
    /// <summary>
    /// CONTOH: Bagaimana mengakses configuration di service/controller
    /// </summary>
    public class ExampleConfigurationUsage
    {
        // ========================================
        // METHOD 1: Menggunakan IOptions<T> (Recommended untuk single config section)
        // ========================================

        public class GeminiServiceExample
        {
            private readonly IOptions<GeminiSettings> _geminiSettings;

            public GeminiServiceExample(IOptions<GeminiSettings> geminiSettings)
            {
                _geminiSettings = geminiSettings;
            }

            public async Task<string> CallGeminiAPI(string prompt)
            {
                var apiKey = _geminiSettings.Value.ApiKey;
                var model = _geminiSettings.Value.Model;
                var baseUrl = _geminiSettings.Value.BaseUrl;

                Console.WriteLine($"Using Gemini Model: {model}");
                Console.WriteLine($"API URL: {baseUrl}");

                // Call Gemini API dengan credentials dari configuration
                // var response = await CallAPI(apiKey, model, prompt);
                // return response;

                return "Response dari Gemini";
            }
        }

        // ========================================
        // METHOD 2: Menggunakan IConfiguration (Direct access)
        // ========================================

        public class KnowledgeBaseServiceExample
        {
            private readonly IConfiguration _configuration;

            public KnowledgeBaseServiceExample(IConfiguration configuration)
            {
                _configuration = configuration;
            }

            public int GetMaxDocumentsPerSearch()
            {
                // Direct access ke configuration
                var maxDocs = _configuration.GetValue<int>("KnowledgeBase:MaxDocumentsPerSearch");
                return maxDocs;
            }

            public string GetQdrantUrl()
            {
                return _configuration["Qdrant:Url"];
            }
        }

        // ========================================
        // METHOD 3: Menggunakan AppSettings Helper (Most Convenient)
        // ========================================

        public class ChatServiceExample
        {
            private readonly AppSettings _appSettings;

            public ChatServiceExample(AppSettings appSettings)
            {
                _appSettings = appSettings;
            }

            public string GetErrorMessage()
            {
                // Easy access ke semua configurations
                return _appSettings.Chat.DefaultErrorMessage;
            }

            public int GetCacheDuration()
            {
                return _appSettings.Caching.DefaultDurationMinutes;
            }

            public bool IsQdrantEnabled()
            {
                return _appSettings.Qdrant.Enabled;
            }

            public string GetSupportEmail()
            {
                return _appSettings.Support.HelpDeskEmail;
            }
        }

        // ========================================
        // METHOD 4: Menggunakan IOptionsSnapshot<T> (Reload tanpa restart)
        // ========================================

        public class DynamicConfigService
        {
            private readonly IOptionsSnapshot<PerformanceSettings> _performanceSettings;

            public DynamicConfigService(IOptionsSnapshot<PerformanceSettings> performanceSettings)
            {
                _performanceSettings = performanceSettings;
            }

            public bool IsSlowOperation(int operationTimeMs)
            {
                // Configuration bisa berubah tanpa restart app
                var threshold = _performanceSettings.Value.SlowOperationThresholdMs;
                return operationTimeMs > threshold;
            }
        }

        // ========================================
        // USAGE EXAMPLES DI CONTROLLER
        // ========================================

        public class ExampleController
        {
            private readonly AppSettings _appSettings;

            public ExampleController(AppSettings appSettings)
            {
                _appSettings = appSettings;
            }

            public void Examples()
            {
                // ? Example 1: Get Support Information
                Console.WriteLine($"Support Email: {_appSettings.Support.HelpDeskEmail}");
                Console.WriteLine($"Support Phone: {_appSettings.Support.HelpDeskPhone}");

                // ? Example 2: Get Chat Configuration
                if (string.IsNullOrEmpty("user_message"))
                {
                    var emptyError = _appSettings.Chat.EmptyMessageError;
                    // return BadRequest(emptyError);
                }

                // ? Example 3: Get Knowledge Base Settings
                var maxDocs = _appSettings.KnowledgeBase.MaxDocumentsPerSearch;
                var minScore = _appSettings.KnowledgeBase.MinRelevanceScore;
                // var results = await SearchKB(query, maxDocs, minScore);

                // ? Example 4: Check if Qdrant is enabled
                if (_appSettings.Qdrant.Enabled)
                {
                    var qdrantUrl = _appSettings.Qdrant.Url;
                    // await InitializeQdrant(qdrantUrl);
                }

                // ? Example 5: Get Performance Settings
                var slowOpThreshold = _appSettings.Performance.SlowOperationThresholdMs;
                // if (operationTime > slowOpThreshold) LogSlowOperation();

                // ? Example 6: Get Caching Settings
                var cacheExpiry = TimeSpan.FromMinutes(_appSettings.Caching.DefaultDurationMinutes);
                // cache.Set(key, value, cacheExpiry);

                // ? Example 7: Get Suggestion Settings
                var maxSuggestions = _appSettings.Suggestion.MaxSuggestions;
                // var suggestions = GetTopSuggestions(maxSuggestions);

                // ? Example 8: Get Metrics Settings
                if (_appSettings.Metrics.EnableTracking)
                {
                    // TrackUserInteraction();
                }
            }
        }
    }
}
