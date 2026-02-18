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

        public PromptEngineeringService(ILoggerService logger, IKnowledgeBaseSearchService searchService)
        {
            _logger = logger;
            _searchService = searchService;
        }

        /// <summary>
        /// Build intelligent prompt by analyzing KB content and user intent
        /// FIX #4: Query-type-specific prompts untuk better accuracy
        /// </summary>
        public async Task<string> BuildIntelligentPromptAsync(string userQuery, List<KnowledgeBaseResult> kbResults, string sessionContext = null)
        {
            try
            {
                _logger.LogInformation($"[PromptEngineering] Building intelligent prompt for: {userQuery}");

                // FIX #4: Classify query type for smarter prompt engineering
                var queryType = ClassifyQueryType(userQuery);
                _logger.LogInformation($"[PromptEngineering] Query classified as: {queryType}");

                // Step 1: Analyze the KB results to understand what we have
                var contextAnalysis = await AnalyzeContextAsync(userQuery, kbResults);
                _logger.LogInformation($"[PromptEngineering] Context analysis - Relevance: {contextAnalysis.OverallRelevance}%, Coverage: {contextAnalysis.ContentCoverage}");

                // Step 2: Build dynamic system prompt based on KB analysis
                var systemPrompt = BuildDynamicSystemPrompt(kbResults, userQuery);

                // Step 3: Build the main prompt with proper context hierarchy
                var mainPrompt = BuildMainPrompt(userQuery, kbResults, contextAnalysis, sessionContext);

                // FIX #4: Build query-type-specific instructions
                var querySpecificInstructions = BuildQuerySpecificInstructions(queryType, userQuery, kbResults);

                // Step 4: Combine system + main prompt with enhanced instructions
                var finalPrompt = $@"{systemPrompt}

=== KONTEKS DARI KNOWLEDGE BASE JIFAS ===
{mainPrompt}
=== END KONTEKS ===

PERTANYAAN USER: ""{userQuery}""

{querySpecificInstructions}

INSTRUKSI UMUM JAWABAN:
1. Gunakan HANYA informasi dari Knowledge Base di atas
2. Jika ada beberapa bagian yang relevan, gabungkan menjadi jawaban lengkap
3. Format jawaban ramah namun profesional
4. Jika informasi tidak cukup atau ada yang kurang jelas, katakan dengan terus terang
5. Jika ada referensi ke dokumen atau menu, sebutkan nama lengkapnya

CONFIDENCE GUIDELINE:
- [DARI KB] untuk informasi yang jelas dari Knowledge Base
- [PARTIAL] jika hanya sebagian informasi ditemukan
- [KURANG JELAS] jika ada gap atau ambiguitas
- JANGAN PERNAH guess atau extrapolate beyond KB

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
        /// Build dynamic system prompt based on what's in the KB results
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
        /// Build main prompt with hierarchical content from KB
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
        /// </summary>
        public string BuildFallbackPrompt(string userQuery, List<KnowledgeBaseResult> kbResults)
        {
            var fallbackBuilder = new StringBuilder();

            fallbackBuilder.AppendLine(@"Kamu adalah JIFAS AI Assistant. Jawab pertanyaan user berdasarkan Knowledge Base JIFAS.

INSTRUKSI PENTING:
1. Cari informasi yang paling relevan dari KB
2. Jika KB tidak punya jawaban langsung, gunakan konteks terdekat
3. Katakan dengan jelas jika informasi tidak tersedia di KB
4. Berikan referensi ke bagian KB yang digunakan
5. Jangan membuat informasi baru

KNOWLEDGE BASE YANG TERSEDIA:");

            if (kbResults.Count == 0)
            {
                fallbackBuilder.AppendLine("(Tidak ada hasil pencarian yang relevan)");
            }
            else
            {
                foreach (var result in kbResults.OrderByDescending(r => r.Score).Take(3))
                {
                    fallbackBuilder.AppendLine($"\n- {result.Title} (Relevance: {(result.Score * 100):F1}%)");
                    fallbackBuilder.AppendLine($"  Content: {result.Content.Substring(0, Math.Min(300, result.Content.Length))}...");
                }
            }

            fallbackBuilder.AppendLine($@"

PERTANYAAN USER: ""{userQuery}""

Jawab dengan jelas berdasarkan informasi di atas. Jika tidak tahu, katakan ""Informasi ini tidak tersedia di Knowledge Base JIFAS.""

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
        /// FIX #4: Build query-type-specific instructions
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
}
