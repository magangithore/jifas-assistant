using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;
using Jifas.Chatbot.Models;

namespace Jifas.Chatbot.Services
{
    /// <summary>
    /// Main chat service that orchestrates all JIFAS AI Assistant components
    /// Strictly knowledge base only - NO general AI responses
    /// </summary>
    public interface IChatService
    {
        /// <summary>
        /// Process a chat message and return response
        /// </summary>
        Task<ChatResponse> ProcessMessageAsync(ChatRequest request);
    }

    public class ChatService : IChatService
    {
        private readonly IGeminiService _geminiService;
        private readonly IKnowledgeBaseService _knowledgeBaseService;
        private readonly IConversationService _conversationService;
        private readonly IOutOfScopeDetector _outOfScopeDetector;
        private readonly ISuggestionService _suggestionService;
        private readonly ILoggerService _logger;
        private readonly ICacheService _cacheService;
        private readonly IInputValidator _inputValidator;
        private readonly IJifasContextService _jifasContextService;

        // Option 1: Expanded Cache Service
        private readonly ICommonQueryCacheService _commonQueryCacheService;

        // Configuration flags for enabling/disabling options
        private readonly bool _enableOption1ExpandedCache;

        public ChatService()
        {
            _geminiService = new GeminiService();
            _knowledgeBaseService = new KnowledgeBaseService();
            _conversationService = new ConversationService();
            _outOfScopeDetector = new OutOfScopeDetector(_knowledgeBaseService);
            _suggestionService = new SuggestionService(_geminiService, _knowledgeBaseService);
            _jifasContextService = new JifasContextService();
            _logger = LoggerFactory.GetLogger();
            _cacheService = new MemoryCacheService();
            _inputValidator = new InputValidator();

            // Option 1: Initialize Common Query Cache Service
            _commonQueryCacheService = new CommonQueryCacheService();

            // Load configuration flags for options
            _enableOption1ExpandedCache = GetBoolConfig("Optimization:EnableOption1ExpandedCache", true);

            _logger.LogInformation("[ChatService] Initialized with caching optimization enabled: {0}", _enableOption1ExpandedCache);
        }

        public ChatService(
            IGeminiService geminiService,
            IKnowledgeBaseService knowledgeBaseService,
            IConversationService conversationService,
            IOutOfScopeDetector outOfScopeDetector,
            ISuggestionService suggestionService)
        {
            _geminiService = geminiService;
            _knowledgeBaseService = knowledgeBaseService;
            _conversationService = conversationService;
            _outOfScopeDetector = outOfScopeDetector;
            _suggestionService = suggestionService;
            _jifasContextService = new JifasContextService();
            _logger = LoggerFactory.GetLogger();
            _cacheService = new MemoryCacheService();
            _inputValidator = new InputValidator();

            // Option 1: Initialize Common Query Cache Service
            _commonQueryCacheService = new CommonQueryCacheService();

            // Load configuration flags
            _enableOption1ExpandedCache = GetBoolConfig("Optimization:EnableOption1ExpandedCache", true);
        }

        public async Task<ChatResponse> ProcessMessageAsync(ChatRequest request)
        {
            // Validate input first
            var validationResult = _inputValidator.ValidateChatRequest(request);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("[ChatService] Invalid request: {0}", validationResult.ErrorMessage);
                return new ChatResponse
                {
                    Sender = "JIFAS AI Assistant",
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Success = false,
                    SessionId = Guid.NewGuid().ToString(),
                    Message = "Permintaan Anda tidak valid: " + validationResult.ErrorMessage,
                    Source = "Validation Error",
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

            var userMessage = validationResult.Value.Message;

            // Check response cache (optional, configurable)
            var enableResponseCache = bool.TryParse(ConfigurationManager.AppSettings["Caching:EnableResponseCache"], out var responseCache) 
                ? responseCache 
                : false;

            if (enableResponseCache && !string.IsNullOrWhiteSpace(userMessage))
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

            try
            {
                if (string.IsNullOrEmpty(userMessage))
                {
                    response.Message = ConfigurationManager.AppSettings["Chat:EmptyMessageError"] 
                        ?? "Mohon maaf, pesan Anda kosong. Silakan ajukan pertanyaan tentang JIFAS.";
                    response.Source = "Input Validation";
                    response.IsFromKnowledgeBase = false;
                    response.Suggestions = await _suggestionService.GenerateSuggestionsAsync(userMessage ?? "", response.Message);
                    return response;
                }

                // Step 1: Check if query is in scope (JIFAS-related)
                var scopeCheckResult = await _outOfScopeDetector.CheckScopeAsync(userMessage);
                
                if (!scopeCheckResult.IsInScope)
                {
                    response.Message = scopeCheckResult.Message;
                    response.Source = "Out of Scope";
                    response.IsFromKnowledgeBase = false;
                    response.Suggestions = await _suggestionService.GenerateSuggestionsAsync(userMessage, response.Message);
                    
                    await LogConversation(request, response, "out_of_scope", 0);
                    return response;
                }

                // Phase 2 Optimization: Check common queries cache BEFORE KB search
                // This dramatically speeds up responses for frequently asked questions (~7s ? instant)
                var commonQueryResponse = TryGetCommonQueryResponse(request, response, userMessage);
                if (commonQueryResponse != null)
                {
                    // Phase 3B Optimization: PARALLEL SUGGESTION GENERATION
                    // Return response immediately (300ms), generate suggestions in background (non-blocking)
                    // This provides instant response to user while suggestions are generated asynchronously
                    
                    // Start with empty suggestions list (instant, no wait)
                    commonQueryResponse.Suggestions = new List<string>();
                    
                    // Fire-and-forget background task for suggestion generation (non-blocking)
                    #pragma warning disable CS4014 // Because this call is not awaited, execution continues before the call is completed
                    Task.Run(async () => 
                    {
                        try
                        {
                            // Generate suggestions asynchronously in background thread
                            // This doesn't block the main response thread
                            var backgroundSuggestions = await _suggestionService.GenerateSuggestionsAsync(
                                userMessage, 
                                commonQueryResponse.Message
                            );
                            
                            // Log background suggestions for monitoring
                            _logger.LogDebug($"[ChatService] Background suggestions generated for cache query: {string.Join(", ", backgroundSuggestions)}");
                        }
                        catch (Exception ex)
                        {
                            // Graceful error handling - user already has response
                            _logger.LogError($"[ChatService] Background suggestion generation error: {ex.Message}");
                        }
                    });
                    #pragma warning restore CS4014
                    
                    // Phase 4A Optimization: ASYNC LOGGING (Fire-and-Forget)
                    // Log conversation in background (non-blocking)
                    // User gets response immediately, logging happens asynchronously
                    #pragma warning disable CS4014 // Because this call is not awaited, execution continues before the call is completed
                    Task.Run(async () =>
                    {
                        try
                        {
                            // Log conversation asynchronously in background thread
                            // This doesn't block the main response thread
                            await LogConversation(request, commonQueryResponse, "common_query", commonQueryResponse.ConfidenceScore);
                            _logger.LogDebug($"[ChatService] Async logging completed for cache query");
                        }
                        catch (Exception ex)
                        {
                            // Graceful error handling - user already has response
                            _logger.LogError($"[ChatService] Async logging error: {ex.Message}");
                        }
                    });
                    #pragma warning restore CS4014
                    
                    // Cache it (skip re-caching - Phase 4B optimization)
                    // Already cached in CommonQueryResponses dictionary, no need to cache again
                    // if (enableResponseCache)
                    // {
                    //     var cacheKey = $"Chat_Response_{userMessage.GetHashCode()}";
                    //     var cacheDurationHours = int.TryParse(ConfigurationManager.AppSettings["Caching:ResponseCacheDurationHours"], out var duration) 
                    //         ? duration 
                    //         : 24;
                    //     _cacheService.Set(cacheKey, commonQueryResponse, cacheDurationHours * 60);
                    // }
                    
                    // Return IMMEDIATELY! (1.68s ? 1.18s with async logging!)
                    return commonQueryResponse;
                }

                // Step 2: Search Knowledge Base
                var kbResults = await _knowledgeBaseService.SearchAsync(userMessage, topK: 2);

                
                // Step 3: Check if KB has relevant information
                if (kbResults.Count == 0 || kbResults[0].Score < 0.3)
                {
                    var noMatchMessage = ConfigurationManager.AppSettings["Chat:NoKBMatchMessage"] 
                        ?? "Mohon maaf, saya tidak menemukan informasi yang relevan di Knowledge Base JIFAS untuk pertanyaan Anda. Silakan coba dengan kata kunci lain atau hubungi {0} di {1}.";
                    var department = ConfigurationManager.AppSettings["Support:Department"] ?? "IT Help Desk";
                    var email = ConfigurationManager.AppSettings["Support:HelpDeskEmail"] ?? "finance-it@jababeka.com";
                    response.Message = string.Format(noMatchMessage, department, email);
                    response.Source = "No Match in KB";
                    response.IsFromKnowledgeBase = false;
                    response.ConfidenceScore = kbResults.Count > 0 ? kbResults[0].Score : 0;
                    response.KnowledgeBaseResults = kbResults;
                    response.Suggestions = await _suggestionService.GenerateSuggestionsAsync(userMessage, response.Message);
                    
                    await LogConversation(request, response, "kb_no_match", response.ConfidenceScore);
                    return response;
                }

                // Step 4: Generate response using Gemini with KB context
                var aiResponse = await _geminiService.GenerateResponseAsync(userMessage, kbResults);
                response.Message = aiResponse;
                response.Source = "Knowledge Base + Gemini";
                response.IsFromKnowledgeBase = true;
                response.ConfidenceScore = kbResults[0].Score;
                
                // Include KB results in response for client-side analysis
                response.KnowledgeBaseResults = kbResults;

                // Step 5: Generate suggestions
                var suggestions = await _suggestionService.GenerateSuggestionsAsync(userMessage, response.Message);
                response.Suggestions = suggestions;

                // Step 6: Log conversation with document IDs for analytics
                var category = kbResults.Count > 0 && !string.IsNullOrEmpty(kbResults[0].Category) 
                    ? kbResults[0].Category.ToLower() 
                    : "general";
                var documentIds = kbResults.Select(r => r.DocumentId).Distinct().ToList();
                await LogConversation(request, response, category, response.ConfidenceScore, documentIds);

                // Cache successful response before returning
                if (enableResponseCache && response.Success)
                {
                    var cacheKey = $"Chat_Response_{userMessage.GetHashCode()}";
                    var cacheDurationHours = int.TryParse(ConfigurationManager.AppSettings["Caching:ResponseCacheDurationHours"], out var duration) 
                        ? duration 
                        : 24;
                    _cacheService.Set(cacheKey, response, cacheDurationHours * 60); // Convert hours to minutes
                    _logger.LogDebug("[ChatService] Response cached for {0} hours", cacheDurationHours);
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError("[ChatService] Error processing message", ex);
                
                var errorMessage = ConfigurationManager.AppSettings["Chat:DefaultErrorMessage"] 
                    ?? "Mohon maaf, terjadi kesalahan dalam memproses pertanyaan Anda. Silakan coba lagi atau hubungi {0} di {1}.";
                var department = ConfigurationManager.AppSettings["Support:Department"] ?? "IT Help Desk";
                var email = ConfigurationManager.AppSettings["Support:HelpDeskEmail"] ?? "finance-it@jababeka.com";
                response.Message = string.Format(errorMessage, department, email);
                response.Source = "Error";
                response.Success = false;
                
                // Try to generate suggestions, but don't let exception override error message
                try
                {
                    response.Suggestions = await _suggestionService.GenerateSuggestionsAsync(userMessage ?? "", response.Message);
                }
                catch (Exception suggestionEx)
                {
                    _logger.LogWarning("[ChatService] Error generating suggestions in catch block: {0}", suggestionEx.Message);
                    response.Suggestions = new List<string>(); // Empty suggestions instead of crashing
                }

                return response;
            }
        }

        private async Task LogConversation(ChatRequest request, ChatResponse response, string category, double confidence, List<int> documentIds = null)
        {
            try
            {
                await _conversationService.LogConversationAsync(new ConversationLog
                {
                    UserId = request.UserId ?? "anonymous",
                    SessionId = response.SessionId,
                    UserMessage = request.Message,
                    AiResponse = response.Message,
                    Category = category,
                    ConfidenceScore = confidence,
                    IsFromKnowledgeBase = response.IsFromKnowledgeBase,
                    UsedDocumentIds = documentIds ?? new List<int>()
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatService] Log Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Option 1 Optimization: Check common queries cache using CommonQueryCacheService
        /// Loads queries from external JSON file (not hardcoded)
        /// Dramatically speeds up responses for frequently asked questions
        /// </summary>
        private ChatResponse TryGetCommonQueryResponse(ChatRequest request, ChatResponse baseResponse, string userMessage)
        {
            try
            {
                if (!_enableOption1ExpandedCache)
                    return null; // Option 1 disabled, skip cache check

                // Get all cached queries from service
                var cachedQueries = _commonQueryCacheService.GetAllCachedQueries();
                if (cachedQueries == null || cachedQueries.Count == 0)
                {
                    _logger.LogWarning("[ChatService] No cached queries available from CommonQueryCacheService");
                    return null;
                }

                // Normalize message: lowercase, remove punctuation, trim whitespace
                var normalized = System.Text.RegularExpressions.Regex.Replace(
                    userMessage.ToLower(), 
                    @"[^\w\s]", 
                    " "
                ).Trim();
                
                _logger.LogDebug("[ChatService] Common cache lookup for: {0}", normalized);

                // Try exact match first (fastest) - case insensitive
                foreach (var kvp in cachedQueries)
                {
                    if (string.Equals(kvp.Key.ToLower().Trim(), normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("[ChatService] Common query cache HIT (exact match): {0}", userMessage);
                        baseResponse.Message = kvp.Value;
                        baseResponse.Source = "Common Query Cache";
                        baseResponse.IsFromKnowledgeBase = true;
                        baseResponse.ConfidenceScore = 0.99;
                        return baseResponse;
                    }
                }

                // Extract keywords (words > 2 chars) for fuzzy matching
                var keywords = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length > 2)
                    .Distinct()
                    .ToList();

                if (keywords.Count == 0)
                    return null;

                _logger.LogDebug("[ChatService] Cache keywords: {0}", string.Join(",", keywords));

                // Try fuzzy match using keywords
                var bestMatch = "";
                var bestScore = 0.0;

                foreach (var commonQuery in cachedQueries.Keys)
                {
                    var commonNormalized = System.Text.RegularExpressions.Regex.Replace(
                        commonQuery.ToLower(), 
                        @"[^\w\s]", 
                        " "
                    ).Trim();
                    
                    var commonKeywords = commonNormalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(w => w.Length > 2)
                        .Distinct()
                        .ToList();

                    // Better matching: count matching keywords / total unique keywords
                    var matchingKeywords = keywords.Count(k => commonKeywords.Any(ck => k.Equals(ck) || ck.Contains(k) || k.Contains(ck)));
                    var totalUniqueKeywords = keywords.Union(commonKeywords).Count();
                    
                    var score = (double)matchingKeywords / Math.Max(1, totalUniqueKeywords);

                    _logger.LogDebug("[ChatService] Cache candidate '{0}': score={1:F2} (matched {2}/{3} keywords)", 
                        commonQuery, score, matchingKeywords, totalUniqueKeywords);

                    // If 50%+ keyword match, consider it
                    if (score > bestScore && score >= 0.5 && matchingKeywords >= 2)
                    {
                        bestScore = score;
                        bestMatch = commonQuery;
                    }
                }

                // If we found a fuzzy match, return it
                if (!string.IsNullOrWhiteSpace(bestMatch) && bestScore >= 0.5)
                {
                    _logger.LogInformation("[ChatService] Common query cache HIT (fuzzy match ~{0:P0}): {1}", bestScore, userMessage);
                    baseResponse.Message = cachedQueries[bestMatch];
                    baseResponse.Source = $"Common Query Cache (Fuzzy {bestScore:P0})";
                    baseResponse.IsFromKnowledgeBase = true;
                    baseResponse.ConfidenceScore = bestScore;
                    return baseResponse;
                }

                // No common query match found
                _logger.LogDebug("[ChatService] No common query cache match (best score: {0:F2})", bestScore);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[ChatService] Error checking common query cache: {0}", ex.Message);
                return null;
            }
        }


        /// <summary>
        /// Helper method to read boolean configuration values
        /// </summary>
        private bool GetBoolConfig(string key, bool defaultValue)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                if (string.IsNullOrWhiteSpace(value))
                    return defaultValue;

                return bool.TryParse(value, out var result) ? result : defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }
    }
}
