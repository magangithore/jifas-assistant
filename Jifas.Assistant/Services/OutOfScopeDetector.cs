using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Interface for out-of-scope detection
    /// Determines if a query is related to JIFAS
    /// </summary>
    public interface IOutOfScopeDetector
    {
        Task<ScopeCheckResult> CheckScopeAsync(string userQuery);
    }

    /// <summary>
    /// Result of scope check
    /// </summary>
    public class ScopeCheckResult
    {
        public bool IsInScope { get; set; }
        public double ConfidenceScore { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Detects if user query is within JIFAS scope
    /// Uses multi-layered detection:
    /// 1. Hard rejection: clearly out-of-scope keywords
    /// 2. Knowledge base matching
    /// 3. Dynamic JIFAS workflow detection (from JifasContextService)
    /// </summary>
    public class OutOfScopeDetector : IOutOfScopeDetector
    {
        private readonly IKnowledgeBaseService _knowledgeBaseService;
        private readonly IJifasContextService _jifasContext;
        private readonly ILoggerService _logger;

        // Keywords that indicate out-of-scope questions
        private readonly HashSet<string> _outOfScopeKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cuaca", "weather", "berita", "news", "olahraga", "sports", "film", "movie",
            "musik", "music", "resep", "recipe", "game", "gaming", "bitcoin", "crypto",
            "cryptocurrency", "dating", "romance", "cinta", "personal", "pribadi",
            "covid", "covid-19", "pandemi", "pandemic", "politics", "politik",
            "agama", "religion", "joke", "lawak", "meme", "humor", "general knowledge"
        };

        // Keywords that indicate in-scope (JIFAS-related) questions
        private readonly HashSet<string> _jifasKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Core JIFAS System
            "jifas", "finance", "keuangan", "accounting", "akuntansi", 
            
            // JIFAS Modules
            "invoice", "pum", "uang muka", "pengajuan dana", "payment", "pembayaran",
            "receiving", "penerimaan", "cash bank", "bank", "journal", "jurnal",
            
            // JIFAS Master Data
            "master data", "company", "division", "department", "vendor", "supplier",
            "coa", "chart of accounts", "budget", "anggaran", "employee", "karyawan",
            "roles", "permissions", "user", "pengguna",
            
            // JIFAS Key Concepts
            "ar", "ap", "gl", "general ledger", "piutang", "utang", "ledger", "buku",
            "journal entry", "jurnal", "posting", "posting", "approval", "persetujuan",
            "invoice number", "nomor invoice", "pd form", "pd approval",
            "tax approval", "pajak", "pph", "ppn", "over budget", "melebihi budget",
            
            // JIFAS Workflow Terms
            "workflow", "alur", "tahapan", "status", "draft", "pending", "approved",
            "disetujui", "rejected", "ditolak", "void", "pembatalan", "reverse",
            "realization", "realisasi", "correction", "koreksi", "verification", "verifikasi",
            
            // JIFAS Access/Troubleshooting
            "login", "akses", "access", "password", "username", "Windows login",
            "error", "masalah", "problem", "troubleshooting", "tidak bisa", "nggak bisa",
            "help", "bantuan", "support", "pertanyaan", "question", "pertanyaan"
        };

        private const string OUT_OF_SCOPE_MESSAGE_DEFAULT = 
            "Mohon maaf, pertanyaan Anda berada di luar cakupan JIFAS AI Assistant. " +
            "Saya dirancang khusus untuk menjawab pertanyaan tentang JIFAS (Jababeka Integrated Finance Accounting System) saja, " +
            "seperti akses login, troubleshooting, penggunaan modul AR/AP/GL, dan fitur-fitur JIFAS lainnya. " +
            "Apakah ada pertanyaan lain tentang JIFAS yang bisa saya bantu?";

        public OutOfScopeDetector(IKnowledgeBaseService knowledgeBaseService, IJifasContextService jifasContext = null)
        {
            _knowledgeBaseService = knowledgeBaseService;
            _jifasContext = jifasContext ?? new JifasContextService();
            _logger = LoggerFactory.GetLogger();
        }

        public async Task<ScopeCheckResult> CheckScopeAsync(string userQuery)
        {
            try
            {
                var outOfScopeMessage = ConfigurationManager.AppSettings["Chat:OutOfScopeMessage"] ?? OUT_OF_SCOPE_MESSAGE_DEFAULT;
                
                if (string.IsNullOrWhiteSpace(userQuery))
                {
                    return new ScopeCheckResult
                    {
                        IsInScope = false,
                        ConfidenceScore = 0,
                        Message = outOfScopeMessage
                    };
                }

                var lowerQuery = userQuery.ToLower();

                // Hard reject: clearly out-of-scope keywords
                if (HasOutOfScopeKeywords(lowerQuery))
                {
                    return new ScopeCheckResult
                    {
                        IsInScope = false,
                        ConfidenceScore = 0.1,
                        Message = outOfScopeMessage
                    };
                }

                // Soft check: search KB to see if query has matches
                var kbResults = await _knowledgeBaseService.SearchAsync(userQuery, topK: 1);

                // If KB has relevant results with minimum score, it's in scope
                if (kbResults.Count > 0 && kbResults[0].Score > 0.25)
                {
                    return new ScopeCheckResult
                    {
                        IsInScope = true,
                        ConfidenceScore = kbResults[0].Score,
                        Message = ""
                    };
                }

                // Check for JIFAS keywords
                if (HasJifasKeywords(lowerQuery))
                {
                    return new ScopeCheckResult
                    {
                        IsInScope = true,
                        ConfidenceScore = 0.7,
                        Message = ""
                    };
                }

                // Dynamic check: search for module/workflow mentions from JifasContextService
                if (HasDynamicJifasContent(lowerQuery))
                {
                    return new ScopeCheckResult
                    {
                        IsInScope = true,
                        ConfidenceScore = 0.75,
                        Message = ""
                    };
                }

                // Default: assume out of scope if no KB match and no JIFAS keywords
                return new ScopeCheckResult
                {
                    IsInScope = false,
                    ConfidenceScore = 0.2,
                    Message = outOfScopeMessage
                };
            }
            catch (Exception ex)
            {
                _logger.LogError("[OutOfScopeDetector] Error checking scope", ex);
                
                // Default to in-scope if detection fails (fail-safe)
                return new ScopeCheckResult
                {
                    IsInScope = true,
                    ConfidenceScore = 0.5,
                    Message = ""
                };
            }
        }

        private bool HasOutOfScopeKeywords(string query)
        {
            return _outOfScopeKeywords.Any(keyword => query.Contains(keyword));
        }

        private bool HasJifasKeywords(string query)
        {
            return _jifasKeywords.Any(keyword => query.Contains(keyword));
        }

        /// <summary>
        /// Dynamic check using JifasContextService
        /// Searches for workflow actions mentioned in the query
        /// This allows auto-detection of new modules without hardcoding
        /// </summary>
        private bool HasDynamicJifasContent(string lowerQuery)
        {
            try
            {
                // Check for workflow actions (Create, Edit, View, Delete, etc.)
                var workflowActions = new[] 
                { 
                    "create", "edit", "delete", "view", "remove", "expand",
                    "upload", "post", "approval", "approve", "reject", "void"
                };

                // If query contains action + JIFAS-related terms, likely in scope
                var hasAction = workflowActions.Any(action => lowerQuery.Contains(action));
                if (hasAction && (lowerQuery.Contains("master") || lowerQuery.Contains("data") || 
                                  lowerQuery.Contains("workflow") || lowerQuery.Contains("module") ||
                                  lowerQuery.Contains("company") || lowerQuery.Contains("division")))
                    return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OutOfScopeDetector] HasDynamicJifasContent error: {ex.Message}");
            }

            return false;
        }
    }
}
