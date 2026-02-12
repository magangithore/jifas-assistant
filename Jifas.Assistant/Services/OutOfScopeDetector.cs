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
                    return new ScopeCheckResult
                    {
                        IsInScope = false,
                        Message = "Pertanyaan Anda tidak berkaitan dengan JIFAS. Silakan ajukan pertanyaan tentang Jababeka Integrated Finance Accounting System (JIFAS)."
                    };
                }

                return new ScopeCheckResult
                {
                    IsInScope = true,
                    Message = "Query in scope"
                };
            }
            catch (System.Exception ex)
            {
                _logger.LogError($"[OutOfScopeDetector] Error: {ex.Message}");
                // Default to in-scope on error
                return new ScopeCheckResult { IsInScope = true, Message = "Query accepted" };
            }
        }
    }
}
