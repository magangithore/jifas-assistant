using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        Task SaveChatAsync(ChatHistory chatHistory, CancellationToken cancellationToken = default);
        Task<List<ChatHistory>> GetSessionHistoryAsync(string sessionId, string? userId, int limit = 50, CancellationToken cancellationToken = default);
        Task<List<ChatHistory>> GetUserHistoryAsync(string userId, int limit = 100, CancellationToken cancellationToken = default);
    }

    public class ChatHistoryService : IChatHistoryService
    {
        private readonly IDbContextFactory<JIFAS_AssistantContext> _dbFactory;
        private readonly ILoggerService _logger;

        public ChatHistoryService(IDbContextFactory<JIFAS_AssistantContext> dbFactory, ILoggerService logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
        }

        /// <summary>
        /// Simpan chat history ke database
        /// </summary>
        public async Task SaveChatAsync(ChatHistory chatHistory, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

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

                await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
                db.ChatHistories.Add(chatHistory);
                await db.SaveChangesAsync(cancellationToken);

                _logger.LogInformation($"[ChatHistoryService] Chat saved - Session: {chatHistory.SessionId}, ResponseTime: {chatHistory.ResponseTimeMs}ms");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ChatHistoryService] Error saving chat history: {ex.Message}");
                // Don't throw - allow application to continue even if history save fails
            }
        }

        /// <summary>
        /// Get chat history for specific session, filtered by userId.
        /// If userId is provided, only returns history where UserId matches —
        /// preventing session hijacking where a different user reuses the same sessionId.
        /// </summary>
        public async Task<List<ChatHistory>> GetSessionHistoryAsync(string sessionId, string? userId, int limit = 50, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    return new List<ChatHistory>();
                }

                await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
                IQueryable<ChatHistory> query = db.ChatHistories
                    .Where(h => h.SessionId == sessionId);

                // IDOR guard: if userId is provided, verify ownership.
                // If no match, return empty — session belongs to a different user.
                if (!string.IsNullOrWhiteSpace(userId))
                {
                    query = query.Where(h => h.UserId == userId);
                }

                var history = await query
                    .OrderByDescending(h => h.CreatedAt)
                    .Take(limit)
                    .ToListAsync(cancellationToken);

                _logger.LogInformation($"[ChatHistoryService] Retrieved {history.Count} records for session {sessionId} (userId={userId ?? "any"})");
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
        public async Task<List<ChatHistory>> GetUserHistoryAsync(string userId, int limit = 100, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(userId))
                {
                    return new List<ChatHistory>();
                }

                await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
                var history = await db.ChatHistories
                    .Where(h => h.UserId == userId)
                    .OrderByDescending(h => h.CreatedAt)
                    .Take(limit)
                    .ToListAsync(cancellationToken);

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
