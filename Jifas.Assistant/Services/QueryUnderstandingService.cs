using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jifas.Assistant.Utilities;

namespace Jifas.Assistant.Services
{
    #region Models & Enums

    /// <summary>
    /// Intent types untuk JIFAS queries
    /// </summary>
    public enum IntentType
    {
        HowTo,              // "Bagaimana cara...", "Cara membuat..."
        Troubleshooting,    // "Kenapa error...", "Tidak bisa..."
        Explanation,        // "Apa itu...", "Jelaskan..."
        Comparison,         // "Bedanya apa...", "Perbedaan..."
        Navigation,         // "Di mana menu...", "Lokasi..."
        Status,             // "Status approval...", "Progress..."
        Configuration,      // "Cara setting...", "Setup..."
        Greeting,           // "Halo", "Selamat pagi"
        Gratitude,          // "Terima kasih", "Thanks"
        TicketRequest,      // "Buat tiket", "Lapor masalah", "Create ticket"
        OutOfScope,         // Not JIFAS-related
        Unclear,            // Ambiguous query
        General             // General JIFAS question
    }

    /// <summary>
    /// Result dari expanded query
    /// </summary>
    public class ExpandedQuery
    {
        public string OriginalQuery { get; set; }
        public string NormalizedQuery { get; set; }
        public List<string> Variations { get; set; } = new List<string>();
        public List<string> Keywords { get; set; } = new List<string>();
        public List<string> Synonyms { get; set; } = new List<string>();
        public string EnhancedSearchQuery { get; set; }
    }

    /// <summary>
    /// Result dari intent classification
    /// </summary>
    public class IntentResult
    {
        public IntentType Intent { get; set; }
        public double Confidence { get; set; }
        public string Reason { get; set; }
        public List<string> DetectedKeywords { get; set; } = new List<string>();
        public bool RequiresClarification { get; set; }
        public string SuggestedClarification { get; set; }
    }

    /// <summary>
    /// Complete query understanding result
    /// </summary>
    public class QueryUnderstandingResult
    {
        public ExpandedQuery ExpandedQuery { get; set; }
        public IntentResult Intent { get; set; }
        public bool IsInScope { get; set; }
        public string Topic { get; set; }
    }

    #endregion

    #region Interface

    /// <summary>
    /// Service untuk memahami query user secara menyeluruh
    /// Menggabungkan Query Expansion dan Intent Classification
    /// </summary>
    public interface IQueryUnderstandingService
    {
        /// <summary>
        /// Analyze query secara lengkap (expand + classify intent)
        /// </summary>
        Task<QueryUnderstandingResult> AnalyzeQueryAsync(string query);

        /// <summary>
        /// Expand query dengan synonyms dan variations
        /// </summary>
        Task<ExpandedQuery> ExpandQueryAsync(string originalQuery);

        /// <summary>
        /// Classify intent dari user query
        /// </summary>
        Task<IntentResult> ClassifyIntentAsync(string userQuery);

        /// <summary>
        /// Check apakah query dalam scope JIFAS
        /// </summary>
        Task<bool> IsInJifasScopeAsync(string userQuery);

        /// <summary>
        /// Normalize query
        /// </summary>
        string NormalizeQuery(string query);

        /// <summary>
        /// Extract key terms dari query
        /// </summary>
        List<string> ExtractKeyTerms(string query);

        /// <summary>
        /// Detect if query needs clarification
        /// </summary>
        bool NeedsClarification(string userQuery, IntentResult intent);
    }

    #endregion

    #region Implementation

    public class QueryUnderstandingService : IQueryUnderstandingService
    {
        private readonly ILoggerService _logger;
        private readonly ICacheService _cacheService;

        #region Static Data - Synonyms & Patterns

        // JIFAS-specific synonyms mapping
        private static readonly Dictionary<string, List<string>> JifasSynonyms = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            // Invoice & Payment
            { "invoice", new List<string> { "tagihan", "faktur", "inv", "billing", "invoice payment" } },
            { "payment", new List<string> { "pembayaran", "bayar", "transfer", "pmt", "pay" } },
            { "approve", new List<string> { "approval", "setuju", "acc", "otorisasi", "approve", "head approval", "finance checking" } },
            { "reject", new List<string> { "tolak", "batal", "cancel", "void" } },

            // Budget
            { "budget", new List<string> { "anggaran", "dana", "alokasi", "budget card" } },
            { "overbudget", new List<string> { "over budget", "melebihi anggaran", "lebih budget", "committed", "realization" } },

            // PUM
            { "pum", new List<string> { "uang muka", "advance", "kasbon", "perjalanan dinas", "realisasi pum", "old pum", "distribusi" } },
            { "reimburse", new List<string> { "reimbursement", "penggantian", "klaim", "settlement" } },

            // Receiving & CashBank
            { "receiving", new List<string> { "penerimaan", "terima barang", "rv", "receive voucher", "goods receipt", "unidentified rv" } },
            { "rv", new List<string> { "receiving", "receipt", "penerimaan", "receive voucher" } },
            { "cashbank", new List<string> { "cash bank", "kas bank", "petty cash", "receivetax", "paymenttax" } },

            // SPK
            { "spk", new List<string> { "surat perintah kerja", "kontrak", "contract", "perjanjian kerja" } },

            // Tax
            { "tax", new List<string> { "pajak", "ppn", "pph", "npwp", "faktur pajak", "bukti potong", "tax approval" } },
            { "taxapproval", new List<string> { "tax approval", "persetujuan pajak", "review pajak" } },

            // Consolidation
            { "consolacc", new List<string> { "consolidation accounting", "konsolidasi", "laporan konsolidasi" } },

            // Master Data
            { "vendor", new List<string> { "supplier", "pemasok", "rekanan", "tenant" } },
            { "coa", new List<string> { "chart of account", "akun", "account", "kode akun" } },
            { "company", new List<string> { "perusahaan", "entitas", "unit bisnis" } },
            { "division", new List<string> { "divisi", "bagian" } },
            { "department", new List<string> { "departemen", "dept", "unit" } },

            // Accounting
            { "posting", new List<string> { "post", "jurnal", "entry", "bulk posting" } },
            { "gl", new List<string> { "general ledger", "buku besar", "ledger" } },
            { "ar", new List<string> { "account receivable", "piutang" } },
            { "ap", new List<string> { "account payable", "hutang", "payable" } },

            // Actions
            { "buat", new List<string> { "create", "tambah", "input", "bikin", "membuat", "add" } },
            { "edit", new List<string> { "ubah", "update", "modify", "ganti", "change" } },
            { "hapus", new List<string> { "delete", "remove", "buang" } },
            { "lihat", new List<string> { "view", "tampilkan", "cek", "check", "see" } },
            { "cari", new List<string> { "search", "find", "filter", "lookup" } },

            // Status
            { "pending", new List<string> { "menunggu", "belum diproses", "waiting", "need approval" } },
            { "done", new List<string> { "selesai", "completed", "finish", "posted", "paid" } },
            { "draft", new List<string> { "draf", "belum submit", "belum diajukan" } },

            // Troubleshooting
            { "error", new List<string> { "masalah", "gagal", "tidak bisa", "problem", "issue", "bug" } },
            { "tidak bisa", new List<string> { "gagal", "error", "cannot", "can't", "tidak berhasil" } },

            // Common question words
            { "cara", new List<string> { "bagaimana", "how", "langkah", "step", "prosedur", "tutorial" } },
            { "apa", new List<string> { "what", "definisi", "pengertian", "arti", "maksud" } }
        };

        // Indonesian stopwords
        private static readonly HashSet<string> Stopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "apa", "yang", "dan", "atau", "untuk", "dari", "ke", "di", "ini", "itu",
            "adalah", "dengan", "pada", "dalam", "akan", "bisa", "dapat", "sudah",
            "belum", "jika", "bila", "maka", "saya", "kami", "kita", "anda", "mereka",
            "nya", "tersebut", "the", "a", "an", "in", "on", "at", "to", "for", "of",
            "is", "are", "was", "were", "be", "been", "being", "have", "has", "had",
            "do", "does", "did", "will", "would", "could", "should", "may", "might",
            "must", "shall", "can", "need", "tolong", "mohon", "silakan", "please",
            "bantu", "mau", "ingin", "hendak", "gimana", "gmn"
        };

        // Pattern-based intent detection
        private static readonly Dictionary<IntentType, List<string>> IntentPatterns = new Dictionary<IntentType, List<string>>
        {
            { IntentType.HowTo, new List<string> 
            { 
                @"^bagaimana\b", @"^cara\b", @"\blangkah\b", @"\bstep\b", @"\bprosedur\b",
                @"^gimana\b", @"^gmn\b", @"\btutorial\b", @"\bpetunjuk\b", @"^how\b"
            }},
            { IntentType.Troubleshooting, new List<string> 
            { 
                @"\berror\b", @"\bgagal\b", @"\btidak bisa\b", @"\bmasalah\b", @"\bissue\b",
                @"^kenapa\b", @"^mengapa\b", @"\bnot working\b", @"\bfailed\b", @"\bcrash\b",
                @"\bsolusi\b", @"\batasi\b", @"\bfix\b", @"\bresolve\b"
            }},
            { IntentType.Explanation, new List<string> 
            { 
                @"^apa\s+(itu|yang|saja)\b", @"^definisi\b", @"^pengertian\b", @"^arti\b",
                @"^jelaskan\b", @"^explain\b", @"^what\s+is\b", @"\bmaksud\b"
            }},
            { IntentType.Comparison, new List<string> 
            { 
                @"\bbeda(nya)?\b", @"\bperbedaan\b", @"\bvs\b", @"\bversus\b",
                @"\bcompare\b", @"\bdifference\b", @"\bmana yang lebih\b"
            }},
            { IntentType.Navigation, new List<string> 
            { 
                @"\bdi\s*mana\b", @"\blokasi\b", @"\bwhere\b", @"\bmenu\s+apa\b",
                @"\btempat\b", @"\bposisi\b", @"\bpath\b", @"\broute\b"
            }},
            { IntentType.Status, new List<string> 
            { 
                @"\bstatus\b", @"\bprogress\b", @"\btracking\b", @"\bmonitor\b",
                @"\bsudah sampai mana\b", @"\bcek\s+status\b"
            }},
            { IntentType.Configuration, new List<string> 
            { 
                @"\bsetting\b", @"\bsetup\b", @"\bkonfigurasi\b", @"\bconfig\b",
                @"\binstall\b", @"\baktifkan\b", @"\benable\b", @"\bnonaktifkan\b"
            }},
            { IntentType.Greeting, new List<string> 
            { 
                @"^(halo|hai|hello|hi|hey)\b", @"^selamat\s+(pagi|siang|sore|malam)\b",
                @"^good\s+(morning|afternoon|evening)\b", @"^assalamualaikum\b"
            }},
            { IntentType.Gratitude, new List<string> 
            { 
                @"\bterima\s*kasih\b", @"\bthanks\b", @"\bthank\s*you\b", @"\bmakasih\b",
                @"\bthx\b", @"\btq\b"
            }},
            { IntentType.TicketRequest, new List<string>
            {
                @"\bbuat(?:kan)?\s+tiket?\b", @"\bcreate\s+ticket\b", @"\bbikin\s+tiket?\b",
                @"\blapor(?:kan)?\s+(?:masalah|issue|error|problem)\b",
                @"\breport\s+(?:issue|problem|bug)\b",
                @"\bopen\s+ticket\b", @"\bsubmit\s+ticket\b",
                @"\bmau\s+(?:buat|bikin)\s+tiket?\b", @"\btolong\s+buat(?:kan)?\s+tiket?\b",
                @"\bescalate\b", @"\beskalasi\b"
            }}
        };

        // JIFAS scope keywords - comprehensive coverage of all modules
        private static readonly HashSet<string> JifasKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Core system
            "jifas", "jababeka", "integrated", "finance", "accounting",
            // Invoice module
            "invoice", "tagihan", "faktur", "inv", "billing",
            // Payment module
            "payment", "pembayaran", "bayar", "transfer", "bg", "bilyet giro", "giro", "cek",
            // PUM module
            "pum", "uang muka", "advance", "kasbon", "perjalanan dinas", "realisasi", "settlement",
            "distribusi", "old pum",
            // Receiving module
            "receiving", "rv", "receive voucher", "penerimaan", "goods receipt", "unidentified",
            // CashBank module
            "cashbank", "cash bank", "kas", "bank", "petty cash", "receivetax", "paymenttax",
            // Budget module
            "budget", "anggaran", "dana", "overbudget", "over budget", "committed", "realization",
            "budget card",
            // SPK module
            "spk", "surat perintah kerja", "kontrak", "contract",
            // Accounting module
            "gl", "general ledger", "buku besar", "jurnal", "journal", "posting", "bulk posting",
            "ar", "ap", "account receivable", "account payable", "piutang", "hutang",
            "acc period", "account period", "cross month", "cross year",
            // Consolidation
            "consolacc", "consolidation", "konsolidasi",
            // Tax
            "tax", "pajak", "ppn", "pph", "npwp", "faktur pajak", "bukti potong",
            "taxapproval", "tax approval",
            // Master data
            "vendor", "supplier", "pemasok", "rekanan",
            "coa", "chart of account", "akun",
            "company", "perusahaan", "entitas",
            "employee", "karyawan", "pegawai",
            "division", "divisi", "department", "departemen",
            "master", "master data",
            // Approval workflow
            "approval", "approve", "approver", "head approval", "finance checking",
            "reject", "tolak", "void", "reverse", "submit", "draft",
            // Status
            "status", "need head approval", "need finance checking", "need tax approval",
            "need posting", "ready to pay", "posted", "paid", "complete", "confirmed",
            // Report
            "report", "laporan", "cashflow", "daily cashflow", "inquiry",
            "saldo", "deposito", "realisasi", "commited realization",
            // Access
            "login", "akses", "role", "permission", "authority", "otorisasi",
            "user", "wmtr", "fina", "usrl",
            // Actions
            "create", "buat", "edit", "update", "hapus", "delete", "lihat", "view",
            "search", "cari", "filter", "export", "print", "cetak"
        };

        // Out of scope indicators
        private static readonly HashSet<string> OutOfScopeKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "chatgpt", "openai", "google", "facebook", "instagram", "tiktok",
            "bitcoin", "crypto", "forex", "trading",
            "cuaca", "weather", "resep", "recipe", "film", "movie", "musik", "music",
            "game", "olah raga", "sport", "politik",
            "sap", "oracle", "dynamics", "quickbooks", "odoo",
            "joke", "lelucon", "puisi", "poem", "cerita", "story"
        };

        #endregion

        public QueryUnderstandingService(ILoggerService logger, ICacheService cacheService)
        {
            _logger = logger;
            _cacheService = cacheService;
        }

        #region Main Methods

        /// <summary>
        /// Complete query analysis - expand + classify + scope check
        /// </summary>
        public async Task<QueryUnderstandingResult> AnalyzeQueryAsync(string query)
        {
            var result = new QueryUnderstandingResult();

            try
            {
                // Run expansion and intent classification in parallel
                var expandTask = ExpandQueryAsync(query);
                var intentTask = ClassifyIntentAsync(query);
                var scopeTask = IsInJifasScopeAsync(query);

                await Task.WhenAll(expandTask, intentTask, scopeTask);

                result.ExpandedQuery = await expandTask;
                result.Intent = await intentTask;
                result.IsInScope = await scopeTask;
                result.Topic = ExtractTopic(query);

                _logger.LogInformation($"[QueryUnderstanding] Query: '{query}' ? Intent: {result.Intent.Intent}, InScope: {result.IsInScope}, Topic: {result.Topic}");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[QueryUnderstanding] Error: {ex.Message}");
                return new QueryUnderstandingResult
                {
                    ExpandedQuery = new ExpandedQuery { OriginalQuery = query, NormalizedQuery = NormalizeQuery(query) },
                    Intent = new IntentResult { Intent = IntentType.General, Confidence = 0.5 },
                    IsInScope = true
                };
            }
        }

        #endregion

        #region Query Expansion

        public async Task<ExpandedQuery> ExpandQueryAsync(string originalQuery)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(originalQuery))
                    return new ExpandedQuery { OriginalQuery = originalQuery };

                // Check cache
                var cacheKey = $"QueryExpand_{HashHelper.ToShortStableHash(originalQuery)}";
                var cached = _cacheService.Get<ExpandedQuery>(cacheKey);
                if (cached != null) return cached;

                var result = new ExpandedQuery
                {
                    OriginalQuery = originalQuery,
                    NormalizedQuery = NormalizeQuery(originalQuery)
                };

                result.Keywords = ExtractKeyTerms(result.NormalizedQuery);

                // Find synonyms for each keyword
                foreach (var keyword in result.Keywords)
                {
                    var synonyms = FindSynonyms(keyword);
                    result.Synonyms.AddRange(synonyms);
                }
                result.Synonyms = result.Synonyms.Distinct().ToList();

                // Generate query variations
                result.Variations = await GenerateVariationsAsync(originalQuery, result.Keywords);

                // Build enhanced search query
                result.EnhancedSearchQuery = BuildEnhancedSearchQuery(result);

                // Cache result
                _cacheService.Set(cacheKey, result, 60);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[QueryExpansion] Error: {ex.Message}");
                return new ExpandedQuery
                {
                    OriginalQuery = originalQuery,
                    NormalizedQuery = NormalizeQuery(originalQuery),
                    Keywords = ExtractKeyTerms(originalQuery)
                };
            }
        }

        public string NormalizeQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return "";

            var normalized = query.ToLower().Trim();
            normalized = Regex.Replace(normalized, @"\s+", " ");
            normalized = Regex.Replace(normalized, @"[^\w\s\-]", " ");
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            return normalized;
        }

        public List<string> ExtractKeyTerms(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<string>();

            return query.ToLower()
                .Split(new[] { ' ', '-', '_', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 1 && !Stopwords.Contains(w))
                .Distinct()
                .ToList();
        }

        private List<string> FindSynonyms(string word)
        {
            var synonyms = new List<string>();

            if (JifasSynonyms.TryGetValue(word, out var directSynonyms))
                synonyms.AddRange(directSynonyms);

            foreach (var kvp in JifasSynonyms)
            {
                if (kvp.Value.Any(s => s.Equals(word, StringComparison.OrdinalIgnoreCase)))
                {
                    synonyms.Add(kvp.Key);
                    synonyms.AddRange(kvp.Value);
                }
            }

            return synonyms.Distinct().Where(s => !s.Equals(word, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private Task<List<string>> GenerateVariationsAsync(string originalQuery, List<string> keywords)
        {
            var variations = new List<string>();
            var normalized = NormalizeQuery(originalQuery);

            // Variation 1: Replace synonyms
            foreach (var keyword in keywords.Take(3))
            {
                var synonyms = FindSynonyms(keyword);
                foreach (var synonym in synonyms.Take(2))
                {
                    var variation = normalized.Replace(keyword, synonym);
                    if (!variation.Equals(normalized))
                        variations.Add(variation);
                }
            }

            // Variation 2: Add JIFAS context
            if (!normalized.Contains("jifas"))
                variations.Add($"jifas {normalized}");

            // Variation 3: Rephrase patterns
            if (normalized.StartsWith("cara "))
            {
                variations.Add(normalized.Replace("cara ", "bagaimana "));
                variations.Add(normalized.Replace("cara ", "langkah-langkah "));
            }
            else if (normalized.StartsWith("bagaimana "))
            {
                variations.Add(normalized.Replace("bagaimana ", "cara "));
            }

            return Task.FromResult(variations.Distinct().Take(5).ToList());
        }

        private string BuildEnhancedSearchQuery(ExpandedQuery expanded)
        {
            var terms = new List<string>();
            terms.AddRange(expanded.Keywords);
            terms.AddRange(expanded.Synonyms.Take(5));
            return string.Join(" ", terms.Distinct());
        }

        #endregion

        #region Intent Classification

        public async Task<IntentResult> ClassifyIntentAsync(string userQuery)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userQuery))
                    return new IntentResult { Intent = IntentType.Unclear, Confidence = 0, Reason = "Empty query" };

                var normalizedQuery = userQuery.ToLower().Trim();

                // Step 1: Check for greetings/gratitude (fast path)
                var greetingResult = CheckGreetingOrGratitude(normalizedQuery);
                if (greetingResult != null) return greetingResult;

                // Step 2: Check if out of scope
                if (!await IsInJifasScopeAsync(userQuery))
                    return new IntentResult { Intent = IntentType.OutOfScope, Confidence = 0.9, Reason = "Query tidak berkaitan dengan JIFAS" };

                // Step 3: Pattern-based detection
                var patternResult = DetectIntentByPattern(normalizedQuery);
                if (patternResult.Confidence >= 0.7) return patternResult;

                // Step 4: Keyword-based detection
                var keywordResult = DetectIntentByKeywords(normalizedQuery);
                var result = keywordResult.Confidence > patternResult.Confidence ? keywordResult : patternResult;

                // Step 5: Check if needs clarification
                result.RequiresClarification = NeedsClarification(userQuery, result);
                if (result.RequiresClarification)
                    result.SuggestedClarification = GenerateClarificationSuggestion(userQuery, result);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[IntentClassifier] Error: {ex.Message}");
                return new IntentResult { Intent = IntentType.General, Confidence = 0.5, Reason = "Classification error" };
            }
        }

        public async Task<bool> IsInJifasScopeAsync(string userQuery)
        {
            if (string.IsNullOrWhiteSpace(userQuery)) return false;

            var normalizedQuery = userQuery.ToLower();

            // Check for out of scope keywords first
            foreach (var keyword in OutOfScopeKeywords)
            {
                if (normalizedQuery.Contains(keyword))
                    return false;
            }

            // Check for JIFAS keywords
            foreach (var keyword in JifasKeywords)
            {
                if (normalizedQuery.Contains(keyword.ToLower()))
                    return true;
            }

            // Short queries without clear indicators - assume in scope
            if (normalizedQuery.Split(' ').Length <= 3)
                return true;

            return await Task.FromResult(true);
        }

        public bool NeedsClarification(string userQuery, IntentResult intent)
        {
            if (userQuery.Length < 10) return true;
            if (intent.Confidence < 0.5) return true;
            if (intent.Intent == IntentType.Unclear) return true;
            if (userQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2) return true;
            return false;
        }

        private IntentResult CheckGreetingOrGratitude(string query)
        {
            foreach (var pattern in IntentPatterns[IntentType.Greeting])
            {
                if (Regex.IsMatch(query, pattern, RegexOptions.IgnoreCase))
                    return new IntentResult { Intent = IntentType.Greeting, Confidence = 0.95, Reason = "Greeting pattern" };
            }

            foreach (var pattern in IntentPatterns[IntentType.Gratitude])
            {
                if (Regex.IsMatch(query, pattern, RegexOptions.IgnoreCase))
                    return new IntentResult { Intent = IntentType.Gratitude, Confidence = 0.95, Reason = "Gratitude pattern" };
            }

            foreach (var pattern in IntentPatterns[IntentType.TicketRequest])
            {
                if (Regex.IsMatch(query, pattern, RegexOptions.IgnoreCase))
                    return new IntentResult { Intent = IntentType.TicketRequest, Confidence = 0.95, Reason = "Ticket request pattern" };
            }

            return null;
        }

        private IntentResult DetectIntentByPattern(string query)
        {
            var result = new IntentResult { Intent = IntentType.General, Confidence = 0.3 };

            foreach (var intentPattern in IntentPatterns)
            {
                if (intentPattern.Key == IntentType.Greeting || intentPattern.Key == IntentType.Gratitude)
                    continue;

                foreach (var pattern in intentPattern.Value)
                {
                    if (Regex.IsMatch(query, pattern, RegexOptions.IgnoreCase))
                    {
                        var confidence = pattern.StartsWith("^") ? 0.85 : 0.75;
                        if (confidence > result.Confidence)
                        {
                            result.Intent = intentPattern.Key;
                            result.Confidence = confidence;
                            result.Reason = $"Pattern match: {pattern}";
                        }
                    }
                }
            }

            return result;
        }

        private IntentResult DetectIntentByKeywords(string query)
        {
            var result = new IntentResult { Intent = IntentType.General, Confidence = 0.4 };
            var keywords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var howToKeywords = new[] { "cara", "bagaimana", "langkah", "step", "tutorial", "membuat", "create" };
            var troubleKeywords = new[] { "error", "gagal", "masalah", "tidak", "bisa", "kenapa", "issue" };
            var explainKeywords = new[] { "apa", "definisi", "pengertian", "jelaskan", "arti", "maksud" };

            var howToMatches = keywords.Count(k => howToKeywords.Contains(k.ToLower()));
            if (howToMatches > 0)
            {
                var confidence = Math.Min(0.5 + (howToMatches * 0.15), 0.85);
                if (confidence > result.Confidence)
                {
                    result.Intent = IntentType.HowTo;
                    result.Confidence = confidence;
                    result.Reason = "HowTo keywords";
                }
            }

            var troubleMatches = keywords.Count(k => troubleKeywords.Contains(k.ToLower()));
            if (troubleMatches > 0)
            {
                var confidence = Math.Min(0.5 + (troubleMatches * 0.15), 0.85);
                if (confidence > result.Confidence)
                {
                    result.Intent = IntentType.Troubleshooting;
                    result.Confidence = confidence;
                    result.Reason = "Troubleshooting keywords";
                }
            }

            var explainMatches = keywords.Count(k => explainKeywords.Contains(k.ToLower()));
            if (explainMatches > 0)
            {
                var confidence = Math.Min(0.5 + (explainMatches * 0.15), 0.85);
                if (confidence > result.Confidence)
                {
                    result.Intent = IntentType.Explanation;
                    result.Confidence = confidence;
                    result.Reason = "Explanation keywords";
                }
            }

            return result;
        }

        private string GenerateClarificationSuggestion(string query, IntentResult intent)
        {
            return intent.Intent switch
            {
                IntentType.Unclear => "Bisa diperjelas pertanyaannya? Misalnya: 'Bagaimana cara membuat Invoice di JIFAS?'",
                IntentType.General => "Apakah pertanyaan ini terkait dengan modul tertentu di JIFAS? (Invoice, Payment, PUM, Budget, dll)",
                _ => "Silakan berikan detail lebih lanjut agar saya dapat membantu dengan lebih baik."
            };
        }

        private string ExtractTopic(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return "General";

            var messageLower = message.ToLower();
            var topics = new Dictionary<string, string[]>
            {
                { "Invoice", new[] { "invoice", "tagihan", "faktur" } },
                { "Payment", new[] { "payment", "bayar", "transfer" } },
                { "PUM", new[] { "pum", "uang muka", "advance", "kasbon" } },
                { "Receiving", new[] { "receiving", "rv", "terima barang" } },
                { "Budget", new[] { "budget", "anggaran", "overbudget" } },
                { "Approval", new[] { "approve", "approval", "reject" } },
                { "Master Data", new[] { "master", "vendor", "coa", "company" } },
                { "Accounting", new[] { "posting", "jurnal", "gl", "ledger" } },
                { "Report", new[] { "report", "laporan", "dashboard" } }
            };

            foreach (var topic in topics)
            {
                if (topic.Value.Any(k => messageLower.Contains(k)))
                    return topic.Key;
            }

            return "General";
        }

        #endregion
    }

    #endregion
}
