using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Jifas.Assistant.Services
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
    /// 
    /// Compatible with .NET 10 and uses proper dependency injection.
    /// </summary>
    public class OutOfScopeDetector : IOutOfScopeDetector
    {
        private readonly IKnowledgeBaseService _knowledgeBaseService;
        private readonly IJifasContextService _jifasContext;
        private readonly ILoggerService _logger;
        private readonly IConfiguration _configuration;
        private readonly string _outOfScopeMessage;

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
            "journal entry", "jurnal", "posting", "approval", "persetujuan",
            "invoice number", "nomor invoice", "pd form", "pd approval",
            "tax approval", "pajak", "pph", "ppn", "over budget", "melebihi budget",
            
            // JIFAS Workflow Terms
            "workflow", "alur", "tahapan", "status", "draft", "pending", "approved",
            "disetujui", "rejected", "ditolak", "void", "pembatalan", "reverse",
            "realization", "realisasi", "correction", "koreksi", "verification", "verifikasi",
            
            // JIFAS Access/Troubleshooting
            "login", "akses", "access", "password", "username", "windows login",
            "error", "masalah", "problem", "troubleshooting", "tidak bisa", "nggak bisa",
            "help", "bantuan", "support", "pertanyaan", "question"
        };

        private const string OUT_OF_SCOPE_MESSAGE_DEFAULT = 
            "Mohon maaf, pertanyaan Anda berada di luar cakupan JIFAS AI Assistant. " +
            "Saya dirancang khusus untuk menjawab pertanyaan tentang JIFAS (Jababeka Integrated Finance Accounting System) saja, " +
            "seperti akses login, troubleshooting, penggunaan modul AR/AP/GL, dan fitur-fitur JIFAS lainnya. " +
            "Apakah ada pertanyaan lain tentang JIFAS yang bisa saya bantu?";

        /// <summary>
        /// Initialize out-of-scope detector with dependency injection
        /// All dependencies required - no optional parameters
        /// </summary>
        public OutOfScopeDetector(
            IKnowledgeBaseService knowledgeBaseService,
            IJifasContextService jifasContext,
            ILoggerService logger,
            IConfiguration configuration)
        {
            _knowledgeBaseService = knowledgeBaseService ?? throw new ArgumentNullException(nameof(knowledgeBaseService));
            _jifasContext = jifasContext ?? throw new ArgumentNullException(nameof(jifasContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            // Read custom out-of-scope message from configuration, fallback to default
            _outOfScopeMessage = _configuration.GetValue("Chat:OutOfScopeMessage", OUT_OF_SCOPE_MESSAGE_DEFAULT);

            _logger.LogInformation("[OutOfScopeDetector] Initialized with {0} out-of-scope keywords and {1} JIFAS keywords",
                _outOfScopeKeywords.Count, _jifasKeywords.Count);
        }

        /// <summary>
        /// Check if user query is within JIFAS scope
        /// Uses multi-layered detection for accuracy
        /// </summary>
        public async Task<ScopeCheckResult> CheckScopeAsync(string userQuery)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userQuery))
                {
                    _logger.LogWarning("[OutOfScopeDetector] Empty query provided");
                    return new ScopeCheckResult
                    {
                        IsInScope = false,
                        ConfidenceScore = 0,
                        Message = _outOfScopeMessage
                    };
                }

                var lowerQuery = userQuery.ToLower();

                // Layer 1: Hard reject - clearly out-of-scope keywords
                if (HasOutOfScopeKeywords(lowerQuery))
                {
                    _logger.LogDebug("[OutOfScopeDetector] Query rejected (out-of-scope keywords detected): {0}", 
                        userQuery.Substring(0, Math.Min(50, userQuery.Length)));
                    return new ScopeCheckResult
                    {
                        IsInScope = false,
                        ConfidenceScore = 0.1,
                        Message = _outOfScopeMessage
                    };
                }

                // Layer 2: Knowledge base search
                var kbResults = await _knowledgeBaseService.SearchAsync(userQuery, topK: 1);
                if (kbResults.Count > 0 && kbResults[0].Score > 0.25)
                {
                    _logger.LogDebug("[OutOfScopeDetector] Query approved (KB match score: {0:F2})", kbResults[0].Score);
                    return new ScopeCheckResult
                    {
                        IsInScope = true,
                        ConfidenceScore = kbResults[0].Score,
                        Message = ""
                    };
                }

                // Layer 3: JIFAS keywords check
                if (HasJifasKeywords(lowerQuery))
                {
                    _logger.LogDebug("[OutOfScopeDetector] Query approved (JIFAS keywords detected)");
                    return new ScopeCheckResult
                    {
                        IsInScope = true,
                        ConfidenceScore = 0.7,
                        Message = ""
                    };
                }

                // Layer 4: Dynamic JIFAS content detection
                if (HasDynamicJifasContent(lowerQuery))
                {
                    _logger.LogDebug("[OutOfScopeDetector] Query approved (dynamic JIFAS content detected)");
                    return new ScopeCheckResult
                    {
                        IsInScope = true,
                        ConfidenceScore = 0.75,
                        Message = ""
                    };
                }

                // Default: out of scope
                _logger.LogDebug("[OutOfScopeDetector] Query rejected (no JIFAS markers detected): {0}", 
                    userQuery.Substring(0, Math.Min(50, userQuery.Length)));
                return new ScopeCheckResult
                {
                    IsInScope = false,
                    ConfidenceScore = 0.2,
                    Message = _outOfScopeMessage
                };
            }
            catch (Exception ex)
            {
                _logger.LogError("[OutOfScopeDetector] Error checking scope: {0}", ex, ex.Message);
                
                // Fail-safe: assume in-scope if detection fails
                return new ScopeCheckResult
                {
                    IsInScope = true,
                    ConfidenceScore = 0.5,
                    Message = ""
                };
            }
        }

        /// <summary>
        /// Check if query contains out-of-scope keywords
        /// </summary>
        private bool HasOutOfScopeKeywords(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return false;

            return _outOfScopeKeywords.Any(keyword => query.Contains(keyword));
        }

        /// <summary>
        /// Check if query contains JIFAS-related keywords
        /// </summary>
        private bool HasJifasKeywords(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return false;

            return _jifasKeywords.Any(keyword => query.Contains(keyword));
        }

        /// <summary>
        /// Dynamic detection using workflow patterns
        /// Searches for workflow actions mentioned in the query
        /// This allows auto-detection of new modules without hardcoding
        /// </summary>
        private bool HasDynamicJifasContent(string lowerQuery)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(lowerQuery))
                    return false;

                // Workflow action keywords
                var workflowActions = new[] 
                { 
                    "create", "edit", "delete", "view", "remove", "expand",
                    "upload", "post", "approval", "approve", "reject", "void"
                };

                // JIFAS context keywords
                var contextKeywords = new[]
                {
                    "master", "data", "workflow", "module", "company", 
                    "division", "department", "status", "approval"
                };

                // If query contains action + JIFAS-related terms, likely in scope
                var hasAction = workflowActions.Any(action => lowerQuery.Contains(action));
                var hasContext = contextKeywords.Any(ctx => lowerQuery.Contains(ctx));

                return hasAction && hasContext;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[OutOfScopeDetector] Error in dynamic content detection: {0}", ex.Message);
                return false;
            }
        }
    }
}
