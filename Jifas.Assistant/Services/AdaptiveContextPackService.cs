using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jifas.Assistant.Models;

namespace Jifas.Assistant.Services
{
    public class AdaptiveContextPack
    {
        public string IntentLabel { get; set; } = "General";
        public string Topic { get; set; } = "JIFAS";
        public string AnswerMode { get; set; } = "Direct";
        public string GroundingPolicy { get; set; } = "Use available JIFAS reference information only.";
        public string FormattedContext { get; set; } = string.Empty;
    }

    public interface IAdaptiveContextPackService
    {
        Task<AdaptiveContextPack> BuildAsync(
            ChatRequest? request,
            string userMessage,
            IntentResult intent,
            ExpandedQuery? expandedQuery,
            IReadOnlyList<KnowledgeBaseResult> kbResults,
            string? conversationContext,
            bool isFollowUp,
            string? activePageContext,
            double confidenceScore);
    }

    /// <summary>
    /// Builds a compact, structured briefing for the model before answer generation.
    /// This keeps long prompts useful by separating goal, context, evidence, and constraints.
    /// </summary>
    public class AdaptiveContextPackService : IAdaptiveContextPackService
    {
        private readonly IUserMemoryService _userMemory;
        private readonly ILoggerService _logger;

        public AdaptiveContextPackService(IUserMemoryService userMemory, ILoggerService logger)
        {
            _userMemory = userMemory;
            _logger = logger;
        }

        public async Task<AdaptiveContextPack> BuildAsync(
            ChatRequest? request,
            string userMessage,
            IntentResult intent,
            ExpandedQuery? expandedQuery,
            IReadOnlyList<KnowledgeBaseResult> kbResults,
            string? conversationContext,
            bool isFollowUp,
            string? activePageContext,
            double confidenceScore)
        {
            try
            {
                var topResults = kbResults?
                    .Where(r => r != null)
                    .OrderByDescending(r => r.Score)
                    .Take(5)
                    .ToList() ?? new List<KnowledgeBaseResult>();

                var userContext = await _userMemory.BuildUserContextForPromptAsync(request?.UserId ?? "anonymous");
                var inferredTopic = InferTopic(userMessage, topResults, request?.Context?.ActiveModule);
                var answerMode = GetAnswerMode(intent.Intent, confidenceScore, topResults);
                var groundingPolicy = GetGroundingPolicy(confidenceScore, topResults.Count);

                var sb = new StringBuilder();
                sb.AppendLine("=== CONTEXT PACK ===");
                sb.AppendLine("Tujuan: bantu user menyelesaikan pertanyaan JIFAS secara akurat, praktis, dan natural.");
                sb.AppendLine($"Intent: {intent.Intent} ({intent.Confidence:P0})");
                sb.AppendLine($"Topik utama: {inferredTopic}");
                sb.AppendLine($"Mode jawaban: {answerMode}");
                sb.AppendLine($"Confidence internal: {confidenceScore:P0}");
                sb.AppendLine($"Kebijakan grounding: {groundingPolicy}");

                if (expandedQuery != null)
                {
                    var keywords = expandedQuery.Keywords.Take(8).ToList();
                    var synonyms = expandedQuery.Synonyms.Take(8).ToList();
                    if (keywords.Count > 0)
                        sb.AppendLine($"Kata kunci user: {string.Join(", ", keywords)}");
                    if (synonyms.Count > 0)
                        sb.AppendLine($"Sinonim pencarian: {string.Join(", ", synonyms)}");
                }

                if (!string.IsNullOrWhiteSpace(activePageContext))
                {
                    sb.AppendLine();
                    sb.AppendLine("Konteks halaman aktif:");
                    sb.AppendLine(activePageContext);
                }

                if (!string.IsNullOrWhiteSpace(userContext))
                {
                    sb.AppendLine();
                    sb.AppendLine(userContext.Trim());
                }

                if (isFollowUp && !string.IsNullOrWhiteSpace(conversationContext))
                {
                    sb.AppendLine();
                    sb.AppendLine("Konteks follow-up:");
                    sb.AppendLine(TrimToLength(conversationContext, 1800));
                }

                if (topResults.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Peta referensi teratas:");
                    foreach (var result in topResults)
                    {
                        sb.AppendLine($"- {result.Title} | {result.Category} | score {(result.Score * 100):F0}%");
                    }
                }

                sb.AppendLine();
                sb.AppendLine("Checklist jawaban:");
                sb.AppendLine("- Jawab langsung pada kebutuhan user, tanpa menyebut Knowledge Base atau KB.");
                sb.AppendLine("- Jika intent how-to, berikan langkah bernomor dan nama tombol/menu yang tersedia.");
                sb.AppendLine("- Jika intent troubleshooting, mulai dari kemungkinan penyebab lalu solusi berurutan.");
                sb.AppendLine("- Jika informasi kurang, katakan batasannya dan arahkan ke IT Help Desk: it@jababeka.com.");
                sb.AppendLine("- Jangan tampilkan proses analisis internal.");
                sb.AppendLine("=== END CONTEXT PACK ===");

                return new AdaptiveContextPack
                {
                    IntentLabel = intent.Intent.ToString(),
                    Topic = inferredTopic,
                    AnswerMode = answerMode,
                    GroundingPolicy = groundingPolicy,
                    FormattedContext = sb.ToString()
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AdaptiveContextPack] Failed to build context pack: {ex.Message}");
                return new AdaptiveContextPack
                {
                    IntentLabel = intent.Intent.ToString(),
                    Topic = request?.Context?.ActiveModule ?? "JIFAS",
                    FormattedContext = string.Empty
                };
            }
        }

        private static string InferTopic(string userMessage, IReadOnlyList<KnowledgeBaseResult> results, string? activeModule)
        {
            if (!string.IsNullOrWhiteSpace(activeModule))
                return activeModule;

            var category = results
                .Where(r => !string.IsNullOrWhiteSpace(r.Category))
                .GroupBy(r => r.Category)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Max(r => r.Score))
                .Select(g => g.Key)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(category))
                return category;

            var lower = userMessage.ToLowerInvariant();
            if (lower.Contains("invoice")) return "Invoice";
            if (lower.Contains("payment") || lower.Contains("bayar")) return "Payment";
            if (lower.Contains("pum") || lower.Contains("uang muka")) return "PUM";
            if (lower.Contains("budget") || lower.Contains("anggaran")) return "Budget";
            if (lower.Contains("approval") || lower.Contains("approve")) return "Approval";
            if (lower.Contains("report") || lower.Contains("laporan")) return "Report";
            return "JIFAS";
        }

        private static string GetAnswerMode(IntentType intent, double confidenceScore, IReadOnlyList<KnowledgeBaseResult> results)
        {
            if (confidenceScore < 0.35 || results.Count == 0)
                return "Careful fallback with escalation";

            return intent switch
            {
                IntentType.HowTo => "Step-by-step operator guidance",
                IntentType.Troubleshooting => "Root-cause troubleshooting",
                IntentType.Navigation => "Menu/path navigation",
                IntentType.Status => "Workflow/status explanation",
                IntentType.TicketRequest => "Ticket handoff",
                _ => "Direct concise explanation"
            };
        }

        private static string GetGroundingPolicy(double confidenceScore, int resultCount)
        {
            if (resultCount == 0)
                return "Tidak ada referensi spesifik; jawab hanya dari pengetahuan domain JIFAS yang aman dan sarankan eskalasi bila perlu.";
            if (confidenceScore < 0.5)
                return "Referensi lemah; gunakan konteks terdekat, beri batasan jawaban, dan jangan mengarang detail.";
            return "Gunakan referensi teratas sebagai sumber utama dan jawab dengan percaya diri.";
        }

        private static string TrimToLength(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value;

            var cut = value.Substring(0, maxLength);
            var lastBreak = Math.Max(cut.LastIndexOf('\n'), cut.LastIndexOf('.'));
            if (lastBreak > maxLength * 0.65)
                return cut.Substring(0, lastBreak + 1).TrimEnd() + "\n... [context diperpendek]";

            return cut.TrimEnd() + "\n... [context diperpendek]";
        }
    }
}
