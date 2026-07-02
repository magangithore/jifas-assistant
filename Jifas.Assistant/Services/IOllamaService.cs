using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Kontrak service AI untuk membuat jawaban berdasarkan hasil Knowledge Base JIFAS.
    /// </summary>
    public interface IOllamaService
    {
        /// <summary>
        /// Buat jawaban berdasarkan query user dan konteks KB.
        /// </summary>
        /// <param name="userQuery">The user's question.</param>
        /// <param name="kbResults">Knowledge base search results.</param>
        /// <param name="sessionContext">
        /// Optional active page context from the frontend.
        /// Format: "PAGE:{url}|MODULE:{module}|TITLE:{title}|DOC:{docId}|DOCTYPE:{type}|STATUS:{status}"
        /// </param>
        Task<string> GenerateResponseAsync(string userQuery, List<KnowledgeBaseResult> kbResults, string? sessionContext = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Cek apakah pertanyaan masih berada dalam scope JIFAS.
        /// </summary>
        Task<bool> IsInScopeAsync(string userQuery);

        /// <summary>
        /// Panggil model AI langsung dengan prompt custom.
        /// </summary>
        Task<string> CallOllamaApiAsync(string prompt, CancellationToken cancellationToken = default);

        /// <summary>
        /// Set konteks per panggilan agar monitoring bisa menyimpan identitas request.
        /// </summary>
        void SetCallContext(string? userId, string? sessionId, string? activeModule, string callType = "chat");

        /// <summary>
        /// Single-pass conversational response: history + RAG + scope/format rules in one Ollama call.
        /// The model autonomously decides intent (follow-up/clarification/greeting/OOS/new topic).
        /// </summary>
        Task<string> GenerateConversationalResponseAsync(
            string userQuery,
            List<KnowledgeBaseResult> kbResults,
            List<(string UserMessage, string AssistantResponse)> conversationHistory,
            string? activePageContext = null,
            string? userId = null,
            string? runningSummary = null,
            CancellationToken cancellationToken = default);
    }

    public class KnowledgeBaseResult
    {
        public int DocumentId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Category { get; set; } = "General";
        public string Department { get; set; } = string.Empty;
        public double Score { get; set; }
        
        // Metadata untuk re-ranking dan confidence scoring.
        public DateTime? UpdatedDate { get; set; }
        public int? ViewCount { get; set; }
        public bool IsOfficial { get; set; } = true;
    }
}
