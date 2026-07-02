using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jifas.Assistant.Models;

namespace Jifas.Assistant.Services
{
    public class ScopeCheckResult
    {
        public bool IsInScope { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Result dari pre-gate scope check.
    /// </summary>
    public class ScopePreGateResult
    {
        /// <summary>True jika pertanyaan di-hard-block (langsung return OOS, skip pipeline).</summary>
        public bool IsHardBlocked { get; set; }
        /// <summary>Alasan block (kosong jika tidak diblok).</summary>
        public string Reason { get; set; } = string.Empty;
        /// <summary>True jika ada sinyal JIFAS (activeModule atau istilah JIFAS terdeteksi).</summary>
        public bool HasJifasSignal { get; set; }
        /// <summary>True jika lolos semua cek dan harus diteruskan ke LLM pipeline.</summary>
        public bool PassThrough { get; set; }
    }

    public interface IOutOfScopeDetector
    {
        Task<ScopeCheckResult> CheckScopeAsync(string userQuery);

        /// <summary>
        /// Pre-gate scope evaluation: HIGH PRECISION, LOW RECALL.
        /// 1. ESCAPE jika ada sinyal JIFAS (activeModule atau istilah JIFAS).
        /// 2. HARD-BLOCK jika topik OOS jelas DAN tidak ada sinyal JIFAS.
        /// 3. PASS THROUGH untuk sisanya (nuansa/ambigu -> LLM pipeline).
        /// Tidak memanggil Ollama.
        /// </summary>
        ScopePreGateResult EvaluatePreGate(string userQuery, ChatRequest? request);
    }

    /// <summary>
    /// Detects out-of-scope queries using a two-stage approach:
    /// 1. Fast keyword pre-filter (no API call) to catch obvious off-topic queries
    /// 2. Ollama AI for ambiguous cases — tight binary prompt (minimal tokens)
    /// In-scope by default to avoid blocking legitimate JIFAS questions.
    /// </summary>
    public class OutOfScopeDetector : IOutOfScopeDetector
    {
        // =====================================================================
        // CENTRALIZED TERM REGISTRY
        // =====================================================================
        // CATATAN: Daftar ini HANYA untuk pre-gate fast-path scope detection.
        // scope rules utama tetap di prompt Ollama (OllamaAIService.cs baris ~118-121).
        // Jangan tambahkan istilah teknis IT umum (websocket/API/OAuth/graphQL/dll)
        // ke daftar OOS di sini — sudah ditangani rule teknis Ollama.
        // Kalau ragu antara OOS vs ambigu -> JANGAN blok, teruskan ke LLM.
        // =====================================================================

        /// <summary>
        /// Istilah dan sinyal yang PASTI menandakan konteks JIFAS.
        /// Pre-gate: jika terdeteksi, langsung ESCAPE (lolos ke LLM pipeline).
        /// fast-path only — BUKAN satu-satunya sumber kebenaran scope.
        /// Catatan: HANYA istilah yang jelas-jelas JIFAS. Hindari istilah umum
        /// yang bisa match di luar konteks (mis. "di" = preposisi Indonesia,
        /// "bank" = bank umum). Gunakan frase multi-kata untuk presisi.
        /// </summary>
        private static readonly string[] JifasSignalTerms =
        {
            // Nama sistem
            "jifas",
            // Invoice & billing
            "invoice", "faktur", "billing",
            // Payment & kas
            "payment", "pembayaran", "pum", "uang muka",
            "cashbank", "cash bank", "cash-flow", "cash flow",
            // Receiving
            "receiving", "receive", "rv", "grpo",
            // Accounting
            "gl", "general ledger", "account payable", "account receivable",
            "coa", "chart of account", "jurnal", "journal",
            // Budget
            "budget", "anggaran",
            // Master data
            "vendor", "supplier", "customer", "supplier code",
            // Approval & workflow
            "approval", "approve", "reject", "head approval", "finance approval",
            "tax approval", "settle", "realisasi", "submission",
            // Module names
            "home", "dashboard", "purchase", "sales", "inventory", "fixed asset",
            // Navigation / page context (frasa multi-kata agar presisi)
            "halaman ini", "halaman apa", "page ini", "di sini", "sedang di",
            "menu ini", "modul ini", "fitur ini", "fungsi halaman", "apa yang bisa",
            // Transaction history
            "history transaksi", "riwayat transaksi", "history pembayaran", "riwayat pembayaran",
            "history invoice", "riwayat invoice", "history pum", "riwayat pum",
            // Reports
            "laporan keuangan", "trial balance", "neraca", "laba rugi", "pnl",
            "balance sheet", "profit loss", "cash flow report",
            // User & access
            "login jifas", "akses jifas", "password jifas", "username jifas",
            "role jifas", "hak akses", "role-based",
            // Company/org
            "company", "divisi", "departemen", "company code", "comp code",
            // Financial operations
            "overbudget", "over budget", "void", "cancel", "submit",
            "posting", "journal entry", "intercompany", "interco",
            // Tax & treasury
            "ppn", "pajak", "tax", "treasury", "deposito", "bank statement",
            // Employee & payroll
            "payroll", "gaji", "employee", "dinas",
            // Document types
            "spk", "surat jalan", "delivery order",
            "tb", "bs", "caje", "paje"
        };

        /// <summary>
        /// Topik OOS yang sangat jelas & tidak ambigu.
        /// Pre-gate: jika terdeteksi DAN TIDAK ada sinyal JIFAS -> hard-block.
        /// fast-path only — BUKAN satu-satunya sumber kebenaran scope.
        /// </summary>
        private static readonly string[] OosFastPathTerms =
        {
            "cuaca", "weather", "hari ini panas",
            "berita", "news", "politik", "pemilu",
            "agama", "sholat", "puasa", "ibadah", "doa",
            "resep", "masak", "makanan", "kuliner",
            "film", "musik", "lagu", "game", "olahraga", "sepakbola", "basket",
            "crypto", "bitcoin", "saham", "forex", "investasi pribadi",
            "medis", "dokter", "rumah sakit", "obat", "penyakit", "covid", "suhu",
            "wisata", "hotel", "penerbangan", "travel",
            "programming tutorial", "belajar python", "belajar javascript",
            "ghibah", "gosip", "artis", "selebritis",
            "rumus matematika", "soal matematika", "tutorial excel",
            "cara membuat website", "belajar coding"
        };
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

                _logger.LogInformation("[OutOfScopeDetector] AI scope check: '{0}' => {1} (AI: {2})", userQuery, isInScope ? "InScope" : "OutOfScope", result?.Trim() ?? string.Empty);

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

        /// <summary>
        /// Pre-gate: cek apakah ada sinyal konteks JIFAS.
        /// Escape condition: activeModule terisi ATAU pesan mengandung istilah JIFAS.
        /// </summary>
        public bool HasJifasSignal(string userQuery, ChatRequest? request)
        {
            // Escape: activeModule terisi -> user sedang di halaman JIFAS
            if (!string.IsNullOrWhiteSpace(request?.Context?.ActiveModule))
                return true;

            // Escape: currentModule terisi
            if (!string.IsNullOrWhiteSpace(request?.CurrentModule))
                return true;

            if (string.IsNullOrWhiteSpace(userQuery))
                return false;

            var queryLower = userQuery.ToLowerInvariant();

            // Escape: istilah JIFAS terdeteksi dalam pesan
            return JifasSignalTerms.Any(term => queryLower.Contains(term));
        }

        /// <summary>
        /// Pre-gate: coba hard-block topik OOS yang sangat jelas.
        /// HARUS dipanggil SETELAH HasJifasSignal() returned false.
        /// </summary>
        private bool TryHardBlock(string userQuery, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(userQuery))
                return false;

            var queryLower = userQuery.ToLowerInvariant();

            foreach (var term in OosFastPathTerms)
            {
                if (queryLower.Contains(term))
                {
                    reason = $"OOS fast-path keyword: {term}";
                    _logger.LogInformation("[ScopePreGate] Hard-block matched: {0}", reason);
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public ScopePreGateResult EvaluatePreGate(string userQuery, ChatRequest? request)
        {
            var result = new ScopePreGateResult();

            // Step 1: ESCAPE — sinyal JIFAS terdeteksi -> lolos ke LLM pipeline
            if (HasJifasSignal(userQuery, request))
            {
                result.HasJifasSignal = true;
                result.PassThrough = true;
                _logger.LogDebug("[ScopePreGate] ESCAPE: JIFAS signal detected, pass to LLM");
                return result;
            }

            // Step 2: HARD-BLOCK — topik OOS jelas tanpa sinyal JIFAS
            if (TryHardBlock(userQuery, out var reason))
            {
                result.IsHardBlocked = true;
                result.Reason = reason;
                _logger.LogInformation("[ScopePreGate] HARD-BLOCK: {0}", reason);
                return result;
            }

            // Step 3: PASS THROUGH — tidak escape, tidak block -> LLM pipeline
            result.PassThrough = true;
            _logger.LogDebug("[ScopePreGate] PASS: ambiguous/no-match, continue to LLM pipeline");
            return result;
        }
    }
}
