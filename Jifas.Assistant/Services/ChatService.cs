using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Jifas.Assistant.Models;
using jifas_assistant.DAL.Models;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Main chat service that orchestrates all JIFAS AI Assistant components
    /// Strictly knowledge base only - NO general AI responses
    /// </summary>
    public interface IChatService
    {
        Task<ChatResponse> ProcessMessageAsync(ChatRequest request);
    }

    public class ChatService : IChatService
    {
        private readonly IGeminiService _geminiService;
        private readonly IKnowledgeBaseService _knowledgeBaseService;
        private readonly IOutOfScopeDetector _outOfScopeDetector;
        private readonly ISuggestionService _suggestionService;
        private readonly ILoggerService _logger;
        private readonly ICacheService _cacheService;
        private readonly IJifasContextService _jifasContextService;
        private readonly IConfiguration _configuration;

        public ChatService(
            IGeminiService geminiService,
            IKnowledgeBaseService knowledgeBaseService,
            IOutOfScopeDetector outOfScopeDetector,
            ISuggestionService suggestionService,
            ILoggerService logger,
            ICacheService cacheService,
            IJifasContextService jifasContextService,
            IConfiguration configuration)
        {
            _geminiService = geminiService;
            _knowledgeBaseService = knowledgeBaseService;
            _outOfScopeDetector = outOfScopeDetector;
            _suggestionService = suggestionService;
            _logger = logger;
            _cacheService = cacheService;
            _jifasContextService = jifasContextService;
            _configuration = configuration;
        }

        public async Task<ChatResponse> ProcessMessageAsync(ChatRequest request)
        {
            var response = new ChatResponse
            {
                Sender = "JIFAS AI Assistant",
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Success = true,
                SessionId = request.SessionId ?? Guid.NewGuid().ToString()
            };

            if (request == null || string.IsNullOrWhiteSpace(request.Message))
            {
                response.Message = "Mohon maaf, pesan Anda kosong. Silakan ajukan pertanyaan tentang JIFAS.";
                response.Source = "Input Validation";
                response.IsFromKnowledgeBase = false;
                response.Success = false;
                return response;
            }

            var userMessage = request.Message.Trim();

            try
            {
                // Check response cache
                var enableCache = _configuration.GetValue<bool>("Caching:EnableResponseCache");
                if (enableCache && !string.IsNullOrWhiteSpace(userMessage))
                {
                    var cacheKey = $"Chat_Response_{userMessage.GetHashCode()}";
                    var cachedResponse = _cacheService.Get<ChatResponse>(cacheKey);
                    
                    if (cachedResponse != null)
                    {
                        cachedResponse.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        cachedResponse.SessionId = response.SessionId;
                        _logger.LogInformation("[ChatService] Response cache HIT for user message");
                        return cachedResponse;
                    }
                }

                // Step 1: Check if query is in scope
                var scopeCheckResult = await _outOfScopeDetector.CheckScopeAsync(userMessage);
                
                if (!scopeCheckResult.IsInScope)
                {
                    response.Message = scopeCheckResult.Message;
                    response.Source = "Out of Scope";
                    response.IsFromKnowledgeBase = false;
                    response.Suggestions = await _suggestionService.GenerateSuggestionsAsync(userMessage, response.Message);
                    return response;
                }

                // Step 2: Search Knowledge Base
                var kbResults = await _knowledgeBaseService.SearchAsync(userMessage, topK: 2);

                // Step 3: Check if KB has relevant information
                if (kbResults == null || kbResults.Count == 0 || kbResults[0].Score < 0.3)
                {
                    var noMatchMessage = _configuration["Chat:NoKBMatchMessage"] 
                        ?? "Mohon maaf, saya tidak menemukan informasi yang relevan di Knowledge Base JIFAS untuk pertanyaan Anda.";
                    response.Message = noMatchMessage;
                    response.Source = "No Match in KB";
                    response.IsFromKnowledgeBase = false;
                    response.ConfidenceScore = kbResults?.Count > 0 ? kbResults[0].Score : 0;
                    response.KnowledgeBaseResults = kbResults;
                    response.Suggestions = await _suggestionService.GenerateSuggestionsAsync(userMessage, response.Message);
                    return response;
                }

                // Step 4: Generate response using Gemini with KB context
                var aiResponse = await _geminiService.GenerateResponseAsync(userMessage, kbResults);
                response.Message = aiResponse;
                response.Source = "Knowledge Base + Gemini";
                response.IsFromKnowledgeBase = true;
                response.ConfidenceScore = kbResults[0].Score;
                response.KnowledgeBaseResults = kbResults;

                // Step 5: Generate suggestions
                var suggestions = await _suggestionService.GenerateSuggestionsAsync(userMessage, response.Message);
                response.Suggestions = suggestions;

                // Cache successful response
                if (enableCache && response.Success)
                {
                    var cacheKey = $"Chat_Response_{userMessage.GetHashCode()}";
                    var cacheDuration = _configuration.GetValue<int>("Caching:ResponseCacheDurationHours", 24);
                    _cacheService.Set(cacheKey, response, cacheDuration * 60);
                    _logger.LogDebug("[ChatService] Response cached for {0} hours", cacheDuration);
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ChatService] Error processing message: {ex.Message}");
                
                response.Message = "Mohon maaf, terjadi kesalahan dalam memproses pertanyaan Anda. Silakan coba lagi.";
                response.Source = "Error";
                response.Success = false;
                
                try
                {
                    response.Suggestions = await _suggestionService.GenerateSuggestionsAsync(userMessage ?? "", response.Message);
                }
                catch
                {
                    response.Suggestions = new List<string>();
                }

                return response;
            }
        }
    }
}
