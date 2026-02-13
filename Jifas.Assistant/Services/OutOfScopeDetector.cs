using System;
using System.Threading.Tasks;

namespace Jifas.Assistant.Services
{
    public class ScopeCheckResult
    {
        public bool IsInScope { get; set; }
        public string Message { get; set; }
    }

    public interface IOutOfScopeDetector
    {
        Task<ScopeCheckResult> CheckScopeAsync(string userQuery);
    }

    public class OutOfScopeDetector : IOutOfScopeDetector
    {
        private readonly IGeminiService _geminiService;
        private readonly ILoggerService _logger;

        public OutOfScopeDetector(IGeminiService geminiService, ILoggerService logger)
        {
            _geminiService = geminiService;
            _logger = logger;
        }

        public async Task<ScopeCheckResult> CheckScopeAsync(string userQuery)
        {
            try
            {
                var isInScope = await _geminiService.IsInScopeAsync(userQuery);

                if (!isInScope)
                {
                    // Generate natural out-of-scope response using Gemini
                    var outOfScopeMessage = await GenerateOutOfScopeResponseAsync(userQuery);
                    
                    return new ScopeCheckResult
                    {
                        IsInScope = false,
                        Message = outOfScopeMessage
                    };
                }

                return new ScopeCheckResult
                {
                    IsInScope = true,
                    Message = "Query in scope"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"[OutOfScopeDetector] Error: {ex.Message}");
                // Default to in-scope on error to avoid blocking legitimate queries
                return new ScopeCheckResult { IsInScope = true, Message = "Query accepted" };
            }
        }

        /// <summary>
        /// Generate natural out-of-scope response using Gemini
        /// Politely redirect user to JIFAS topics
        /// </summary>
        private async Task<string> GenerateOutOfScopeResponseAsync(string userQuery)
        {
            try
            {
                var prompt = $@"Pengguna menanyakan hal yang di luar scope sistem JIFAS (Jababeka Integrated Finance Accounting System).

Pertanyaan user: ""{userQuery}""

Buatlah respons yang:
1. Sopan dan ramah
2. Jelaskan bahwa pertanyaan tidak berkaitan dengan JIFAS
3. Sebutkan topik JIFAS yang bisa Anda jawab (AR, AP, GL, Budget, PUM, Master Data, Reporting)
4. Arahkan user untuk bertanya tentang JIFAS
5. Gunakan bahasa Indonesia profesional
6. Singkat, jelas, dan mudah dipahami (max 2-3 kalimat)

Buatlah respons langsung tanpa penjelasan tambahan.";

                var response = await _geminiService.CallGeminiApiAsync(prompt);
                _logger.LogInformation("[OutOfScopeDetector] Generated out-of-scope response");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[OutOfScopeDetector] Error generating out-of-scope response: {ex.Message}");
                // Fallback to default message on error
                return "Maaf, pertanyaan Anda tidak berkaitan dengan JIFAS. Saya hanya dapat menjawab pertanyaan tentang sistem JIFAS. Silakan tanyakan tentang AR, AP, GL, Budget, PUM, atau modul lainnya.";
            }
        }
    }
}

