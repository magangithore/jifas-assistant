using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jifas.Assistant.Data;
using Jifas.Assistant.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Chat service interface
    /// </summary>
    public interface IChatService
    {
        Task<ChatResponse> ProcessMessageAsync(ChatRequest request);
    }

    public class ChatService : IChatService
    {
        private readonly IGeminiService _geminiService;
        private readonly IKnowledgeBaseService _knowledgeBaseService;
        private readonly ILoggerService _logger;
        private readonly ICacheService _cacheService;
        private readonly IConfiguration _configuration;
        private readonly JifasAssistantDbContext _db;

        public ChatService(
            IGeminiService geminiService,
            IKnowledgeBaseService knowledgeBaseService,
            ILoggerService logger,
            ICacheService cacheService,
            IConfiguration configuration,
            JifasAssistantDbContext db)
        {
            _geminiService = geminiService;
            _knowledgeBaseService = knowledgeBaseService;
            _logger = logger;
            _cacheService = cacheService;
            _configuration = configuration;
            _db = db;

            _logger.LogInformation("[ChatService] Initialized");
        }

        public async Task<ChatResponse> ProcessMessageAsync(ChatRequest request)
        {
            // Validate input
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
            {
                _logger.LogWarning("[ChatService] Invalid request");
                return new ChatResponse
                {
                    Sender = "JIFAS AI Assistant",
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Success = false,
                    SessionId = Guid.NewGuid().ToString(),
                    Message = "Permintaan tidak valid",
                    IsFromKnowledgeBase = false
                };
            }

            var response = new ChatResponse
            {
                Sender = "JIFAS AI Assistant",
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Success = true,
                SessionId = request.SessionId ?? Guid.NewGuid().ToString()
            };

            var userMessage = request.Message.Trim();

            try
            {
                // Search Knowledge Base
                var kbResults = await _knowledgeBaseService.SearchAsync(userMessage, topK: 2);

                // Check if KB has relevant information
                if (kbResults.Count == 0 || kbResults[0].Score < 0.3)
                {
                    response.Message = "Mohon maaf, informasi yang Anda cari tidak ditemukan di Knowledge Base JIFAS.";
                    response.Source = "No Match in KB";
                    response.IsFromKnowledgeBase = false;
                    response.ConfidenceScore = kbResults.Count > 0 ? kbResults[0].Score : 0;
                    response.KnowledgeBaseResults = kbResults;
                    
                    await LogConversation(request, response, "kb_no_match", response.ConfidenceScore);
                    return response;
                }

                // Generate response using Gemini with KB context
                var aiResponse = await _geminiService.GenerateResponseAsync(userMessage, kbResults);
                response.Message = aiResponse;
                response.Source = "Knowledge Base + Gemini";
                response.IsFromKnowledgeBase = true;
                response.ConfidenceScore = kbResults[0].Score;
                response.KnowledgeBaseResults = kbResults;
                response.Suggestions = new List<string>();

                await LogConversation(request, response, "kb_match", response.ConfidenceScore);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError("[ChatService] Error processing message: {0}", ex, ex.Message);
                response.Message = "Terjadi kesalahan saat memproses pertanyaan Anda.";
                response.Source = "Error";
                response.Success = false;
                response.Suggestions = new List<string>();
                return response;
            }
        }

        private async Task LogConversation(ChatRequest request, ChatResponse response, string category, double confidence)
        {
            try
            {
                if (_db == null)
                {
                    _logger.LogWarning("[ChatService] DbContext is null");
                    return;
                }

                var chat = new Data.Models.Chat
                {
                    UserId = request.UserId ?? "anonymous",
                    SessionId = response.SessionId,
                    UserMessage = request.Message,
                    AssistantResponse = response.Message,
                    Category = category,
                    ConfidenceScore = confidence,
                    IsFromKnowledgeBase = response.IsFromKnowledgeBase,
                    Source = response.Source,
                    CreatedAt = DateTime.Now
                };

                _db.Chats.Add(chat);
                await _db.SaveChangesAsync();

                _logger.LogInformation("[ChatService] Conversation logged");
            }
            catch (Exception ex)
            {
                _logger.LogError("[ChatService] Error logging conversation: {0}", ex, ex.Message);
            }
        }
    }
}
