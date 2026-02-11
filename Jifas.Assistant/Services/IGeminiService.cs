using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Interface for Gemini AI service - used ONLY for summarization and response generation
    /// based on JIFAS Knowledge Base content
    /// </summary>
    public interface IGeminiService
    {
        /// <summary>
        /// Generate response based on knowledge base context
        /// </summary>
        Task<string> GenerateResponseAsync(string userQuery, List<KnowledgeBaseResult> kbResults);

        /// <summary>
        /// Generate follow-up suggestions based on context
        /// </summary>
        Task<List<string>> GenerateSuggestionsAsync(string userQuery, string response);

        /// <summary>
        /// Check if the query is within JIFAS scope
        /// </summary>
        Task<bool> IsInScopeAsync(string userQuery);
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
