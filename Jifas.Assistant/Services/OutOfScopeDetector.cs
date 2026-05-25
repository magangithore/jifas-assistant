using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// Detects out-of-scope queries using a two-stage approach:
    /// 1. Fast keyword pre-filter (no API call) to catch obvious off-topic queries
    /// 2. Ollama AI for ambiguous cases — tight binary prompt (minimal tokens)
    /// In-scope by default to avoid blocking legitimate JIFAS questions.
    /// </summary>
    public class OutOfScopeDetector : IOutOfScopeDetector
    {
        private readonly IOllamaService _ollamaService;
        private readonly ILoggerService _logger;

        // Obvious non-JIFAS topics — catch immediately, no Ollama call needed
        private static readonly string[] ObviousOutOfScopeKeywords =
        {
            "cuaca", "weather", "hari ini panas", "berita", "news", "politik", "pemilu",
            "agama", "sholat", "puasa", "ibadah", "doa",
            "resep", "masak", "makanan", "kuliner",
            "film", "musik", "lagu", "game", "olahraga", "sepakbola", "basket",
            "crypto", "bitcoin", "saham", "forex", "investasi pribadi",
            "medis", "dokter", "obat", "penyakit", "covid",
            "wisata", "hotel", "penerbangan", "travel",
            "programming tutorial", "belajar python", "belajar javascript",
            "ghibah", "gosip", "artis", "selebritis"
        };

        // Obvious JIFAS-related signals — always in scope, skip AI check
        private static readonly string[] ObviousInScopeKeywords =
        {
            "jifas", "invoice", "faktur", "payment", "pembayaran", "pum", "uang muka",
            "receiving", "rv", "receive", "approval", "approve", "reject", "posting",
            "gl", "general ledger", "ap ", " ar ", "account payable", "account receivable",
            "budget", "anggaran", "vendor", "supplier", "coa", "chart of account",
            "jurnal", "journal", "laporan keuangan", "trial balance", "neraca",
            "cashflow", "kas", "bank", "deposito", "treasury", "inquiry",
            "login jifas", "akses jifas", "password", "username", "role",
            "finance", "accounting", "divisi", "departemen", "company",
            "overbudget", "over budget", "void", "cancel", "submit", "save",
            "head approval", "finance approval", "tax approval", "settle", "realisasi",
            // Context/navigation questions — user is on a JIFAS page
            "halaman ini", "halaman apa", "page ini", "di sini", "sedang di",
            "menu ini", "modul ini", "fitur ini", "fungsi halaman", "apa yang bisa",
            // History/transaction queries — always JIFAS context
            "history transaksi", "riwayat transaksi", "history pembayaran", "riwayat pembayaran",
            "history invoice", "riwayat invoice", "history pum", "riwayat pum"
        };

        public OutOfScopeDetector(IOllamaService ollamaService, ILoggerService logger)
        {
            _ollamaService = ollamaService;
            _logger = logger;
        }

        public async Task<ScopeCheckResult> CheckScopeAsync(string userQuery)
        {
            if (string.IsNullOrWhiteSpace(userQuery))
                return new ScopeCheckResult { IsInScope = false, Message = "Pertanyaan tidak boleh kosong." };

            var queryLower = userQuery.ToLowerInvariant();

            // Stage 1a: Obvious in-scope → skip any further check
            if (ObviousInScopeKeywords.Any(k => queryLower.Contains(k)))
            {
                _logger.LogDebug("[OutOfScopeDetector] Fast in-scope: {0}", userQuery);
                return new ScopeCheckResult { IsInScope = true };
            }

            // Stage 1b: Obvious out-of-scope → return immediately, no API call
            if (ObviousOutOfScopeKeywords.Any(k => queryLower.Contains(k)))
            {
                _logger.LogInformation("[OutOfScopeDetector] Keyword out-of-scope: {0}", userQuery);
                return new ScopeCheckResult
                {
                    IsInScope = false,
                    Message = BuildOutOfScopeMessage(userQuery)
                };
            }

            // Stage 2: Ambiguous → ask Ollama with minimal prompt
            try
            {
                var scopePrompt = $@"Sistem JIFAS adalah aplikasi ERP keuangan perusahaan (invoice, payment, AP, AR, GL, budget, PUM, receiving, laporan keuangan, akuntansi, payroll, vendor, approval workflow).

Pertanyaan: ""{userQuery}""

Apakah pertanyaan ini relevan dengan sistem JIFAS atau keuangan/akuntansi/operasional bisnis perusahaan?
Jawab SATU KATA: Ya atau Tidak";

                var result = await _ollamaService.CallOllamaApiAsync(scopePrompt);
                var isInScope = !(result?.Trim().StartsWith("Tidak", StringComparison.OrdinalIgnoreCase) == true);

                _logger.LogInformation("[OutOfScopeDetector] AI scope check: '{0}' → {1} (AI: {2})", userQuery, isInScope ? "InScope" : "OutOfScope", result?.Trim());

                if (!isInScope)
                    return new ScopeCheckResult { IsInScope = false, Message = BuildOutOfScopeMessage(userQuery) };

                return new ScopeCheckResult { IsInScope = true };
            }
            catch (Exception ex)
            {
                // Default in-scope on error — better to attempt answering than to block
                _logger.LogWarning("[OutOfScopeDetector] AI check failed, defaulting in-scope: {0}", ex.Message);
                return new ScopeCheckResult { IsInScope = true };
            }
        }

        private static string BuildOutOfScopeMessage(string userQuery) =>
            $"Maaf, pertanyaan tentang \"{userQuery}\" di luar cakupan JIFAS AI Assistant. " +
            "Saya dirancang khusus untuk membantu hal-hal terkait sistem JIFAS — " +
            "Invoice, Payment, PUM, Receiving, Budget, Accounting (GL/AP/AR), Laporan Keuangan, dan proses approval. " +
            "Ada yang ingin Anda tanyakan tentang JIFAS?";
    }
}
