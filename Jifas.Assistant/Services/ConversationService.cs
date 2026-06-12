using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using jifas_assistant.DAL.Models;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Service untuk mencatat percakapan ke database.
    /// Data ini dipakai untuk audit, analytics, dan evaluasi kualitas jawaban.
    /// </summary>
    public interface IConversationService
    {
        Task<int> LogConversationAsync(ConversationLog log);
    }

    public class ConversationLog
    {
        public string UserId { get; set; } = "anonymous";
        public string SessionId { get; set; } = string.Empty;
        public string UserMessage { get; set; } = string.Empty;
        public string AiResponse { get; set; } = string.Empty;
        public string Category { get; set; } = "General";
        public double? ConfidenceScore { get; set; }
        public bool IsFromKnowledgeBase { get; set; }
        
        /// <summary>
        /// ID dokumen KB yang dipakai untuk menjawab percakapan ini.
        /// </summary>
        public List<int> UsedDocumentIds { get; set; } = new List<int>();
    }

    public class ConversationService : IConversationService
    {
        private readonly JIFAS_AssistantContext _db;
        private readonly ILoggerService _logger;

        public ConversationService(JIFAS_AssistantContext db, ILoggerService logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<int> LogConversationAsync(ConversationLog log)
        {
            try
            {
                var conversation = new Chats
                {
                    UserId = log.UserId ?? "anonymous",
                    Message = TruncateString(log.UserMessage, 2000),
                    Response = TruncateString(log.AiResponse, 3000),
                    IsOutOfScope = !log.IsFromKnowledgeBase,
                    Confidence = log.ConfidenceScore,
                    RelatedDocumentIds = log.UsedDocumentIds?.Count > 0 
                        ? string.Join(",", log.UsedDocumentIds) 
                        : null,
                    CreatedAt = DateTime.UtcNow
                };

                _db.Chats.Add(conversation);
                await _db.SaveChangesAsync();

                if (log.UsedDocumentIds != null && log.UsedDocumentIds.Count > 0)
                {
                    _logger.LogInformation(
                        $"[ConversationService] Logged conversation #{conversation.Id} using docs: {string.Join(", ", log.UsedDocumentIds)}");
                }

                return conversation.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ConversationService] Error logging conversation: {ex.Message}");
                return 0;
            }
        }

        private string TruncateString(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}
