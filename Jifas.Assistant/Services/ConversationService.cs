using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Jifas.Assistant.Data;
using Jifas.Assistant.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Jifas.Assistant.Services
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
        public string Source { get; set; }
        
        /// <summary>
        /// IDs of KB documents used in this conversation (for analytics tracking)
        /// </summary>
        public List<int> UsedDocumentIds { get; set; } = new List<int>();
    }

    public class ConversationService : IConversationService
    {
        private readonly JifasAssistantDbContext _db;
        private readonly ILoggerService _logger;

        public ConversationService(JifasAssistantDbContext db, ILoggerService logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<int> LogConversationAsync(ConversationLog log)
        {
            try
            {
                if (log == null)
                {
                    _logger.LogWarning("[ConversationService] Null conversation log provided");
                    return 0;
                }

                var chat = new Chat
                {
                    UserId = log.UserId ?? "anonymous",
                    SessionId = log.SessionId ?? Guid.NewGuid().ToString(),
                    UserMessage = TruncateString(log.UserMessage, 500),
                    AssistantResponse = TruncateString(log.AiResponse, 4000),
                    Category = log.Category ?? "general",
                    ConfidenceScore = log.ConfidenceScore,
                    IsFromKnowledgeBase = log.IsFromKnowledgeBase,
                    Source = log.Source ?? "Unknown",
                    CreatedAt = DateTime.Now
                };

                _db.Chats.Add(chat);
                await _db.SaveChangesAsync();

                // Log which documents were used (for analytics/reranking)
                if (log.UsedDocumentIds != null && log.UsedDocumentIds.Count > 0)
                {
                    _logger.LogInformation(
                        "[ConversationService] Logged conversation #{0} using docs: {1}", 
                        chat.Id, 
                        string.Join(", ", log.UsedDocumentIds));
                }

                _logger.LogDebug("[ConversationService] Conversation saved with ID: {0}", chat.Id);
                return chat.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError("[ConversationService] Error logging conversation: {0}", ex, ex.Message);
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
