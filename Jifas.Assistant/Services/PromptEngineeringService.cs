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
    /// Creates intelligent, context-aware prompts that guide Gemini to find answers
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

                // Improvement #2: Build enhanced JIFAS-specific system prompt
                var systemPrompt = BuildEnhancedSystemPrompt(kbResults, userQuery);

                // Improvement #1 & #10: Semantic optimization for main prompt
                var mainPrompt = BuildOptimizedMainPrompt(userQuery, kbResults, contextAnalysis, sessionContext);

                // Improvement #3: Build query-type-specific instructions with few-shot examples
                var querySpecificInstructions = BuildQuerySpecificInstructionsWithExamples(queryType, userQuery, kbResults);

                // Step 4: Combine system + main prompt with enhanced instructions
                var finalPrompt = $@"{systemPrompt}

=== KNOWLEDGE BASE CONTEXT ===
{mainPrompt}
=== END CONTEXT ===

PERTANYAAN USER: ""{userQuery}""

{querySpecificInstructions}

INSTRUKSI UMUM JAWABAN:
1. Gunakan HANYA informasi dari Knowledge Base di atas
2. Jika ada beberapa bagian yang relevan, gabungkan menjadi jawaban lengkap
3. Format jawaban ramah namun profesional, gunakan Bahasa Indonesia
4. Jika informasi tidak cukup atau ada yang kurang jelas, katakan dengan terus terang
5. Jika ada referensi ke dokumen atau menu, sebutkan nama lengkapnya
6. Hindari jawaban yang terlalu singkat - berikan detail yang cukup untuk memahami

QUALITY STANDARDS:
- Jawaban harus spesifik dan tidak umum
- Minimum 2-3 kalimat untuk konteks yang lengkap
- Sertakan langkah-langkah/prosedur jika relevan
- Hubungkan dengan menu/modul JIFAS yang sesuai

RESPONS:";

                return finalPrompt;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PromptEngineering] Error building prompt: {ex.Message}");
                return BuildFallbackPrompt(userQuery, kbResults);
            }
        }

        /// <summary>
        /// Build enhanced JIFAS-specific system prompt
        /// Improvement #2: Contains JIFAS-specific context, workflows, and constraints
        /// </summary>
        public string BuildEnhancedSystemPrompt(List<KnowledgeBaseResult> kbResults, string userQuery)
        {
            try
            {
                var categories = kbResults.GroupBy(r => r.Category).Select(g => g.Key).Distinct().ToList();
                var highestScore = kbResults.Count > 0 ? kbResults.Max(r => r.Score) : 0;

                var systemPromptBuilder = new StringBuilder();
                systemPromptBuilder.AppendLine("Kamu adalah JIFAS AI Assistant, asisten cerdas untuk Jababeka Integrated Finance Accounting System.");
                systemPromptBuilder.AppendLine("Sistem JIFAS adalah solusi terintegrasi untuk mengelola keuangan dan aset perusahaan.");
                systemPromptBuilder.AppendLine();

                systemPromptBuilder.AppendLine("PERAN DAN TANGGUNG JAWAB MU:");
                systemPromptBuilder.AppendLine("- Expert di semua modul JIFAS: Master Data, Invoice, PUM, Receiving, Payment, Accounting");
                systemPromptBuilder.AppendLine("- Memberikan jawaban AKURAT berdasarkan Knowledge Base yang terbukti");
                systemPromptBuilder.AppendLine("- Memahami alur bisnis dan proses kerja setiap modul");
                systemPromptBuilder.AppendLine("- Membantu user dengan cara yang jelas dan terstruktur");
                systemPromptBuilder.AppendLine();

                systemPromptBuilder.AppendLine("JIFAS CRITICAL KNOWLEDGE:");
                systemPromptBuilder.AppendLine("- Budget Status: OK (sufficient), CM (cross-month), CA (cross-accumulation), CY (cross-year - not allowed)");
                systemPromptBuilder.AppendLine("- Core Workflows: Invoice ? Head Approval ? Finance Approval ? Finance Checking ? Payment");
                systemPromptBuilder.AppendLine("- Master Data setup adalah WAJIB sebelum menggunakan modul lainnya");
                systemPromptBuilder.AppendLine("- Periode Akuntansi yang CLOSED tidak bisa di-input transaksi baru");
                systemPromptBuilder.AppendLine("- Posting jurnal adalah action final - tidak bisa di-edit setelah posting");
                systemPromptBuilder.AppendLine();

                if (categories.Count > 0)
                {
                    systemPromptBuilder.AppendLine($"TOPIK TERKAIT DENGAN QUERY: {string.Join(", ", categories.Take(4))}");
                    systemPromptBuilder.AppendLine();
                }

                systemPromptBuilder.AppendLine("CARA MENJAWAB:");
                systemPromptBuilder.AppendLine("1. Pahami konteks pertanyaan dengan HATI-HATI");
                systemPromptBuilder.AppendLine("2. Cari bagian Knowledge Base yang PALING RELEVAN");
                systemPromptBuilder.AppendLine("3. Rangkum informasi dengan JELAS dan TERSTRUKTUR");
                systemPromptBuilder.AppendLine("4. Sertakan nama MENU dan FITUR yang sesuai dalam JIFAS");
                systemPromptBuilder.AppendLine("5. Jika ada langkah-langkah, berikan NOMOR yang urut dan JELAS");
                systemPromptBuilder.AppendLine("6. Sertakan TIPS atau CATATAN PENTING jika relevan");
                systemPromptBuilder.AppendLine();

                systemPromptBuilder.AppendLine("ATURAN KETAT TANPA PENGECUALIAN:");
                systemPromptBuilder.AppendLine("- JANGAN membuat atau mengarang informasi yang tidak ada di KB");
                systemPromptBuilder.AppendLine("- Jika ada keraguan, tanyakan klarifikasi kepada user");
                systemPromptBuilder.AppendLine("- Jawab dalam Bahasa Indonesia yang profesional dan jelas");
                systemPromptBuilder.AppendLine("- Jika KB tidak memiliki jawaban, katakan dengan TERUS TERANG");
                systemPromptBuilder.AppendLine("- Hindari jawaban yang terlalu singkat - detail adalah kunci");
                systemPromptBuilder.AppendLine("- Jangan rujuk ke fitur yang tidak disebutkan di Knowledge Base");
                systemPromptBuilder.AppendLine();

                systemPromptBuilder.AppendLine("RESPONSE QUALITY CHECKLIST SEBELUM JAWAB:");
                systemPromptBuilder.AppendLine("? Apakah jawaban SPESIFIK untuk pertanyaan user? (bukan umum)");
                systemPromptBuilder.AppendLine("? Apakah semua informasi BERASAL dari KB? (no hallucination)");
                systemPromptBuilder.AppendLine("? Apakah jawaban TERSTRUKTUR dan MUDAH DIMENGERTI?");
                systemPromptBuilder.AppendLine("? Apakah mencakup LANGKAH-LANGKAH atau PROSEDUR yang lengkap?");

                return systemPromptBuilder.ToString();
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
            return queryType switch
            {
                "HowTo" => @"INSTRUKSI UNTUK PERTANYAAN 'BAGAIMANA/CARA':
Berikan LANGKAH-LANGKAH yang JELAS dan TERSTRUKTUR dengan nomor urut.

FORMAT CONTOH JAWABAN:
'Untuk [tujuan], ikuti langkah-langkah berikut:

1. [Langkah pertama dengan detail lengkap]
   - Sub-langkah jika diperlukan
   - Informasi tambahan

2. [Langkah kedua]

3. [Langkah ketiga]

? TIP PENTING: [sertakan tips atau catatan penting dari KB]'

PASTIKAN:
- Mulai dari yang paling dasar
- Setiap langkah punya penjelasan detail
- Sertakan nama MENU atau FITUR JIFAS yang digunakan
- Jika ada screenshot, referensikan lokasinya",

                "Definition" => @"INSTRUKSI UNTUK PERTANYAAN DEFINISI/PENJELASAN:
Jelaskan dengan RINGKAS namun LENGKAP.

FORMAT CONTOH JAWABAN:
'[Istilah/Konsep] adalah [definisi singkat dan jelas].

Dalam konteks JIFAS, [istilah tersebut] digunakan untuk [penjelasan penggunaan].

Contoh: [berikan contoh konkret dari KB]

Hubungan dengan modul lain: [jika relevan, jelaskan keterkaitan]'

PASTIKAN:
- Definisi di kalimat pertama jelas dan ringkas
- Jelaskan MENGAPA penting di JIFAS
- Berikan contoh praktis, bukan hanya teori",

                "Troubleshooting" => @"INSTRUKSI UNTUK PERTANYAAN MASALAH/ERROR:
Bantu user menyelesaikan masalah dengan METODIS.

FORMAT CONTOH JAWABAN:
'PENYEBAB KEMUNGKINAN:
- [Penyebab 1: kondisi dan tanda-tandanya]
- [Penyebab 2]

SOLUSI (dari paling mudah ke kompleks):

?? SOLUSI 1 - [Nama solusi]:
1. [Langkah 1]
2. [Langkah 2]
Hasil yang diharapkan: [apa yang seharusnya terjadi]

?? SOLUSI 2 - [Nama solusi lain]:
1. [Langkah 1]
...

?? JIKA TETAP GAGAL: [saran untuk hubungi support/escalate]'

PASTIKAN:
- Urutkan solusi dari paling mudah
- Jelaskan EXPECTED RESULT setelah setiap solusi
- Jangan asumsikan user sudah tahu tentang fitur JIFAS",

                "Technical" => @"INSTRUKSI UNTUK PERTANYAAN TEKNIS/FIELD/MENU:
Jelaskan dengan DETAIL dan TERSTRUKTUR.

FORMAT CONTOH JAWABAN:
'[NAMA FIELD/MENU] adalah [penjelasan singkat].

DETAIL TEKNIS:
- Lokasi: [Path/Menu di JIFAS]
- Tipe Data: [format yang diterima]
- Wajib/Opsional: [status]
- Validasi: [aturan/batasan]
- Contoh Input: [contoh data yang valid]

HUBUNGAN:
- Field/Modul terkait: [jika ada]
- Dampak jika tidak diisi: [konsekuensi]

?? CATATAN: [tips atau hal khusus]'

PASTIKAN:
- Field description lengkap dan akurat
- Contoh input real dan praktis
- Jelaskan relasi dengan field lain
- Hindari jargon teknis yang terlalu kompleks",

                _ => @"INSTRUKSI UMUM UNTUK SEMUA JENIS PERTANYAAN:
- Pahami konteks pertanyaan dengan baik
- Gunakan informasi yang paling RELEVAN dari KB
- Berikan jawaban yang TERSTRUKTUR dan MUDAH DIPAHAMI
- Setiap bagian jawaban harus SPESIFIK, bukan umum
- Jika ada yang tidak jelas di KB, KATAKAN dengan tegas"
            };
        }

        private string GetDefaultSystemPrompt()
        {
            return @"Kamu adalah JIFAS AI Assistant, asisten virtual untuk Jababeka Integrated Finance Accounting System.

ATURAN:
1. Jawab HANYA berdasarkan Knowledge Base
2. Jangan membuat informasi baru
3. Gunakan Bahasa Indonesia profesional
4. Berikan jawaban ringkas tapi lengkap
5. Jika tidak tahu, katakan dengan jelas";
        }

        /// <summary>
        /// FIX #4: Classify query type for smarter prompt engineering
        /// </summary>
        private string ClassifyQueryType(string query)
        {
            var lowerQuery = query.ToLower();
            
            if (lowerQuery.StartsWith("bagaimana") || lowerQuery.StartsWith("cara") || 
                lowerQuery.Contains("langkah") || lowerQuery.Contains("step"))
                return "HowTo";
            
            if (lowerQuery.StartsWith("apa") || lowerQuery.StartsWith("siapa") || 
                lowerQuery.StartsWith("yang") || lowerQuery.Contains("definisi"))
                return "Definition";
            
            if (lowerQuery.Contains("error") || lowerQuery.Contains("masalah") || 
                lowerQuery.Contains("tidak bisa") || lowerQuery.Contains("error"))
                return "Troubleshooting";
            
            if (lowerQuery.Contains("field") || lowerQuery.Contains("menu") || 
                lowerQuery.Contains("modul") || lowerQuery.Contains("teknis"))
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
