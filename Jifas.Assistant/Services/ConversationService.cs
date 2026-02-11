using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Jifas.Chatbot.DAL;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Service for logging conversations to database
    /// Tracks which KB documents were used in each conversation for analytics
    /// </summary>
    public interface IConversationService
    {
        /// <summary>
        /// Log a conversation exchange
        /// </summary>
        Task<int> LogConversationAsync(ConversationLog log);
    }

    public class ConversationLog
    {
        public string UserId { get; set; }
        public string SessionId { get; set; }
        public string UserMessage { get; set; }
        public string AiResponse { get; set; }
        public string Category { get; set; }
        public double? ConfidenceScore { get; set; }
        public bool IsFromKnowledgeBase { get; set; }
        
        /// <summary>
        /// IDs of KB documents used in this conversation (for reranking popularity tracking)
        /// </summary>
        public List<int> UsedDocumentIds { get; set; } = new List<int>();
    }

    public class ConversationService : IConversationService
    {
        private readonly JIFAS_AssistantEntities _db;

        public ConversationService()
        {
            _db = new JIFAS_AssistantEntities();
        }

        public ConversationService(JIFAS_AssistantEntities db)
        {
            _db = db;
        }

        public async Task<int> LogConversationAsync(ConversationLog log)
        {
            try
            {
                var conversation = new Conversations
                {
                    UserId = log.UserId ?? "anonymous",
                    SessionId = log.SessionId ?? Guid.NewGuid().ToString(),
                    UserMessage = TruncateString(log.UserMessage, 2000),
                    AiResponse = TruncateString(log.AiResponse, 3000),
                    Category = log.Category ?? "general",
                    ConfidenceScore = log.ConfidenceScore,
                    IsFromKnowledgeBase = log.IsFromKnowledgeBase,
                    CreatedAt = DateTime.Now
                };

                _db.Conversations.Add(conversation);
                await _db.SaveChangesAsync();

                // Log which documents were used (for analytics/reranking)
                if (log.UsedDocumentIds != null && log.UsedDocumentIds.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ConversationService] Logged conversation #{conversation.Id} using docs: {string.Join(", ", log.UsedDocumentIds)}");
                }

                return conversation.Id;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConversationService] Error: {ex.Message}");
                return 0;
            }
        }

        private string TruncateString(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}
