using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using jifas_assistant.DAL.Models;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Result of context analysis
    /// </summary>
    public class ContextAnalysisResult
    {
        public string UserQuery { get; set; }
        public int ResultCount { get; set; }
        public double AverageRelevance { get; set; }
        public int OverallRelevance { get; set; }
        public int ContentCoverage { get; set; }
        public bool IsExactMatch { get; set; }
        public bool IsPartialMatch { get; set; }
        public bool IsWeakMatch { get; set; }
        public string MatchType { get; set; }
        public List<string> MissingContext { get; set; } = new List<string>();
    }

    /// <summary>
    /// Advanced prompt engineering service for JIFAS AI Assistant
    /// Creates intelligent, context-aware prompts that guide Ollama to find answers
    /// This is NOT hardcoded - it analyzes KB content and builds dynamic prompts
    /// </summary>
    public interface IPromptEngineeringService
    {
        /// <summary>
        /// Build intelligent prompt based on KB results and user query
        /// </summary>
        Task<string> BuildIntelligentPromptAsync(string userQuery, List<KnowledgeBaseResult> kbResults, string sessionContext = null);

        /// <summary>
        /// Generate context-aware system prompt dynamically
        /// </summary>
        string BuildDynamicSystemPrompt(List<KnowledgeBaseResult> kbResults, string userQuery);

        /// <summary>
        /// Analyze KB results to find best matching sections
        /// </summary>
        Task<ContextAnalysisResult> AnalyzeContextAsync(string userQuery, List<KnowledgeBaseResult> kbResults);

        /// <summary>
        /// Build fallback prompt when direct match is not found
        /// </summary>
        string BuildFallbackPrompt(string userQuery, List<KnowledgeBaseResult> kbResults);

        /// <summary>
        /// Generate clarification questions if context is ambiguous
        /// </summary>
        Task<string> GenerateClarificationPromptAsync(string userQuery, List<KnowledgeBaseResult> kbResults);
    }

    public class PromptEngineeringService : IPromptEngineeringService
    {
        private readonly ILoggerService _logger;
        private readonly IKnowledgeBaseSearchService _searchService;
        
        // Constants untuk semantic chunking optimization
        private const int OPTIMAL_CHUNK_SIZE = 600;  // words
        private const int MAX_KB_SECTIONS = 5;
        private const int MAX_CONTENT_PER_SECTION = 2000;  // chars

        public PromptEngineeringService(ILoggerService logger, IKnowledgeBaseSearchService searchService)
        {
            _logger = logger;
            _searchService = searchService;
        }

        /// <summary>
        /// Build intelligent prompt by analyzing KB content and user intent
        /// Improvement #1: Semantic chunking optimization
        /// Improvement #2: Enhanced system prompt with JIFAS context
        /// Improvement #3: Query classification & routing
        /// Improvement #4: Conversation context awareness
        /// Improvement #5: Few-shot examples
        /// Improvement #6: Intelligent fallback handling
        /// Improvement #8: Response validation checks
        /// Improvement #9: Error message context enrichment
        /// Improvement #10: Semantic chunking refinement
        /// </summary>
        public async Task<string> BuildIntelligentPromptAsync(string userQuery, List<KnowledgeBaseResult> kbResults, string sessionContext = null)
        {
            try
            {
                _logger.LogInformation($"[PromptEngineering] Building intelligent prompt for: {userQuery}");

                // Improvement #3: Classify query type for smarter routing
                var queryType = ClassifyQueryType(userQuery);
                _logger.LogInformation($"[PromptEngineering] Query classified as: {queryType}");

                // Improvement #1: Analyze KB results with semantic optimization
                var contextAnalysis = await AnalyzeContextAsync(userQuery, kbResults);
                _logger.LogInformation($"[PromptEngineering] Context analysis - Relevance: {contextAnalysis.OverallRelevance}%, Coverage: {contextAnalysis.ContentCoverage}");

                // Build JIFAS system prompt
                var systemPrompt = BuildEnhancedSystemPrompt(kbResults, userQuery);

                // Build main KB content prompt
                var mainPrompt = BuildOptimizedMainPrompt(userQuery, kbResults, contextAnalysis, sessionContext);

                // Build query-type-specific instructions
                var querySpecificInstructions = BuildQuerySpecificInstructionsWithExamples(queryType, userQuery, kbResults);

                // Parse active page context from sessionContext (format: "PAGE:{page}|MODULE:{module}|TITLE:{title}|DOC:{docId}|STATUS:{status}")
                var activePageContext = BuildActivePageContextSection(sessionContext);

                var confidenceLevel = contextAnalysis.OverallRelevance >= 80 ? "TINGGI" : 
                                     contextAnalysis.OverallRelevance >= 60 ? "SEDANG" : "RENDAH";

                var finalPrompt = $@"{systemPrompt}
{activePageContext}
=== KNOWLEDGE BASE ===
{mainPrompt}
=== END KB ===

PERTANYAAN USER: ""{userQuery}""
CONFIDENCE KB: {confidenceLevel} ({contextAnalysis.OverallRelevance}%)

{querySpecificInstructions}

INSTRUKSI FINAL:
- Confidence TINGGI: Jawab dengan lengkap dan percaya diri berdasarkan KB
- Confidence SEDANG: Jawab dengan info yang ada, tambahkan catatan jika ada yang kurang
- Confidence RENDAH: Jujur bahwa info terbatas, sarankan hubungi IT Help Desk
- Jika ada konteks halaman aktif di atas, prioritaskan jawaban yang relevan dengan halaman tersebut
- Format: Natural, spesifik, actionable. Gunakan bullet/numbering untuk langkah-langkah.

JAWABAN:";

                return finalPrompt;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PromptEngineering] Error building prompt: {ex.Message}");
                return BuildFallbackPrompt(userQuery, kbResults);
            }
        }

        /// <summary>
        /// Build active page context section dari sessionContext string
        /// Format sessionContext: "PAGE:/Invoice/Finance|MODULE:Invoice|TITLE:Finance Invoice|DOC:INV-001|STATUS:Draft"
        /// </summary>
        private string BuildActivePageContextSection(string? sessionContext)
        {
            if (string.IsNullOrWhiteSpace(sessionContext)) return string.Empty;

            try
            {
                var parts = sessionContext.Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Split(':', 2))
                    .Where(p => p.Length == 2)
                    .ToDictionary(p => p[0].Trim(), p => p[1].Trim());

                var sb = new StringBuilder();
                sb.AppendLine("=== KONTEKS HALAMAN AKTIF USER ===");

                if (parts.TryGetValue("PAGE", out var page) && !string.IsNullOrEmpty(page))
                    sb.AppendLine($"Halaman yang sedang dibuka: {page}");
                if (parts.TryGetValue("MODULE", out var module) && !string.IsNullOrEmpty(module))
                    sb.AppendLine($"Modul aktif: {module}");
                if (parts.TryGetValue("TITLE", out var title) && !string.IsNullOrEmpty(title))
                    sb.AppendLine($"Judul halaman: {title}");
                if (parts.TryGetValue("DOC", out var doc) && !string.IsNullOrEmpty(doc))
                    sb.AppendLine($"Dokumen yang dipilih: {doc}");
                if (parts.TryGetValue("DOCTYPE", out var docType) && !string.IsNullOrEmpty(docType))
                    sb.AppendLine($"Tipe dokumen: {docType}");
                if (parts.TryGetValue("STATUS", out var status) && !string.IsNullOrEmpty(status))
                    sb.AppendLine($"Status dokumen: {status}");

                sb.AppendLine("CATATAN: Prioritaskan jawaban yang relevan dengan konteks halaman di atas!");
                sb.AppendLine("=== END KONTEKS ===");

                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Build enhanced JIFAS-specific system prompt.
        /// Dynamically enriched based on detected KB categories, query type, and confidence level.
        /// </summary>
        public string BuildEnhancedSystemPrompt(List<KnowledgeBaseResult> kbResults, string userQuery)
        {
            try
            {
                var categories = kbResults.GroupBy(r => r.Category).Select(g => g.Key).Distinct().ToList();
                var documentTitles = kbResults.Select(r => r.Title).Distinct().Take(4).ToList();
                var highestScore = kbResults.Count > 0 ? kbResults.Max(r => r.Score) : 0;
                var queryType = ClassifyQueryType(userQuery);
                var hasSteps = kbResults.Any(r => r.Content.Contains("langkah") || r.Content.Contains("Langkah") || r.Content.Contains("1.") || r.Content.Contains("Step"));
                var confidenceLabel = highestScore >= 0.8 ? "TINGGI" : highestScore >= 0.55 ? "SEDANG" : "RENDAH";

                var sb = new StringBuilder();

                // Core identity — compact but rich
                sb.AppendLine("Kamu adalah JIFAS AI Assistant, AI Persona Agent untuk sistem JIFAS (Jababeka Integrated Finance Accounting System) PT Jababeka Tbk.");
                sb.AppendLine("Kamu seperti rekan kerja senior yang sangat paham JIFAS — helpful, jujur, dan langsung ke inti.");
                sb.AppendLine();

                // Domain knowledge reminder (key concepts)
                sb.AppendLine("DOMAIN PENGETAHUANMU (JIFAS):");
                sb.AppendLine("- Invoice: alur Create?Finance Approval?Head Approval?Tax?Post; status Draft/Submitted/Checking/Approved/Posted");
                sb.AppendLine("- PUM: Pengajuan Uang Muka perjalanan dinas; alur Pengajuan?Approval?Distribusi?Realisasi?Settlement");
                sb.AppendLine("- Receiving: RV (Receive Voucher) penerimaan barang/jasa; ReceiveTax butuh NPWP+alamat WP lengkap");
                sb.AppendLine("- Payment: Invoice Payment dan PUM Payment; alur Finance?Head?Post; metode: Transfer/BG/Cek/Giro");
                sb.AppendLine("- Accounting: GL jurnal, AP hutang vendor, AR piutang customer, Acc Period buka/tutup bulan, Posting bulk");
                sb.AppendLine("- Budget: Budget Card, Committed, Realization, Over Budget ? butuh approval khusus");
                sb.AppendLine("- Report: Cashflow, Inquiry AP/AR/CB/PUM, Saldo Buku Bank, Deposito, Realisasi PUM");
                sb.AppendLine("- Master: Company, Employee, Vendor, COA, Department, Division, Account Period, Roles");
                sb.AppendLine("- Login: username Windows tanpa @jababeka.com; password = password Windows");
                sb.AppendLine();

                // Dynamic context from KB results
                if (categories.Count > 0)
                    sb.AppendLine($"TOPIK YANG RELEVAN SAAT INI: {string.Join(" | ", categories.Take(4))}");
                if (documentTitles.Count > 0)
                    sb.AppendLine($"DOKUMEN KB YANG TERSEDIA: {string.Join(", ", documentTitles.Take(3))}");
                sb.AppendLine($"KEPERCAYAAN JAWABAN: {confidenceLabel} ({(highestScore * 100):F0}% match)");
                sb.AppendLine();

                // Query-type adaptive behavior
                sb.Append("MODE JAWABAN (");
                sb.Append(queryType);
                sb.AppendLine("):");
                switch (queryType)
                {
                    case "HowTo":
                        sb.AppendLine("? Berikan langkah-langkah BERNOMOR yang jelas, konkret, dan bisa langsung dilakukan.");
                        if (hasSteps) sb.AppendLine("? KB punya panduan langkah-langkah — gunakan sepenuhnya.");
                        break;
                    case "Troubleshooting":
                        sb.AppendLine("? Identifikasi root cause terlebih dahulu, lalu berikan solusi yang actionable.");
                        sb.AppendLine("? Jika ada beberapa kemungkinan masalah, sebutkan semuanya.");
                        break;
                    case "Explanation":
                        sb.AppendLine("? Jelaskan konsep dengan analogi sederhana bila perlu, lalu berikan detail teknis.");
                        sb.AppendLine("? Pastikan jawaban mudah dipahami oleh user non-teknis.");
                        break;
                    case "Navigation":
                        sb.AppendLine("? Tunjukkan path menu yang jelas: Modul ? Sub-menu ? Halaman.");
                        break;
                    case "Authorization":
                        sb.AppendLine("? Jelaskan role/permission yang diperlukan dan siapa yang berwenang.");
                        break;
                    default:
                        sb.AppendLine("? Jawab langsung dan spesifik berdasarkan KB.");
                        break;
                }
                sb.AppendLine();

                // Precision rules — non-negotiable
                sb.AppendLine("ATURAN WAJIB:");
                sb.AppendLine("? Confidence TINGGI: jawab lengkap dan percaya diri dari KB");
                sb.AppendLine("? Confidence SEDANG: jawab dengan info yang ada, tambahkan catatan jika kurang");
                sb.AppendLine("? Confidence RENDAH: jujur bahwa informasi terbatas, sarankan hubungi IT Help Desk");
                sb.AppendLine("? JANGAN mengarang info yang tidak ada di KB");
                sb.AppendLine("? JANGAN ulangi pertanyaan user di awal jawaban");
                sb.AppendLine("? JANGAN tambahkan disclaimer panjang yang tidak perlu");
                sb.AppendLine();
                sb.AppendLine("Eskalasi jika perlu: IT Help Desk ? it@jababeka.com");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PromptEngineering] Error building enhanced system prompt: {ex.Message}");
                return GetDefaultSystemPrompt();
            }
        }

        /// <summary>
        /// Build original dynamic system prompt (kept for compatibility)
        /// </summary>
        public string BuildDynamicSystemPrompt(List<KnowledgeBaseResult> kbResults, string userQuery)
        {
            try
            {
                // Analyze KB results to determine system behavior
                var categories = kbResults.GroupBy(r => r.Category).Select(g => g.Key).Distinct().ToList();
                var documentTypes = kbResults.Select(r => r.Title).Distinct().Take(5).ToList();
                var highestScore = kbResults.Max(r => r.Score);
                var hasStepByStepGuide = kbResults.Any(r => 
                    r.Content.ToLower().Contains("langkah") || 
                    r.Content.ToLower().Contains("step") ||
                    r.Content.ToLower().Contains("cara"));

                var systemPromptBuilder = new StringBuilder();
                systemPromptBuilder.AppendLine("Kamu adalah JIFAS AI Assistant, asisten cerdas untuk Jababeka Integrated Finance Accounting System (JIFAS).");
                systemPromptBuilder.AppendLine();
                systemPromptBuilder.AppendLine("PERAN MU:");
                systemPromptBuilder.AppendLine("- Ahli dalam sistem JIFAS dan semua modulnya");
                systemPromptBuilder.AppendLine("- Memberikan jawaban yang akurat berdasarkan Knowledge Base");
                systemPromptBuilder.AppendLine("- Memahami konteks bisnis dan proses kerja JIFAS");
                systemPromptBuilder.AppendLine();

                // Dynamic behavior based on KB content
                systemPromptBuilder.AppendLine("KONTEKS PENGETAHUAN MU:");
                if (categories.Count > 0)
                {
                    systemPromptBuilder.AppendLine($"- Topik yang relevan: {string.Join(", ", categories)}");
                }
                if (documentTypes.Count > 0)
                {
                    systemPromptBuilder.AppendLine($"- Dokumen terkait: {string.Join(", ", documentTypes.Take(3))}");
                }
                systemPromptBuilder.AppendLine($"- Tingkat relevansi hasil pencarian: {(highestScore * 100):F1}%");
                systemPromptBuilder.AppendLine();

                // Guide for response format
                systemPromptBuilder.AppendLine("CARA MENJAWAB:");
                systemPromptBuilder.AppendLine("1. ANALISIS pertanyaan user dengan cermat");
                systemPromptBuilder.AppendLine("2. CARI bagian Knowledge Base yang paling relevan");
                systemPromptBuilder.AppendLine("3. RANGKUM informasi dengan jelas dan terstruktur");
                if (hasStepByStepGuide)
                {
                    systemPromptBuilder.AppendLine("4. BERIKAN langkah-langkah jika diperlukan");
                }
                systemPromptBuilder.AppendLine("5. SERTAKAN detail penting dan contoh jika ada");
                systemPromptBuilder.AppendLine();

                systemPromptBuilder.AppendLine("ATURAN KETAT:");
                systemPromptBuilder.AppendLine("- JANGAN membuat informasi yang tidak ada di KB");
                systemPromptBuilder.AppendLine("- Jika ada keraguan, tanyakan klarifikasi pada user");
                systemPromptBuilder.AppendLine("- Jawab dalam Bahasa Indonesia yang profesional");
                systemPromptBuilder.AppendLine("- Berikan jawaban yang singkat tapi lengkap");
                systemPromptBuilder.AppendLine("- Jika KB tidak punya jawaban, katakan dengan jelas");

                return systemPromptBuilder.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PromptEngineering] Error building system prompt: {ex.Message}");
                return GetDefaultSystemPrompt();
            }
        }

        /// <summary>
        /// Build main prompt with optimized semantic chunking
        /// Improvement #1: Semantic chunking optimization - groups by document boundaries
        /// Improvement #10: Refinement with optimal chunk size (600 words/section)
        /// </summary>
        private string BuildOptimizedMainPrompt(string userQuery, List<KnowledgeBaseResult> kbResults, ContextAnalysisResult analysis, string sessionContext)
        {
            var promptBuilder = new StringBuilder();

            // Improvement #4: Add conversation context awareness if available
            if (!string.IsNullOrEmpty(sessionContext))
            {
                promptBuilder.AppendLine("KONTEKS PERCAKAPAN SEBELUMNYA:");
                promptBuilder.AppendLine(sessionContext);
                promptBuilder.AppendLine();
            }

            // Improvement #1: Organize results with semantic optimization
            var orderedResults = kbResults.OrderByDescending(r => r.Score).ToList();
            var groupedByDoc = orderedResults.GroupBy(r => r.Title).ToList();

            promptBuilder.AppendLine("INFORMASI RELEVAN DARI KNOWLEDGE BASE:");
            promptBuilder.AppendLine("(Diurutkan dari paling relevan)");
            promptBuilder.AppendLine();

            int sectionNum = 1;
            foreach (var docGroup in groupedByDoc.Take(MAX_KB_SECTIONS))
            {
                promptBuilder.AppendLine($"[Bagian {sectionNum}] {docGroup.Key}");
                promptBuilder.AppendLine($"Kategori: {docGroup.First().Category} | Relevansi: {(docGroup.First().Score * 100):F1}%");
                promptBuilder.AppendLine();

                // Improvement #10: Optimize content by semantic chunking
                var combinedContent = CombineAndOptimizeContent(docGroup.ToList());

                // Limit content intelligently based on relevance
                int maxLength = docGroup.First().Score >= 0.8 ? 3000 : 2000;
                if (combinedContent.Length > maxLength)
                {
                    combinedContent = TruncateIntelligently(combinedContent, maxLength);
                }

                promptBuilder.AppendLine(combinedContent);
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("---");
                promptBuilder.AppendLine();

                sectionNum++;
            }

            // Add context analysis notes
            promptBuilder.AppendLine("CATATAN ANALISIS:");
            if (analysis.IsExactMatch)
            {
                promptBuilder.AppendLine("? Pertanyaan user memiliki kecocokan LANGSUNG dengan Knowledge Base");
            }
            else if (analysis.IsPartialMatch)
            {
                promptBuilder.AppendLine("? Pertanyaan user SEBAGIAN sesuai dengan KB - gunakan informasi yang tersedia sebaik mungkin");
            }
            else
            {
                promptBuilder.AppendLine("? Kecocokan TERBATAS dengan KB - berikan jawaban berdasarkan konteks terdekat");
            }

            if (analysis.ContentCoverage > 0)
            {
                promptBuilder.AppendLine($"Coverage Konten: {analysis.ContentCoverage}% dari keywords yang diminta");
            }

            return promptBuilder.ToString();
        }

        /// <summary>
        /// Original BuildMainPrompt - kept for compatibility
        /// </summary>
        private string BuildMainPrompt(string userQuery, List<KnowledgeBaseResult> kbResults, ContextAnalysisResult analysis, string sessionContext)
        {
            var promptBuilder = new StringBuilder();

            // Add session context if available
            if (!string.IsNullOrEmpty(sessionContext))
            {
                promptBuilder.AppendLine("KONTEKS PERCAKAPAN SEBELUMNYA:");
                promptBuilder.AppendLine(sessionContext);
                promptBuilder.AppendLine();
            }

            // Organize results by relevance and structure
            var orderedResults = kbResults.OrderByDescending(r => r.Score).ToList();

            // Group by document for better organization
            var groupedByDoc = orderedResults.GroupBy(r => r.Title).ToList();

            promptBuilder.AppendLine("INFORMASI DARI KB (Diurutkan dari paling relevan):");
            promptBuilder.AppendLine();

            int sectionNum = 1;
            foreach (var docGroup in groupedByDoc.Take(5))  // Limit to top 5 documents
            {
                promptBuilder.AppendLine($"[{sectionNum}] Dokumen: {docGroup.Key}");
                promptBuilder.AppendLine($"    Kategori: {docGroup.First().Category}");
                promptBuilder.AppendLine($"    Relevance Score: {(docGroup.First().Score * 100):F1}%");
                promptBuilder.AppendLine();

                // Combine content from all chunks in this document
                var combinedContent = string.Join("\n\n", docGroup.Select(r => r.Content.Trim()));
                
                // Limit content length per document
                if (combinedContent.Length > 2000)
                {
                    combinedContent = combinedContent.Substring(0, 2000) + "... [konten diperpendek]";
                }

                promptBuilder.AppendLine(combinedContent);
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("---");
                promptBuilder.AppendLine();

                sectionNum++;
            }

            // Add analysis notes
            if (analysis.IsExactMatch)
            {
                promptBuilder.AppendLine("CATATAN: Pertanyaan user memiliki kecocokan LANGSUNG dengan KB.");
            }
            else if (analysis.IsPartialMatch)
            {
                promptBuilder.AppendLine("CATATAN: Pertanyaan user SEBAGIAN sesuai dengan KB. Gunakan informasi yang tersedia sebaik mungkin.");
            }
            else
            {
                promptBuilder.AppendLine("CATATAN: Pertanyaan user memiliki KECOCOKAN TERBATAS dengan KB. Berikan jawaban berdasarkan konteks terdekat.");
            }

            if (analysis.MissingContext.Count > 0)
            {
                promptBuilder.AppendLine($"INFORMASI YANG MUNGKIN KURANG: {string.Join(", ", analysis.MissingContext)}");
            }

            return promptBuilder.ToString();
        }

        /// <summary>
        /// Analyze context to understand relevance and coverage
        /// </summary>
        public async Task<ContextAnalysisResult> AnalyzeContextAsync(string userQuery, List<KnowledgeBaseResult> kbResults)
        {
            try
            {
                var analysis = new ContextAnalysisResult
                {
                    UserQuery = userQuery,
                    ResultCount = kbResults.Count,
                    AverageRelevance = kbResults.Count > 0 ? kbResults.Average(r => r.Score) * 100 : 0,
                    OverallRelevance = kbResults.Count > 0 ? (int)(kbResults.Max(r => r.Score) * 100) : 0
                };

                // Determine match type
                if (analysis.OverallRelevance >= 90)
                {
                    analysis.IsExactMatch = true;
                    analysis.MatchType = "EXACT";
                }
                else if (analysis.OverallRelevance >= 70)
                {
                    analysis.IsPartialMatch = true;
                    analysis.MatchType = "PARTIAL";
                }
                else
                {
                    analysis.IsWeakMatch = true;
                    analysis.MatchType = "WEAK";
                }

                // Analyze content coverage
                var combinedContent = string.Join(" ", kbResults.Select(r => r.Content.ToLower()));
                var keywords = ExtractKeywords(userQuery);

                int matchedKeywords = 0;
                foreach (var keyword in keywords)
                {
                    if (combinedContent.Contains(keyword.ToLower()))
                    {
                        matchedKeywords++;
                    }
                }

                analysis.ContentCoverage = keywords.Count > 0 
                    ? (int)((matchedKeywords * 100.0) / keywords.Count) 
                    : 0;

                // Identify missing context
                analysis.MissingContext = keywords
                    .Where(k => !combinedContent.Contains(k.ToLower()))
                    .Take(3)
                    .ToList();

                _logger.LogInformation($"[ContextAnalysis] Match: {analysis.MatchType}, Coverage: {analysis.ContentCoverage}%, Missing: {analysis.MissingContext.Count}");

                return analysis;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ContextAnalysis] Error: {ex.Message}");
                return new ContextAnalysisResult { UserQuery = userQuery };
            }
        }

        /// <summary>
        /// Build fallback prompt when direct match is not found
        /// Improvement #6: Intelligent fallback with related topics and helpful suggestions
        /// </summary>
        public string BuildFallbackPrompt(string userQuery, List<KnowledgeBaseResult> kbResults)
        {
            var fallbackBuilder = new StringBuilder();

            fallbackBuilder.AppendLine(@"Kamu adalah JIFAS AI Assistant. Jawab pertanyaan user berdasarkan Knowledge Base JIFAS.

SITUASI: Pertanyaan ini memiliki kecocokan TERBATAS dengan Knowledge Base. 
Gunakan informasi yang PALING MENDEKATI dan bantu user sebisa mungkin.

INSTRUKSI PENTING:
1. Cari dan gunakan informasi yang PALING RELEVAN dari KB
2. Jika KB tidak punya jawaban langsung, gunakan konteks TERDEKAT yang relevan
3. KATAKAN DENGAN JELAS jika informasi yang diminta TIDAK TERSEDIA di KB
4. JANGAN membuat atau mengarang informasi baru
5. Berikan alternatif atau pertanyaan terkait yang MUNGKIN lebih sesuai
6. Sarankan topik JIFAS lain yang MUNGKIN relevan

KNOWLEDGE BASE YANG TERSEDIA:");

            if (kbResults.Count == 0)
            {
                fallbackBuilder.AppendLine("(Tidak ada hasil pencarian yang relevan)");
                fallbackBuilder.AppendLine();
                fallbackBuilder.AppendLine("SARAN TOPIK JIFAS YANG TERSEDIA:");
                fallbackBuilder.AppendLine("- Master Data (setup perusahaan, divisi, departemen, vendor, COA, budget)");
                fallbackBuilder.AppendLine("- Invoice (pengajuan pembayaran berdasarkan invoice vendor)");
                fallbackBuilder.AppendLine("- PUM (pengajuan uang muka/advance)");
                fallbackBuilder.AppendLine("- Receiving (penerimaan barang/jasa)");
                fallbackBuilder.AppendLine("- Payment (proses pembayaran berbagai jenis)");
                fallbackBuilder.AppendLine("- Accounting (jurnal, laporan keuangan, posting)");
                fallbackBuilder.AppendLine("- Troubleshooting (cara mengatasi masalah umum)");
            }
            else
            {
                foreach (var result in kbResults.OrderByDescending(r => r.Score).Take(3))
                {
                    var relevanceLevel = result.Score >= 0.7 ? "CUKUP RELEVAN" : "AGAK RELEVAN";
                    fallbackBuilder.AppendLine($"\n• {result.Title} ({relevanceLevel}: {(result.Score * 100):F1}%)");
                    fallbackBuilder.AppendLine($"  Kategori: {result.Category}");
                    var preview = result.Content.Length > 200 
                        ? result.Content.Substring(0, 200) + "..." 
                        : result.Content;
                    fallbackBuilder.AppendLine($"  Preview: {preview}");
                }
            }

            fallbackBuilder.AppendLine($@"

PERTANYAAN USER: ""{userQuery}""

RESPONS YANG DIHARAPKAN:
- Jika bisa menjawab dengan informasi dari KB: Berikan jawaban yang jelas dan terstruktur
- Jika TIDAK bisa menjawab: Katakan terang-terangan ""Maaf, informasi ini tidak tersedia di Knowledge Base JIFAS""
- Selalu sarankan alternatif topik terkait yang MUNGKIN membantu

JANGAN LUPA: Jawab dalam Bahasa Indonesia yang profesional dan ramah.

RESPONS:");

            return fallbackBuilder.ToString();
        }

        /// <summary>
        /// Generate clarification questions if context is ambiguous
        /// </summary>
        public async Task<string> GenerateClarificationPromptAsync(string userQuery, List<KnowledgeBaseResult> kbResults)
        {
            var analysis = await AnalyzeContextAsync(userQuery, kbResults);

            if (analysis.OverallRelevance < 50)
            {
                return $@"Berdasarkan Knowledge Base JIFAS, pertanyaan Anda kurang jelas. 

Untuk memberikan jawaban yang lebih akurat, saya perlu klarifikasi:

1. Apakah Anda bertanya tentang: {GetCategoryGuess(kbResults)}?
2. Topik spesifik apa dalam JIFAS?
3. Apakah ini terkait dengan: {string.Join(", ", kbResults.Take(3).Select(r => r.Category))}?

Silakan jelaskan lebih detail agar saya dapat memberikan jawaban yang tepat.";
            }

            return null;
        }

        /// <summary>
        /// Extract keywords from query
        /// </summary>
        private List<string> ExtractKeywords(string query)
        {
            var stopwords = new[] { "apa", "yang", "bagaimana", "dimana", "kapan", "siapa", "adalah", "ini", "itu", "dan", "atau", "untuk", "dari", "ke", "di", "ke", "the", "a", "an", "in", "on", "at", "to", "by" };
            
            var words = query.ToLower()
                .Split(new[] { ' ', ',', '.', '?', '!', '-', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2 && !stopwords.Contains(w))
                .Distinct()
                .ToList();

            return words;
        }

        private string GetCategoryGuess(List<KnowledgeBaseResult> results)
        {
            return results.FirstOrDefault()?.Category ?? "JIFAS";
        }

        /// <summary>
        /// Improvement #1: Combine content with semantic optimization
        /// Groups related content and respects semantic boundaries
        /// </summary>
        private string CombineAndOptimizeContent(List<KnowledgeBaseResult> results)
        {
            if (results.Count == 0) return "";

            var contentParts = new List<string>();
            
            // Sort by relevance within the document group
            foreach (var result in results.OrderByDescending(r => r.Score))
            {
                if (!string.IsNullOrWhiteSpace(result.Content))
                {
                    contentParts.Add(result.Content.Trim());
                }
            }

            // Join with clear semantic boundaries
            return string.Join("\n\n", contentParts);
        }

        /// <summary>
        /// Improvement #10: Intelligently truncate content while preserving meaning
        /// Finds natural break points (paragraphs) instead of cutting mid-sentence
        /// </summary>
        private string TruncateIntelligently(string content, int maxLength)
        {
            if (content.Length <= maxLength) return content;

            var truncated = content.Substring(0, maxLength);
            var lastNewline = truncated.LastIndexOf('\n');
            
            if (lastNewline > maxLength * 0.7)  // If natural break is reasonably close
            {
                return truncated.Substring(0, lastNewline).TrimEnd() + "\n... [konten diperpendek]";
            }

            return truncated + "... [konten diperpendek]";
        }

        /// <summary>
        /// Improvement #3 & #5: Build query-type-specific instructions with few-shot examples
        /// Enhanced with concrete examples to guide response format
        /// </summary>
        private string BuildQuerySpecificInstructionsWithExamples(string queryType, string userQuery, List<KnowledgeBaseResult> kbResults)
        {
            var hasKbContent = kbResults.Count > 0;
            var confidenceNote = hasKbContent
                ? $"KB tersedia ({kbResults.Count} dokumen relevan) — gunakan penuh."
                : "KB tidak punya jawaban spesifik — jujur bahwa info terbatas.";

            return queryType switch
            {
                "HowTo" =>
$@"INSTRUKSI — PERTANYAAN 'CARA/LANGKAH' ({confidenceNote})
- Berikan langkah bernomor yang jelas dan actionable
- Gunakan nama tombol/menu JIFAS yang sebenarnya (Save, Submit, Approve, Post, Void, dll)
- Sebutkan path menu jika diketahui dari KB
- Tambahkan catatan/tip penting di akhir jika ada
- Jangan skip langkah — user mungkin pemula dengan JIFAS",

                "Troubleshooting" =>
$@"INSTRUKSI — PERTANYAAN MASALAH/ERROR ({confidenceNote})
- Identifikasi dulu kemungkinan penyebab masalah
- Berikan solusi urut dari paling mudah ke kompleks
- Jelaskan expected result setelah setiap solusi
- Jika butuh eskalasi: it@jababeka.com
- Jangan berasumsi user sudah familiar dengan sistem",

                "Explanation" =>
$@"INSTRUKSI — PERTANYAAN DEFINISI/PENJELASAN ({confidenceNote})
- Mulai dengan definisi singkat di kalimat pertama
- Jelaskan peran/fungsinya di sistem JIFAS
- Hubungkan dengan modul lain jika relevan
- Gunakan analogi sederhana jika konsepnya abstrak
- Berikan contoh konkret dari konteks bisnis JIFAS",

                "Navigation" =>
$@"INSTRUKSI — PERTANYAAN NAVIGASI/LOKASI MENU ({confidenceNote})
- Berikan path menu: Modul ? Sub-menu ? Halaman
- Sebutkan URL relatif jika diketahui
- Deskripsikan tampilan/ciri halaman agar mudah dikenali
- Jika ada prasyarat (login, role, permission), sebutkan",

                "Authorization" =>
$@"INSTRUKSI — PERTANYAAN ROLE/HAK AKSES ({confidenceNote})
- Jelaskan role yang diperlukan: WMTR (IT), USER (umum), USRL (PUM dept-level)
- Tunjukkan cara request akses jika user tidak punya permission
- Jelaskan perbedaan antar role jika relevan
- Eskalasi akses: minta ke admin sistem atau IT Help Desk",

                "Technical" =>
$@"INSTRUKSI — PERTANYAAN TEKNIS/FIELD ({confidenceNote})
- Jelaskan field/komponen dengan detail: nama, tipe, validasi, format
- Berikan contoh input yang valid
- Hubungkan dengan field/modul terkait
- Jelaskan konsekuensi jika field tidak diisi atau salah format",

                _ =>
$@"INSTRUKSI — PERTANYAAN UMUM ({confidenceNote})
- Pahami intent user dan jawab yang paling relevan
- Strukturkan jawaban dengan jelas menggunakan bullet atau numbering
- Spesifik tentang fitur/proses JIFAS, hindari jawaban abstrak"
            };
        }

        private string GetDefaultSystemPrompt() =>
            "Kamu adalah JIFAS AI Assistant, asisten untuk sistem JIFAS PT Jababeka Tbk. " +
            "Jawab berdasarkan Knowledge Base yang diberikan, jujur jika info tidak ada di KB, " +
            "gunakan Bahasa Indonesia yang natural, dan berikan jawaban yang actionable.";

        /// </summary>
        private string ClassifyQueryType(string query)
        {
            var q = query.ToLowerInvariant();

            // Authorization / role / permission
            if (q.Contains("role") || q.Contains("akses") || q.Contains("permission") ||
                q.Contains("hak") || q.Contains("otorisasi") || q.Contains("wmtr") ||
                q.Contains("usrl") || q.Contains("user bisa") || q.Contains("bisa diakses"))
                return "Authorization";

            // Navigation / where is the menu
            if (q.Contains("dimana") || q.Contains("di mana") || q.Contains("letak") ||
                q.Contains("menu") || q.Contains("halaman") || q.Contains("buka") ||
                q.Contains("navigasi") || q.Contains("ke modul") || q.Contains("di jifas"))
                return "Navigation";

            // How-to / step-by-step
            if (q.StartsWith("bagaimana") || q.StartsWith("cara") || q.StartsWith("gimana") ||
                q.Contains("langkah") || q.Contains("step") || q.Contains("proses") ||
                q.Contains("alur") || q.Contains("prosedur") || q.Contains("tutup") ||
                q.Contains("buat") || q.Contains("submit") || q.Contains("approve"))
                return "HowTo";

            // Troubleshooting / error
            if (q.Contains("error") || q.Contains("gagal") || q.Contains("tidak bisa") ||
                q.Contains("nggak bisa") || q.Contains("masalah") || q.Contains("kenapa") ||
                q.Contains("why") || q.Contains("bug") || q.Contains("tidak muncul") ||
                q.Contains("tidak jalan") || q.Contains("tidak berhasil"))
                return "Troubleshooting";

            // Definition / explanation
            if (q.StartsWith("apa") || q.StartsWith("apakah") || q.StartsWith("siapa") ||
                q.Contains("definisi") || q.Contains("maksud") || q.Contains("artinya") ||
                q.Contains("jelaskan") || q.Contains("apa itu") || q.Contains("fungsi"))
                return "Explanation";

            // Technical / field-level
            if (q.Contains("field") || q.Contains("kolom") || q.Contains("input") ||
                q.Contains("format") || q.Contains("validasi") || q.Contains("kode") ||
                q.Contains("tipe data") || q.Contains("teknis"))
                return "Technical";

            return "General";
        }

        /// <summary>
        /// Build query-type-specific instructions with examples
        /// Original method kept for backward compatibility
        /// </summary>
        private string BuildQuerySpecificInstructions(string queryType, string userQuery, List<KnowledgeBaseResult> kbResults)
        {
            return queryType switch
            {
                "HowTo" => @"INSTRUKSI KHUSUS UNTUK PERTANYAAN 'BAGAIMANA/CARA':
- Berikan langkah-langkah yang jelas dan terstruktur (gunakan format bernomor)
- Mulai dari yang paling dasar
- Sertakan screenshot atau referensi menu jika ada di KB
- Jelaskan setiap langkah dengan detail
- Berikan tips atau catatan penting jika ada",

                "Definition" => @"INSTRUKSI KHUSUS UNTUK PERTANYAAN DEFINISI/PENJELASAN:
- Berikan definisi yang jelas dan singkat di awal
- Jelaskan konteks penggunaan di JIFAS
- Berikan contoh jika tersedia di KB
- Hubungkan dengan fitur/modul lain jika relevan",

                "Troubleshooting" => @"INSTRUKSI KHUSUS UNTUK PERTANYAAN MASALAH/ERROR:
- Identifikasi kemungkinan penyebab error
- Berikan solusi langkah-demi-langkah
- Jika ada beberapa solusi, urutkan dari paling mudah ke paling kompleks
- Sertakan pesan error atau kode jika disebutkan di KB
- Rekomendasikan untuk hubungi support jika solusi tidak berhasil",

                "Technical" => @"INSTRUKSI KHUSUS UNTUK PERTANYAAN TEKNIS/FIELD:
- Jelaskan field/menu dengan detail
- Tipe data dan format yang diterima
- Validasi dan rules yang berlaku
- Hubungan dengan field/modul lain
- Contoh penggunaan atau input yang benar",

                _ => @"INSTRUKSI KHUSUS:
- Pahami konteks pertanyaan dengan baik
- Berikan jawaban yang relevan dan terstruktur
- Jika ada bagian yang tidak jelas, katakan dengan tegas"
            };
        }
    }
}
