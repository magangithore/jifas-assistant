using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Interface for AI service - uses Local Ollama for response generation based on JIFAS Knowledge Base content
    /// </summary>
    public interface IOllamaService
    {
        /// <summary>
        /// Generate response based on knowledge base context.
        /// </summary>
        /// <param name="userQuery">The user's question.</param>
        /// <param name="kbResults">Knowledge base search results.</param>
        /// <param name="sessionContext">
        /// Optional active page context from the frontend.
        /// Format: "PAGE:{url}|MODULE:{module}|TITLE:{title}|DOC:{docId}|DOCTYPE:{type}|STATUS:{status}"
        /// </param>
        Task<string> GenerateResponseAsync(string userQuery, List<KnowledgeBaseResult> kbResults, string? sessionContext = null);

        /// <summary>
        /// Generate follow-up suggestions based on context
        /// </summary>
        Task<List<string>> GenerateSuggestionsAsync(string userQuery, string response);

        /// <summary>
        /// Check if the query is within JIFAS scope
        /// </summary>
        Task<bool> IsInScopeAsync(string userQuery);

        /// <summary>
        /// Call Ollama AI service directly with custom prompt
        /// Uses Local Ollama (qwen3:8b model) for generating natural responses
        /// </summary>
        Task<string> CallOllamaApiAsync(string prompt);

        /// <summary>
        /// Set per-call context (userId, sessionId, module, callType) so monitoring
        /// can attach identity information to each recorded metric.
        /// Must be called before GenerateResponseAsync / GenerateSuggestionsAsync.
        /// </summary>
        void SetCallContext(string? userId, string? sessionId, string? activeModule, string callType = "chat");
    }

    public class KnowledgeBaseResult
    {
        public int DocumentId { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string Category { get; set; }
        public string Department { get; set; }
        public double Score { get; set; }
        
        // NEW: Properties for Option 3 Re-ranking Service
        public DateTime? UpdatedDate { get; set; }          // For freshness scoring
        public int? ViewCount { get; set; }                 // For popularity scoring (optional)
        public bool IsOfficial { get; set; } = true;        // For confidence scoring (default true)
    }
}
