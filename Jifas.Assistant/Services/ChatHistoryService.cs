using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using jifas_assistant.DAL.Models;
using Newtonsoft.Json;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Service untuk manage chat history dan conversation tracking
    /// </summary>
    public interface IChatHistoryService
    {
        Task SaveChatAsync(ChatHistory chatHistory);
        Task<List<ChatHistory>> GetSessionHistoryAsync(string sessionId, int limit = 50);
        Task<List<ChatHistory>> GetUserHistoryAsync(string userId, int limit = 100);
    }

    public class ChatHistoryService : IChatHistoryService
    {
        private readonly JIFAS_AssistantContext _db;
        private readonly ILoggerService _logger;

        public ChatHistoryService(JIFAS_AssistantContext db, ILoggerService logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Simpan chat history ke database
        /// </summary>
        public async Task SaveChatAsync(ChatHistory chatHistory)
        {
            try
            {
                if (chatHistory == null)
                {
                    _logger.LogWarning("[ChatHistoryService] Attempted to save null ChatHistory");
                    return;
                }

                // Ensure required fields
                if (string.IsNullOrWhiteSpace(chatHistory.SessionId))
                {
                    chatHistory.SessionId = Guid.NewGuid().ToString();
                }

                chatHistory.CreatedAt = DateTime.UtcNow;

                _db.ChatHistories.Add(chatHistory);
                await _db.SaveChangesAsync();

                _logger.LogInformation($"[ChatHistoryService] Chat saved - Session: {chatHistory.SessionId}, ResponseTime: {chatHistory.ResponseTimeMs}ms");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ChatHistoryService] Error saving chat history: {ex.Message}");
                // Don't throw - allow application to continue even if history save fails
            }
        }

        /// <summary>
        /// Get chat history untuk specific session
        /// </summary>
        public async Task<List<ChatHistory>> GetSessionHistoryAsync(string sessionId, int limit = 50)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    return new List<ChatHistory>();
                }

                var history = await _db.ChatHistories
                    .Where(h => h.SessionId == sessionId)
                    .OrderByDescending(h => h.CreatedAt)
                    .Take(limit)
                    .ToListAsync();

                _logger.LogInformation($"[ChatHistoryService] Retrieved {history.Count} records for session {sessionId}");
                return history;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ChatHistoryService] Error retrieving session history: {ex.Message}");
                return new List<ChatHistory>();
            }
        }

        /// <summary>
        /// Get chat history untuk specific user
        /// </summary>
        public async Task<List<ChatHistory>> GetUserHistoryAsync(string userId, int limit = 100)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return new List<ChatHistory>();
                }

                var history = await _db.ChatHistories
                    .Where(h => h.UserId == userId)
                    .OrderByDescending(h => h.CreatedAt)
                    .Take(limit)
                    .ToListAsync();

                _logger.LogInformation($"[ChatHistoryService] Retrieved {history.Count} records for user {userId}");
                return history;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ChatHistoryService] Error retrieving user history: {ex.Message}");
                return new List<ChatHistory>();
            }
        }
    }
}
