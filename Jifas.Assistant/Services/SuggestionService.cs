using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Jifas.Assistant.Services
{
    public interface ISuggestionService
    {
        Task<List<string>> GenerateSuggestionsAsync(string userQuery, string response);
    }

    /// <summary>
    /// Generates context-aware follow-up suggestions via Ollama AI.
    /// Falls back to keyword-based suggestions only when Ollama is unavailable.
    /// </summary>
    public class SuggestionService : ISuggestionService
    {
        private readonly IOllamaService _ollamaService;
        private readonly ILoggerService _logger;

        // Topic-based fallback — only used when Ollama is unavailable
        private static readonly Dictionary<string, List<string>> FallbackByTopic = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Invoice"] = new() { "Bagaimana cara approve invoice di JIFAS?", "Apa saja status invoice yang ada?", "Bagaimana cara melihat history invoice?" },
            ["Payment"] = new() { "Bagaimana cara membuat payment request?", "Bagaimana proses approval payment?", "Apa saja metode pembayaran yang tersedia?" },
            ["PUM"] = new() { "Bagaimana cara submit PUM?", "Bagaimana cara settlement PUM?", "Siapa yang bisa approve PUM?" },
            ["Budget"] = new() { "Bagaimana cara melihat sisa budget?", "Bagaimana cara mengajukan revisi budget?", "Apa yang terjadi jika over budget?" },
            ["Approval"] = new() { "Siapa saja approver di alur approval?", "Bagaimana cara reject approval?", "Bagaimana cara delegasi approval?" },
            ["GL"] = new() { "Bagaimana cara membuat jurnal manual?", "Bagaimana cara posting di JIFAS?", "Bagaimana cara melihat trial balance?" },
            ["AP"] = new() { "Bagaimana proses matching PO dengan invoice?", "Bagaimana cara melihat outstanding AP?", "Bagaimana cara payment ke vendor?" },
            ["AR"] = new() { "Bagaimana cara melihat outstanding AR?", "Bagaimana cara proses pembayaran dari customer?", "Bagaimana laporan aging AR?" },
            ["Receiving"] = new() { "Bagaimana cara membuat Receiving Voucher?", "Apa yang dilakukan jika barang tidak sesuai PO?", "Siapa yang berwenang approve RV?" },
            ["Report"] = new() { "Bagaimana cara generate laporan keuangan?", "Bagaimana cara lihat cashflow harian?", "Bagaimana cara export laporan?" },
        };

        public SuggestionService(IOllamaService ollamaService, ILoggerService logger)
        {
            _ollamaService = ollamaService;
            _logger = logger;
        }

        public async Task<List<string>> GenerateSuggestionsAsync(string userQuery, string response)
        {
            try
            {
                _logger.LogDebug("[SuggestionService] Generating AI suggestions for: {0}", userQuery);
                var suggestions = await _ollamaService.GenerateSuggestionsAsync(userQuery, response);

                if (suggestions != null && suggestions.Count > 0)
                {
                    _logger.LogInformation("[SuggestionService] AI generated {0} suggestions", suggestions.Count);
                    return suggestions.Take(3).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[SuggestionService] AI suggestions unavailable, using fallback: {0}", ex.Message);
            }

            // Fallback: topic-based static suggestions
            return GetFallbackSuggestions(userQuery + " " + response);
        }

        private List<string> GetFallbackSuggestions(string combined)
        {
            var t = combined.ToLowerInvariant();

            string topic =
                t.Contains("invoice") || t.Contains("faktur") ? (t.Contains("vendor") || t.Contains(" ap ") ? "AP" : "Invoice") :
                t.Contains("payment") || t.Contains("pembayaran") ? "Payment" :
                t.Contains("pum") || t.Contains("uang muka") ? "PUM" :
                t.Contains("budget") || t.Contains("anggaran") ? "Budget" :
                t.Contains("approval") || t.Contains("approve") ? "Approval" :
                t.Contains("jurnal") || t.Contains(" gl ") || t.Contains("ledger") ? "GL" :
                t.Contains("receiving") || t.Contains(" rv ") ? "Receiving" :
                t.Contains("laporan") || t.Contains("report") || t.Contains("cashflow") ? "Report" :
                t.Contains("hutang") || t.Contains(" ap ") ? "AP" :
                t.Contains("piutang") || t.Contains(" ar ") ? "AR" : "Invoice";

            return FallbackByTopic.TryGetValue(topic, out var list)
                ? list.Take(3).ToList()
                : new List<string>
                {
                    "Bagaimana cara menggunakan fitur ini di JIFAS?",
                    "Apa saja langkah approval yang diperlukan?",
                    "Bagaimana cara melihat laporan terkait?"
                };
        }
    }
}
