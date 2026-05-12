using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Jifas.Assistant.Models;
using Jifas.Assistant.Utilities;
using jifas_assistant.DAL.Models;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Main chat service that orchestrates all JIFAS AI Assistant components
    /// Enhanced with intelligent query understanding, adaptive confidence, and quality validation
    /// Strictly knowledge base only - NO general AI responses
    /// </summary>
    public interface IChatService
    {
        Task<ChatResponse> ProcessMessageAsync(ChatRequest request);
    }

    public class ChatService : IChatService
    {
        private readonly IOllamaService _ollamaService;
        private readonly IKnowledgeBaseService _knowledgeBaseService;
        private readonly IOutOfScopeDetector _outOfScopeDetector;
        private readonly ISuggestionService _suggestionService;
        private readonly ILoggerService _logger;
        private readonly ICacheService _cacheService;
        private readonly IJifasContextService _jifasContextService;
        private readonly IKnowledgeBaseContextService _kbContextService;
        private readonly ILocalizationService _localizationService;
        private readonly IConfiguration _configuration;
        private readonly IInputValidator _inputValidator;
        private readonly IChatHistoryService _chatHistoryService;
        
        // NEW: Consolidated enhanced services for better AI quality
        private readonly IQueryUnderstandingService _queryUnderstanding;
        private readonly IResponseQualityService _responseQuality;
        private readonly IConversationIntelligenceService _conversationIntelligence;
        private readonly IUserMemoryService _userMemory;
        private readonly IMonitoringService _monitoring;

        private const int MIN_KB_RESULTS_REQUIRED = 1;
        private const int MAX_REGENERATION_ATTEMPTS = 2;

        public ChatService(
            IOllamaService ollamaService,
            IKnowledgeBaseService knowledgeBaseService,
            IOutOfScopeDetector outOfScopeDetector,
            ISuggestionService suggestionService,
            ILoggerService logger,
            ICacheService cacheService,
            IJifasContextService jifasContextService,
            IKnowledgeBaseContextService kbContextService,
            ILocalizationService localizationService,
            IConfiguration configuration,
            IInputValidator inputValidator,
            IChatHistoryService chatHistoryService,
            // NEW: Consolidated enhanced services
            IQueryUnderstandingService queryUnderstanding,
            IResponseQualityService responseQuality,
            IConversationIntelligenceService conversationIntelligence,
            IUserMemoryService userMemory,
            IMonitoringService monitoring)
        {
            _ollamaService = ollamaService;
            _knowledgeBaseService = knowledgeBaseService;
            _outOfScopeDetector = outOfScopeDetector;
            _suggestionService = suggestionService;
            _logger = logger;
            _cacheService = cacheService;
            _jifasContextService = jifasContextService;
            _kbContextService = kbContextService ?? throw new ArgumentNullException(nameof(kbContextService));
            _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
            _configuration = configuration;
            _inputValidator = inputValidator ?? throw new ArgumentNullException(nameof(inputValidator));
            _chatHistoryService = chatHistoryService ?? throw new ArgumentNullException(nameof(chatHistoryService));
            
            // NEW: Consolidated enhanced services
            _queryUnderstanding = queryUnderstanding ?? throw new ArgumentNullException(nameof(queryUnderstanding));
            _responseQuality = responseQuality ?? throw new ArgumentNullException(nameof(responseQuality));
            _conversationIntelligence = conversationIntelligence ?? throw new ArgumentNullException(nameof(conversationIntelligence));
            _userMemory = userMemory ?? throw new ArgumentNullException(nameof(userMemory));
            _monitoring = monitoring ?? throw new ArgumentNullException(nameof(monitoring));
        }

        public async Task<ChatResponse> ProcessMessageAsync(ChatRequest request)
        {
            // ?? START TOTAL TIMER
            var totalStopwatch = Stopwatch.StartNew();

            // ?? GET OR CREATE CORRELATION ID
            var correlationId = request?.CorrelationId ?? Guid.NewGuid().ToString();

            var response = new ChatResponse
            {
                Sender = "JIFAS AI Assistant",
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Success = true,
                SessionId = request?.SessionId ?? Guid.NewGuid().ToString(),
                CorrelationId = correlationId
            };

            // ?? INITIALIZE PERFORMANCE METRICS
            var metrics = new PerformanceMetrics();

            // ?? SET MONITORING CONTEXT EARLY (before any processing)
            // This ensures all AI calls are tracked with user identity
            _ollamaService.SetCallContext(
                userId:       request?.UserId,
                sessionId:    request?.SessionId,
                activeModule: request?.Context?.ActiveModule,
                callType:     "chat");

            var isFirstMessage = string.IsNullOrWhiteSpace(request?.SessionId);

            // ?? BACKEND DEBUG: Log context from frontend
            _logger.LogDebug(
                $"[ChatService] Processing message — UserId: {request?.UserId}, " +
                $"IsFirstMessage: {isFirstMessage} (or request field: {request?.IsFirstMessage}), " +
                $"UserRole: {request?.UserRole}, " +
                $"UserCompCode: {request?.UserCompCode}, " +
                $"ActiveModule: {request?.Context?.ActiveModule}");

            var jifasIntroductionKey = $"JIFAS_Intro_{response.SessionId}";
            var hasSeenIntroduction = _cacheService.Get<bool>(jifasIntroductionKey);

            // ?? STEP 1: VALIDATE INPUT (CRITICAL!)
            var validationStopwatch = Stopwatch.StartNew();
            var validationResult = _inputValidator.ValidateChatRequest(request);
            validationStopwatch.Stop();
            metrics.InputValidationMs = validationStopwatch.ElapsedMilliseconds;

            if (!validationResult.IsValid)
            {
                response.Message = validationResult.ErrorMessage ?? "Invalid request. Please check your input.";
                response.Source = "Input Validation";
                response.IsFromKnowledgeBase = false;
                response.Success = false;
                response.CorrelationId = correlationId;
                
                _logger.LogWarningWithCorrelation(correlationId, $"[ChatService] Input validation failed: {validationResult.ErrorMessage}");
                _logger.LogPerformance("InputValidation", metrics.InputValidationMs, correlationId);
                
                totalStopwatch.Stop();
                metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                response.PerformanceMetrics = metrics;
                
                // Save chat history asynchronously
                _ = SaveChatHistoryAsync(response, validationResult.ErrorMessage ?? "", request);
                
                return response;
            }

            var userMessage = validationResult.Value.Message;

            try
            {
                // ?? CHECK CACHE
                var cacheStopwatch = Stopwatch.StartNew();
                var enableCache = _configuration.GetValue<bool>("Caching:EnableResponseCache");
                if (enableCache && !string.IsNullOrWhiteSpace(userMessage))
                {
                    var cacheKey = $"Chat_Response_{HashHelper.ToShortStableHash(userMessage)}";
                    var cachedResponse = _cacheService.Get<ChatResponse>(cacheKey);
                    cacheStopwatch.Stop();
                    metrics.CacheLookupMs = cacheStopwatch.ElapsedMilliseconds;
                    
                    if (cachedResponse != null)
                    {
                        cachedResponse.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        cachedResponse.SessionId = response.SessionId;
                        cachedResponse.PerformanceMetrics.WasCacheLit = true;
                        cachedResponse.PerformanceMetrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                        _logger.LogInformation($"[ChatService] ?? Response cache HIT - Total: {cachedResponse.PerformanceMetrics.TotalMs}ms | {cachedResponse.PerformanceMetrics.GetSummary()}");
                        return cachedResponse;
                    }
                }

                // Show JIFAS introduction on first message if not yet shown
                if (isFirstMessage && !hasSeenIntroduction)
                {
                    _logger.LogInformation($"[ChatService] New session detected - showing JIFAS introduction");
                    await ShowJIFASIntroductionAsync(response);
                    _cacheService.Set(jifasIntroductionKey, true, 24 * 60);
                    
                    totalStopwatch.Stop();
                    metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                    response.PerformanceMetrics = metrics;
                    _logger.LogInformation($"[INTRO] {metrics.GetSummary()}");
                    
                    // Save chat history asynchronously
                    _ = SaveChatHistoryAsync(response, "[JIFAS Introduction]", request);
                    
                    return response;
                }

                // ?? STEP 2: INTENT CLASSIFICATION (Enhanced)
                var intentStopwatch = Stopwatch.StartNew();
                var intentResult = await _queryUnderstanding.ClassifyIntentAsync(userMessage);
                intentStopwatch.Stop();
                _logger.LogInformation($"[ChatService] Intent: {intentResult.Intent}, Confidence: {intentResult.Confidence:P0}");

                // Handle special intents (greeting, gratitude, out of scope)
                if (intentResult.Intent == IntentType.Greeting)
                {
                    response.Message = await GenerateNaturalGreetingAsync();
                    response.Source = "Greeting";
                    response.IsFromKnowledgeBase = false;
                    response.Success = true;
                    response.Suggestions = new List<string>
                    {
                        "Bagaimana cara membuat Invoice?",
                        "Apa itu PUM?",
                        "Siapa yang bisa approve payment?"
                    };
                    
                    totalStopwatch.Stop();
                    metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                    response.PerformanceMetrics = metrics;
                    _ = SaveChatHistoryAsync(response, userMessage, request);
                    return response;
                }

                if (intentResult.Intent == IntentType.Gratitude)
                {
                    response.Message = await GenerateNaturalGratitudeResponseAsync();
                    response.Source = "Gratitude";
                    response.IsFromKnowledgeBase = false;
                    response.Success = true;
                    response.Suggestions = new List<string>();
                    
                    totalStopwatch.Stop();
                    metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                    response.PerformanceMetrics = metrics;
                    _ = SaveChatHistoryAsync(response, userMessage, request);
                    return response;
                }

                // ?? STEP 2B: SCOPE DETECTION (Enhanced with Intent)
                var scopeStopwatch = Stopwatch.StartNew();
                var scopeCheckResult = await _outOfScopeDetector.CheckScopeAsync(userMessage);
                scopeStopwatch.Stop();
                metrics.ScopeDetectionMs = scopeStopwatch.ElapsedMilliseconds;
                
                // Out of scope - generate natural rejection
                if (!scopeCheckResult.IsInScope || intentResult.Intent == IntentType.OutOfScope)
                {
                    response.Message = await GenerateNaturalOutOfScopeResponseAsync(userMessage);
                    response.Source = "Out of Scope";
                    response.IsFromKnowledgeBase = false;
                    response.Suggestions = await _suggestionService.GenerateSuggestionsAsync(userMessage, response.Message);
                    
                    totalStopwatch.Stop();
                    metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                    response.PerformanceMetrics = metrics;
                    _logger.LogInformation($"[OUT_OF_SCOPE] {metrics.GetSummary()}");
                    
                    _ = SaveChatHistoryAsync(response, userMessage, request);
                    return response;
                }

                // ?? STEP 2C: QUERY EXPANSION (Enhanced)
                var expandedQuery = await _queryUnderstanding.ExpandQueryAsync(userMessage);
                _logger.LogDebug($"[ChatService] Query expanded: Keywords={expandedQuery.Keywords.Count}, Synonyms={expandedQuery.Synonyms.Count}");

                // ?? STEP 2D: BUILD CONVERSATION CONTEXT
                var conversationContext = await _conversationIntelligence.GetFormattedContextAsync(response.SessionId);
                var isFollowUp = await _conversationIntelligence.IsFollowUpQueryAsync(response.SessionId, userMessage);
                if (isFollowUp)
                {
                    _logger.LogInformation("[ChatService] Detected follow-up question - using conversation context");
                }

                // ?? STEP 3: KNOWLEDGE BASE SEARCH (Enhanced with expanded query)
                var kbSearchStopwatch = Stopwatch.StartNew();
                
                // Search with original query
                var kbResults = await _knowledgeBaseService.SearchAsync(userMessage, topK: 5);
                
                // If results are weak, also search with expanded terms
                if (kbResults == null || kbResults.Count < 2 || kbResults.Max(r => r.Score) < 0.6)
                {
                    var expandedResults = await _knowledgeBaseService.SearchAsync(expandedQuery.EnhancedSearchQuery, topK: 3);
                    if (expandedResults != null && expandedResults.Count > 0)
                    {
                        // Merge results
                        kbResults = MergeKBResults(kbResults ?? new List<KnowledgeBaseResult>(), expandedResults);
                        _logger.LogInformation($"[ChatService] Enhanced search with query expansion - merged {expandedResults.Count} additional results");
                    }
                }
                
                kbSearchStopwatch.Stop();
                metrics.KbSearchMs = kbSearchStopwatch.ElapsedMilliseconds;
                metrics.KbResultsBeforeValidation = kbResults?.Count ?? 0;

                // ?? STEP 4: VALIDATE KB RESULTS
                var validationStopwatch2 = Stopwatch.StartNew();
                var validatedResults = ValidateKBResults(kbResults);
                validationStopwatch2.Stop();
                metrics.ResultValidationMs = validationStopwatch2.ElapsedMilliseconds;
                metrics.KbResultsAfterValidation = validatedResults.Count;

                if (validatedResults.Count == 0 && kbResults?.Count > 0)
                {
                    _logger.LogWarning($"[ChatService] KB results validation failed - all results filtered out");
                }

                // ?? STEP 5: ADAPTIVE CONFIDENCE CALCULATION (Enhanced)
                var confidenceStopwatch = Stopwatch.StartNew();
                
                // Calculate adaptive threshold based on intent
                var adaptiveThreshold = await _responseQuality.CalculateThresholdAsync(userMessage, intentResult.Intent, response.SessionId);
                
                // Calculate confidence with multiple factors
                var confidenceResult = _responseQuality.CalculateConfidence(
                    validatedResults.Count > 0 ? validatedResults : kbResults ?? new List<KnowledgeBaseResult>(),
                    userMessage,
                    intentResult.Intent,
                    adaptiveThreshold);
                
                var confidenceScore = confidenceResult.CalculatedConfidence;
                confidenceStopwatch.Stop();
                metrics.ConfidenceCalculationMs = confidenceStopwatch.ElapsedMilliseconds;
                metrics.AverageKbScore = validatedResults.Count > 0 ? validatedResults.Average(r => r.Score) : 0;
                
                _logger.LogInformation($"[ChatService] KB Search ({metrics.KbSearchMs}ms): {validatedResults.Count} valid results, " +
                    $"Confidence: {confidenceScore:F2}, Threshold: {adaptiveThreshold:F2}, Meets: {confidenceResult.MeetsThreshold}");

                // ?? STEP 5B: HANDLE LOW CONFIDENCE (Enhanced)
                if (!_responseQuality.ShouldGenerateResponse(confidenceResult))
                {
                    var llmStopwatch = Stopwatch.StartNew();
                    
                    // Generate intelligent response based on what we have
                    string fallbackMessage;
                    if (validatedResults != null && validatedResults.Count > 0)
                    {
                        // We have some results - try to help with partial info
                        fallbackMessage = await GenerateIntelligentPartialResponseAsync(userMessage, validatedResults, intentResult.Intent);
                    }
                    else
                    {
                        // No results - generate helpful "I don't know" response
                        fallbackMessage = await GenerateHelpfulNoMatchResponseAsync(userMessage, intentResult.Intent);
                    }
                    
                    llmStopwatch.Stop();
                    metrics.LlmResponseMs = (long)llmStopwatch.Elapsed.TotalMilliseconds;
                    
                    response.Message = fallbackMessage;
                    response.Source = validatedResults?.Count > 0 ? "Partial KB Match" : "No KB Match";
                    response.IsFromKnowledgeBase = validatedResults?.Count > 0;
                    response.ConfidenceScore = confidenceScore;
                    response.KnowledgeBaseResults = validatedResults ?? kbResults ?? new List<KnowledgeBaseResult>();
                    
                    // Generate relevant suggestions
                    var suggestionsStopwatch = Stopwatch.StartNew();
                    response.Suggestions = await GenerateContextualSuggestionsAsync(userMessage, intentResult.Intent, validatedResults);
                    suggestionsStopwatch.Stop();
                    metrics.SuggestionsMs = (long)suggestionsStopwatch.Elapsed.TotalMilliseconds;
                    
                    totalStopwatch.Stop();
                    metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                    response.PerformanceMetrics = metrics;
                    
                    _logger.LogInformation($"[PARTIAL_RESPONSE] Confidence({confidenceScore:F2}) | {metrics.GetSummary()}");
                    
                    _ = SaveChatHistoryAsync(response, userMessage, request);
                    return response;
                }

                // ?? STEP 6: GENERATE KB-BASED RESPONSE (Enhanced with context)
                var responseStopwatch = Stopwatch.StartNew();

                // Build active page context dari request.Context
                var activePageContext = BuildActivePageContextFromRequest(request);

                var aiResponse = await GenerateEnhancedResponseAsync(
                    userMessage, 
                    validatedResults, 
                    intentResult.Intent,
                    conversationContext,
                    isFollowUp,
                    activePageContext,
                    userId: request?.UserId);
                responseStopwatch.Stop();
                metrics.LlmResponseMs = (long)responseStopwatch.Elapsed.TotalMilliseconds;

                // ?? STEP 6B: VALIDATE RESPONSE QUALITY
                var qualityResult = await _responseQuality.ValidateResponseAsync(userMessage, aiResponse, validatedResults);
                
                // If quality is poor, try to regenerate once
                if (qualityResult.ShouldRegenerate && qualityResult.OverallScore < 0.4)
                {
                    _logger.LogWarning($"[ChatService] Low quality response detected ({qualityResult.OverallScore:P0}), regenerating...");
                    
                    aiResponse = await RegenerateImprovedResponseAsync(userMessage, validatedResults, intentResult.Intent, qualityResult);
                    
                    // Re-validate
                    qualityResult = await _responseQuality.ValidateResponseAsync(userMessage, aiResponse, validatedResults);
                }
                
                _logger.LogInformation($"[ChatService] Response quality: {qualityResult.OverallScore:P0}, Grounding: {qualityResult.GroundingScore:P0}");

                 response.Message = aiResponse;
                 response.Source = "Knowledge Base (100% dari KB)";
                 response.IsFromKnowledgeBase = true;
                 response.ConfidenceScore = confidenceScore;
                 response.KnowledgeBaseResults = validatedResults;
                 response.Success = true;

                 // ?? STEP 7: GENERATE SUGGESTIONS (with caching)
                 var suggestionsStopwatch2 = Stopwatch.StartNew();

                 // Update callType for suggestions
                 _ollamaService.SetCallContext(
                     userId:       request?.UserId,
                     sessionId:    request?.SessionId,
                     activeModule: request?.Context?.ActiveModule,
                     callType:     "suggestions");

                 if (enableCache)
                 {
                     var suggestionCacheKey = $"Suggestions_{HashHelper.ToShortStableHash(aiResponse)}";
                     var cachedResult = _cacheService.Get<List<string>>(suggestionCacheKey);

                     if (cachedResult != null)
                     {
                         _logger.LogInformation("[ChatService] ?? Suggestions cache HIT");
                         response.Suggestions = cachedResult;
                         metrics.SuggestionsCached = true;

                         // Still record a monitoring entry so the dashboard shows this call
                         _ = _monitoring.RecordAsync(new AiCallMetrics
                         {
                             UserId              = request?.UserId,
                             SessionId           = request?.SessionId,
                             ActiveModule        = request?.Context?.ActiveModule,
                             CallType            = "suggestions",
                             Model               = "cache",
                             PromptTokens        = 0,
                             CompletionTokens    = 0,
                             TotalDurationMs     = 0,
                             ResponseLengthChars = cachedResult.Sum(s => s.Length),
                             IsError             = false,
                             CreatedAt           = DateTime.UtcNow
                         });
                     }
                     else
                     {
                         var suggestions = await _suggestionService.GenerateSuggestionsAsync(userMessage, aiResponse);
                         var cacheDuration = _configuration.GetValue<int>("Caching:ResponseCacheDurationHours", 24);
                         _cacheService.Set(suggestionCacheKey, suggestions, cacheDuration * 60);
                         response.Suggestions = suggestions;
                         metrics.SuggestionsCached = false;
                     }
                 }
                 else
                 {
                     response.Suggestions = await _suggestionService.GenerateSuggestionsAsync(userMessage, aiResponse);
                     metrics.SuggestionsCached = false;
                 }
                 suggestionsStopwatch2.Stop();
                 metrics.SuggestionsMs = (long)suggestionsStopwatch2.Elapsed.TotalMilliseconds;

                // ?? STEP 8: CACHE RESPONSE
                var cacheStopwatch2 = Stopwatch.StartNew();
                if (enableCache && response.Success)
                {
                    var cacheKey = $"Chat_Response_{HashHelper.ToShortStableHash(userMessage)}";
                    var cacheDuration = _configuration.GetValue<int>("Caching:ResponseCacheDurationHours", 24);
                    _cacheService.Set(cacheKey, response, cacheDuration * 60);
                }
                cacheStopwatch2.Stop();
                metrics.CachingMs = cacheStopwatch2.ElapsedMilliseconds;

                totalStopwatch.Stop();
                metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                response.PerformanceMetrics = metrics;
                response.CorrelationId = correlationId;
                
                // ?? LOG PERFORMANCE METRICS
                _logger.LogPerformance("ChatProcessing", metrics.TotalMs, correlationId);
                _logger.LogInformationWithCorrelation(correlationId, $"[KB_RESPONSE] {metrics.GetSummary()}");
                
                // ?? LOG AUDIT TRAIL
                _logger.LogAudit(request?.UserId ?? "Unknown", "ProcessMessage", 
                    $"Source: {response.Source}, Confidence: {response.ConfidenceScore:F2}", correlationId);

                // Save chat history asynchronously
                _ = SaveChatHistoryAsync(response, userMessage, request);

                // Update long-term user memory (fire-and-forget)
                // Pass new fields: isFirstMessage, userCompCode, userEmpCode, userRole, currentModule
                _ = _userMemory.UpdateMemoryAsync(
                    request?.UserId ?? "anonymous",
                    userMessage,
                    aiResponse,
                    intentResult.Intent,
                    confidenceScore,
                    currentModule: request?.Context?.ActiveModule,
                    userRole: request?.UserRole);

                _logger.LogDebug(
                    $"[ChatService] Memory update queued for user: {request?.UserId}, " +
                    $"Message count incremented, Expertise calc pending");

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogErrorWithCorrelation(correlationId, $"[ChatService] Error processing message", ex);
                
                response.Message = "Mohon maaf, terjadi kesalahan dalam memproses pertanyaan Anda. Silakan coba lagi.";
                response.Source = "Error";
                response.Success = false;
                response.CorrelationId = correlationId;
                response.Errors.Add($"Exception: {ex.GetType().Name}: {ex.Message}");
                
                try
                {
                    response.Suggestions = await _suggestionService.GenerateSuggestionsAsync(userMessage ?? "", response.Message);
                }
                catch
                {
                    response.Suggestions = new List<string>();
                }

                totalStopwatch.Stop();
                metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                response.PerformanceMetrics = metrics;
                response.CorrelationId = correlationId;
                
                // ?? LOG AUDIT TRAIL FOR ERROR
                _logger.LogAudit(request?.UserId ?? "Unknown", "ProcessMessage_ERROR", 
                    $"{ex.GetType().Name}: {ex.Message}", correlationId);
                _logger.LogPerformance("ChatProcessing_Error", metrics.TotalMs, correlationId);
                
                // Save chat history asynchronously
                _ = SaveChatHistoryAsync(response, userMessage ?? "", request);
                
                return response;
            }
        }

        /// <summary>
        /// Validate KB results to ensure quality before using them
        /// Filters out empty, suspicious, or low-quality results
        /// </summary>
        private List<KnowledgeBaseResult> ValidateKBResults(List<KnowledgeBaseResult> results)
        {
            if (results == null || results.Count == 0)
                return new List<KnowledgeBaseResult>();

            var validated = new List<KnowledgeBaseResult>();

            foreach (var result in results)
            {
                // Quality checks:
                // 1. Content must not be empty or whitespace only
                if (string.IsNullOrWhiteSpace(result.Content))
                {
                    _logger.LogWarning($"[ChatService] Filtered out KB result: empty content");
                    continue;
                }

                // 2. Content should be meaningful (at least 20 chars, not just garbage)
                if (result.Content.Length < 20)
                {
                    _logger.LogWarning($"[ChatService] Filtered out KB result: content too short ({result.Content.Length} chars)");
                    continue;
                }

                // 3. Content should have reasonable word count
                var wordCount = result.Content.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                if (wordCount < 5)
                {
                    _logger.LogWarning($"[ChatService] Filtered out KB result: too few words ({wordCount})");
                    continue;
                }

                // 4. Score should be reasonable (not zero)
                if (result.Score <= 0)
                {
                    _logger.LogWarning($"[ChatService] Filtered out KB result: invalid score ({result.Score})");
                    continue;
                }

                // 5. Document ID should be valid
                if (result.DocumentId <= 0)
                {
                    _logger.LogWarning($"[ChatService] Filtered out KB result: invalid document ID");
                    continue;
                }

                validated.Add(result);
            }

            if (validated.Count < results.Count)
            {
                _logger.LogInformation($"[ChatService] KB validation: {results.Count} ? {validated.Count} results " +
                                      $"({results.Count - validated.Count} filtered out)");
            }

            return validated;
        }

        /// <summary>
        /// Calculate confidence score based on KB results
        /// FIX #3: More sophisticated calculation taking multiple factors into account
        /// </summary>
        private double CalculateKBConfidence(List<KnowledgeBaseResult> results)
        {
            if (results == null || results.Count == 0)
            {
                return 0.0;
            }

            // FIX #3: Enhanced multi-factor confidence calculation
            var topResults = results.Take(3).ToList();
            
            // Factor 1: Average relevance score (40% weight)
            var avgScore = topResults.Average(r => r.Score);
            
            // Factor 2: Best match score (30% weight)
            var maxScore = topResults.Max(r => r.Score);
            var scoreCeiling = maxScore >= 0.8 ? maxScore : maxScore * 0.9;
            
            // Factor 3: Result count diversity (20% weight)
            var documentDiversity = topResults.Select(r => r.DocumentId).Distinct().Count();
            var diversityScore = Math.Min(documentDiversity / 3.0, 1.0);
            
            // Factor 4: Minimum quality check
            var hasHighRelevance = topResults.Any(r => r.Score >= 0.75);
            var qualityBonus = hasHighRelevance ? 0.1 : 0.0;
            
            // Factor 5: Result count (10% weight)
            var resultCountScore = Math.Min(results.Count / 5.0, 1.0);
            
            // Composite confidence score (weighted)
            var confidence = (avgScore * 0.4) +
                            (scoreCeiling * 0.3) +
                            (diversityScore * 0.2) +
                            (resultCountScore * 0.1);
            
            // Penalize if no high-relevance match found
            if (!hasHighRelevance && confidence > 0.65)
            {
                confidence *= 0.85; // Reduce confidence by 15% if no strong match
                _logger.LogWarning($"[ChatService] Confidence reduced due to lack of high-relevance match");
            }

            // Final confidence capped at 1.0
            confidence = Math.Min(confidence + qualityBonus, 1.0);
            
            _logger.LogDebug($"[ChatService] Confidence calculated - AvgScore: {avgScore:F2}, MaxScore: {maxScore:F2}, " +
                           $"Diversity: {documentDiversity}, ResultCount: {results.Count}, Final: {confidence:F2}");

            return confidence;
        }

        /// <summary>
        /// Generate partial match response using Ollama
        /// When KB has some results but confidence is borderline
        /// </summary>
        private async Task<string> GeneratePartialMatchResponseAsync(string userQuery, List<KnowledgeBaseResult> partialResults)
        {
            try
            {
                var resultsSummary = string.Join("\n", partialResults.Take(3).Select((r, i) => 
                    $"{i + 1}. {r.Title} (Relevance: {r.Score:P0})"));

                var prompt = $@"User mengajukan pertanyaan kepada JIFAS AI Assistant dengan hasil pencarian partial match di Knowledge Base.

Pertanyaan user: ""{userQuery}""

Hasil yang ditemukan:
{resultsSummary}

Buatlah respons yang:
1. Sopan dan profesional
2. Tunjukkan bahwa ada beberapa informasi terkait yang mungkin membantu
3. Sampaikan hasil yang ditemukan secara natural
4. Jika hasil tidak 100% sesuai, jelaskan perbedaannya
5. Sarankan user untuk ulangi pertanyaan atau hubungi IT Help Desk jika masih belum jelas
6. Gunakan bahasa Indonesia natural dan friendly
7. Singkat dan to the point

Buatlah respons langsung tanpa penjelasan tambahan.";

                var response = await _ollamaService.CallOllamaApiAsync(prompt);
                _logger.LogInformation("[ChatService] Generated partial match response");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ChatService] Error generating partial match response: {ex.Message}");
                return "Saya menemukan beberapa informasi yang mungkin relevan, namun tidak 100% sesuai dengan pertanyaan Anda. Silakan coba reformulasi pertanyaan atau hubungi IT Help Desk untuk bantuan lebih lanjut.";
            }
        }

        /// <summary>
        /// Generate natural no-match response using Ollama
        /// When KB confidence is too low or no relevant results found
        /// </summary>
        private async Task<string> GenerateNoMatchResponseAsync(string userQuery)
        {
            try
            {
                var prompt = $@"Pertanyaan berikut tidak memiliki kecocokan yang cukup dalam Knowledge Base JIFAS.

Pertanyaan user: ""{userQuery}""

Buatlah respons yang:
1. Sopan dan profesional
2. Jelaskan bahwa informasi tidak tersedia di KB
3. Sarankan untuk coba pertanyaan lain atau hubungi IT Help Desk
4. Gunakan bahasa Indonesia natural dan friendly
5. Singkat (1-2 kalimat saja)
6. Jangan hardcoded atau terasa robot

Buatlah respons langsung tanpa penjelasan tambahan.";

                var response = await _ollamaService.CallOllamaApiAsync(prompt);
                _logger.LogInformation("[ChatService] Generated no-match response");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ChatService] Error generating no-match response: {ex.Message}");
                return "Maaf, saya tidak menemukan informasi yang relevan untuk pertanyaan Anda. Silakan coba pertanyaan lain atau hubungi IT Help Desk.";
            }
        }

        /// <summary>
        /// Display AI introduction to JIFAS system on first user message
        /// Generates natural welcome message from Ollama with DYNAMIC system context
        /// </summary>
        private async Task ShowJIFASIntroductionAsync(ChatResponse response)
        {
            try
            {
                // Get dynamic context dari Knowledge Base files
                var systemContext = await _kbContextService.GetSystemContextAsync();

                var prompt = $@"Buatlah salam pembuka yang natural dan profesional untuk AI Assistant JIFAS (Jababeka Integrated Finance Accounting System). 

KNOWLEDGE BASE YANG TERSEDIA:
{systemContext}

Petunjuk pembuatan:
1. JIFAS adalah sistem terintegrasi untuk Finance & Accounting
2. Sebutkan secara ringkas modul-modul utama yang ada di system
3. Jelaskan bahwa AI Assistant ini membantu dengan pertanyaan seputar semua modul yang ada di Knowledge Base
4. Assistant ini HANYA menjawab berdasarkan Knowledge Base JIFAS - jangan membuat informasi baru
5. Berikan 2-3 contoh pertanyaan yang bisa diajukan (berdasarkan modul yang ada)
6. Bahasa: Natural, friendly, tapi profesional
7. Panjang: 2-3 paragraf saja, tidak terlalu panjang

Buatlah respons langsung tanpa penjelasan tambahan.";

                var introMessage = await _ollamaService.CallOllamaApiAsync(prompt);

                response.Message = introMessage;
                response.Source = "JIFAS AI Assistant";
                response.IsFromKnowledgeBase = false;
                response.Success = true;
                
                // Generate dynamic suggestions berdasarkan available topics
                var availableTopics = await _kbContextService.GetAvailableTopicsAsync();
                response.Suggestions = GenerateDynamicSuggestions(availableTopics);

                _logger.LogInformation("[ChatService] JIFAS introduction displayed with dynamic context");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ChatService] Error showing JIFAS introduction: {ex.Message}");
                response.Message = "Selamat datang di JIFAS AI Assistant! Saya siap membantu Anda dengan pertanyaan seputar Finance, Accounting, Budget, PUM, dan sistem JIFAS lainnya. Apa yang bisa saya bantu hari ini?";
                response.Source = "JIFAS AI Assistant";
                response.IsFromKnowledgeBase = false;
                response.Success = true;
                response.Suggestions = new List<string>
                {
                    "Apa itu JIFAS?",
                    "Bagaimana cara membuat Invoice?",
                    "Siapa yang bisa approve invoice?"
                };
            }
        }

        /// <summary>
        /// Generate dynamic suggestions based on available KB topics
        /// </summary>
        private List<string> GenerateDynamicSuggestions(List<string> availableTopics)
        {
            var suggestions = new List<string>();

            // Create suggestions based on available topics
            var topicMap = new Dictionary<string, List<string>>
            {
                { "Invoice", new List<string> { "Bagaimana cara membuat Invoice?", "Apa saja approval untuk Invoice?" } },
                { "Receiving", new List<string> { "Bagaimana proses Receiving barang?", "Apa itu RV dan cara pembuatannya?" } },
                { "Payment", new List<string> { "Bagaimana cara membuat Payment?", "Apa perbedaan Transfer dan BG?" } },
                { "Pum", new List<string> { "Apa itu PUM dan bagaimana pengajuannya?", "Siapa yang bisa approve PUM?" } },
                { "Budget", new List<string> { "Bagaimana cara setup Budget?", "Apa itu Over Budget?" } },
                { "Report", new List<string> { "Laporan apa saja yang tersedia?", "Bagaimana cara membaca Budget Report?" } },
                { "Master", new List<string> { "Apa perbedaan Company dan Division?", "Bagaimana setup Master Data?" } },
                { "Cashbank", new List<string> { "Bagaimana proses Cash & Bank?", "Apa itu Bank Reconciliation?" } },
                { "OverBudget", new List<string> { "Siapa yang bisa approve Over Budget?", "Bagaimana proses Over Budget?" } },
                { "Accounting", new List<string> { "Bagaimana proses Posting di GL?", "Apa itu COA?" } }
            };

            // Add up to 3 suggestions based on available topics
            foreach (var topic in availableTopics.Take(3))
            {
                if (topicMap.ContainsKey(topic))
                {
                    suggestions.AddRange(topicMap[topic].Take(1));
                }
            }

            // If less than 3 suggestions, add generic ones
            if (suggestions.Count < 3)
            {
                suggestions.Add("Bagaimana cara menggunakan JIFAS?");
                if (suggestions.Count < 3)
                    suggestions.Add("Siapa yang dapat melakukan approval?");
            }

            return suggestions.Take(3).ToList();
        }

        /// <summary>
        /// Build conversation context awareness
        /// Improvement #4: Simple context tracking using cache
        /// For full context, would need to extend IChatHistoryService
        /// </summary>
        private string BuildConversationContext(ChatRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request?.SessionId))
                    return null;

                // For now, we track conversation in cache for context awareness
                var contextKey = $"ConversationContext_{request.SessionId}";
                var recentContext = _cacheService.Get<string>(contextKey);
                
                if (string.IsNullOrEmpty(recentContext))
                    return null;

                var contextBuilder = new StringBuilder();
                contextBuilder.AppendLine("KONTEKS PERCAKAPAN SEBELUMNYA:");
                contextBuilder.AppendLine(recentContext);
                contextBuilder.AppendLine("\nGUNAKAN KONTEKS INI UNTUK MEMAHAMI PERTANYAAN FOLLOW-UP!");
                
                _logger.LogInformation("[ChatService] Built conversation context from cache");
                return contextBuilder.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ChatService] Failed to build conversation context: {ex.Message}");
                return null;  // Continue without context if retrieval fails
            }
        }

        /// <summary>
        /// Save chat history to database asynchronously (fire and forget)
        /// </summary>
        private async Task SaveChatHistoryAsync(ChatResponse response, string userMessage, ChatRequest request)
        {
            try
            {
                // Extract document IDs jika ada KB results
                var documentIds = response.KnowledgeBaseResults?.Select(r => r.DocumentId.ToString())
                    .Distinct()
                    .ToList();

                var chatHistory = new ChatHistory
                {
                    SessionId = response.SessionId,
                    UserId = request?.UserId ?? "anonymous",
                    UserMessage = userMessage,
                    AiResponse = response.Message,
                    ResponseSource = response.Source,
                    ConfidenceScore = response.ConfidenceScore,
                    IsFromKnowledgeBase = response.IsFromKnowledgeBase,
                    ResponseTimeMs = response.PerformanceMetrics?.TotalMs ?? 0,
                    Success = response.Success,
                    UsedDocumentIds = documentIds?.Count > 0 ? string.Join(",", documentIds) : null
                };

                // Save asynchronously (don't wait for it to complete)
                _ = _chatHistoryService.SaveChatAsync(chatHistory);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ChatService] Failed to save chat history: {ex.Message}");
                // Don't throw - allow chat to continue even if history save fails
            }
        }

        #region Enhanced Response Generation Methods

        /// <summary>
        /// Generate natural greeting response - OPTIMIZED for natural feel
        /// </summary>
        private async Task<string> GenerateNaturalGreetingAsync()
        {
            try
            {
                // Use random greeting for variety without AI call (faster)
                var greetings = new[]
                {
                    "Hai! Ada yang bisa saya bantu soal JIFAS?",
                    "Halo! Siap membantu pertanyaan seputar JIFAS. Apa yang ingin kamu tahu?",
                    "Hi! Saya JIFAS Assistant. Silakan tanya apa saja tentang Invoice, Payment, PUM, atau modul JIFAS lainnya.",
                    "Halo! Mau tanya apa tentang JIFAS hari ini?",
                    "Hai! Ada pertanyaan tentang sistem JIFAS? Saya siap bantu."
                };
                
                var random = new Random();
                return greetings[random.Next(greetings.Length)];
            }
            catch
            {
                return "Halo! Saya JIFAS AI Assistant. Ada yang bisa saya bantu?";
            }
        }

        /// <summary>
        /// Generate natural gratitude response
        /// </summary>
        private async Task<string> GenerateNaturalGratitudeResponseAsync()
        {
            try
            {
                // Use random response for variety without AI call (faster)
                var responses = new[]
                {
                    "Sama-sama! Kalau ada pertanyaan lagi, langsung tanya saja.",
                    "Senang bisa membantu! ??",
                    "Sip! Hubungi lagi kalau butuh bantuan.",
                    "Sama-sama! Semoga lancar ya.",
                    "No problem! Tanya lagi kapan saja."
                };
                
                var random = new Random();
                return responses[random.Next(responses.Length)];
            }
            catch
            {
                return "Sama-sama! Tanya lagi kalau butuh bantuan.";
            }
        }

        /// <summary>
        /// Generate natural out-of-scope response - OPTIMIZED
        /// </summary>
        private async Task<string> GenerateNaturalOutOfScopeResponseAsync(string userQuery)
        {
            try
            {
                // Fast path: use template with slight variation
                var responses = new[]
                {
                    $"Maaf, '{TruncateForDisplay(userQuery, 30)}' di luar area saya. Saya fokus bantu soal JIFAS - Invoice, Payment, PUM, Budget, dan Approval. Ada yang mau ditanyakan dari topik itu?",
                    $"Hmm, sepertinya itu bukan topik JIFAS. Saya bisa bantu soal Invoice, Payment, PUM, Budget, Receiving, atau Approval. Mau tanya yang mana?",
                    $"Itu di luar scope saya. Saya khusus untuk sistem JIFAS - mulai dari Invoice sampai Payment. Ada pertanyaan tentang itu?"
                };
                
                var random = new Random();
                return await Task.FromResult(responses[random.Next(responses.Length)]);
            }
            catch
            {
                return "Maaf, pertanyaan tersebut di luar cakupan saya. Saya khusus membantu pertanyaan seputar JIFAS seperti Invoice, Payment, PUM, Budget, dan modul lainnya. Ada yang bisa saya bantu tentang JIFAS?";
            }
        }

        /// <summary>
        /// Generate intelligent partial response when KB has some matches
        /// </summary>
        private async Task<string> GenerateIntelligentPartialResponseAsync(
            string userQuery, 
            List<KnowledgeBaseResult> partialResults,
            IntentType intent)
        {
            try
            {
                var kbContext = string.Join("\n\n", partialResults.Take(3).Select(r => 
                    $"[{r.Title}]\n{r.Content.Substring(0, Math.Min(r.Content.Length, 500))}"));

                var prompt = $@"User bertanya tentang JIFAS dan saya menemukan informasi yang SEBAGIAN relevan.

Pertanyaan: ""{userQuery}""
Tipe Intent: {intent}

Informasi yang ditemukan:
{kbContext}

INSTRUKSI:
1. Gunakan informasi di atas untuk menjawab SEBAIK mungkin
2. Jika informasi tidak 100% sesuai, jelaskan apa yang kamu temukan
3. Jangan membuat informasi baru yang tidak ada di atas
4. Jika ada bagian yang tidak bisa dijawab, katakan dengan jelas
5. Berikan saran topik terkait yang mungkin membantu
6. Natural dan helpful, bukan defensif

Respons langsung:";

                return await _ollamaService.CallOllamaApiAsync(prompt);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ChatService] Error generating partial response: {ex.Message}");
                return "Saya menemukan beberapa informasi terkait, namun tidak sepenuhnya menjawab pertanyaan Anda. Silakan coba reformulasi pertanyaan atau hubungi IT Help Desk.";
            }
        }

        /// <summary>
        /// Generate helpful "I don't know" response
        /// </summary>
        private async Task<string> GenerateHelpfulNoMatchResponseAsync(string userQuery, IntentType intent)
        {
            try
            {
                var prompt = $@"User bertanya tentang sesuatu yang TIDAK ada di Knowledge Base JIFAS.

Pertanyaan: ""{userQuery}""
Tipe Intent: {intent}

Buatlah respons yang:
1. JUJUR bahwa informasi tidak ditemukan
2. TIDAK menyalahkan user
3. Berikan alternatif yang KONSTRUKTIF:
   - Mungkin kata kunci yang berbeda
   - Mungkin topik yang lebih spesifik
   - Atau hubungi IT Help Desk
4. Natural dan empatis
5. Singkat (2-3 kalimat)

JANGAN pernah mengarang informasi!

Respons langsung:";

                return await _ollamaService.CallOllamaApiAsync(prompt);
            }
            catch
            {
                return "Maaf, saya tidak menemukan informasi yang relevan untuk pertanyaan Anda di Knowledge Base JIFAS. Coba gunakan kata kunci yang berbeda, atau hubungi IT Help Desk untuk bantuan lebih lanjut.";
            }
        }

        /// <summary>
        /// Generate enhanced response dengan conversation context dan active page context
        /// </summary>
        private async Task<string> GenerateEnhancedResponseAsync(
            string userQuery,
            List<KnowledgeBaseResult> kbResults,
            IntentType intent,
            string conversationContext,
            bool isFollowUp,
            string? activePageContext = null,
            string? userId = null)
        {
            try
            {
                // Bangun user memory context (profil user jangka panjang)
                var userMemoryContext = await _userMemory.BuildUserContextForPromptAsync(userId ?? "anonymous");

                // Gabungkan user memory + active page + conversation context
                var sessionContext = BuildSessionContext(conversationContext, isFollowUp, activePageContext, userMemoryContext);

                // Gunakan prompt engineering dengan session context yang lengkap
                var enhancedQuery = userQuery;
                if (isFollowUp && !string.IsNullOrEmpty(conversationContext))
                {
                    enhancedQuery = $@"{conversationContext}

PERTANYAAN SAAT INI (follow-up): ""{userQuery}""
Jawab dengan mempertimbangkan konteks percakapan sebelumnya.";
                }

                return await _ollamaService.GenerateResponseAsync(enhancedQuery, kbResults, sessionContext: sessionContext);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ChatService] Error generating enhanced response: {ex.Message}");
                return await _ollamaService.GenerateResponseAsync(userQuery, kbResults, sessionContext: activePageContext);
            }
        }

        /// <summary>
        /// Bangun session context string dari active page context di request
        /// Format output: "PAGE:{page}|MODULE:{module}|TITLE:{title}|DOC:{docId}|DOCTYPE:{type}|STATUS:{status}"
        /// </summary>
        private string? BuildActivePageContextFromRequest(ChatRequest? request)
        {
            var ctx = request?.Context;
            if (ctx == null) return null;

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(ctx.CurrentPage))
                parts.Add($"PAGE:{ctx.CurrentPage}");
            if (!string.IsNullOrWhiteSpace(ctx.ActiveModule))
                parts.Add($"MODULE:{ctx.ActiveModule}");
            if (!string.IsNullOrWhiteSpace(ctx.PageTitle))
                parts.Add($"TITLE:{ctx.PageTitle}");
            if (!string.IsNullOrWhiteSpace(ctx.SelectedDocumentId))
                parts.Add($"DOC:{ctx.SelectedDocumentId}");
            if (!string.IsNullOrWhiteSpace(ctx.DocumentType))
                parts.Add($"DOCTYPE:{ctx.DocumentType}");
            if (!string.IsNullOrWhiteSpace(ctx.DocumentStatus))
                parts.Add($"STATUS:{ctx.DocumentStatus}");

            return parts.Count > 0 ? string.Join("|", parts) : null;
        }

        /// <summary>
        /// Bangun session context string dari active page context di request
        /// </summary>
        private string BuildSessionContext(
            string? conversationContext,
            bool isFollowUp,
            string? activePageContext,
            string? userMemoryContext = null)
        {
            var parts = new System.Collections.Generic.List<string>();

            if (!string.IsNullOrEmpty(userMemoryContext))
                parts.Add(userMemoryContext);

            if (!string.IsNullOrEmpty(activePageContext))
                parts.Add(activePageContext);

            if (isFollowUp && !string.IsNullOrEmpty(conversationContext))
                parts.Add(conversationContext);

            return string.Join("\n\n", parts);
        }

        /// <summary>
        /// Regenerate response with quality improvements
        /// </summary>
        private async Task<string> RegenerateImprovedResponseAsync(
            string userQuery,
            List<KnowledgeBaseResult> kbResults,
            IntentType intent,
            QualityValidationResult previousQuality)
        {
            try
            {
                var issues = string.Join(", ", previousQuality.Issues);
                var kbContext = string.Join("\n\n", kbResults.Take(3).Select(r => 
                    $"[{r.Title}]\n{r.Content}"));

                var prompt = $@"Jawaban sebelumnya untuk pertanyaan ini memiliki masalah: {issues}

Pertanyaan: ""{userQuery}""
Intent: {intent}

Knowledge Base:
{kbContext}

Buatlah jawaban yang LEBIH BAIK dengan memperhatikan:
1. Jawaban harus SPESIFIK dan RELEVAN dengan pertanyaan
2. Hanya gunakan informasi dari Knowledge Base (no hallucination)
3. Struktur yang jelas (langkah-langkah jika how-to)
4. Natural dan mudah dipahami
5. Minimal 2-3 kalimat untuk konteks yang cukup

Respons langsung:";

                return await _ollamaService.CallOllamaApiAsync(prompt);
            }
            catch
            {
                // Fallback to original method
                return await _ollamaService.GenerateResponseAsync(userQuery, kbResults);
            }
        }

        /// <summary>
        /// Generate contextual suggestions based on intent and KB results
        /// </summary>
        private async Task<List<string>> GenerateContextualSuggestionsAsync(
            string userQuery,
            IntentType intent,
            List<KnowledgeBaseResult> kbResults)
        {
            try
            {
                // If we have KB results, suggest related topics
                if (kbResults != null && kbResults.Count > 0)
                {
                    var topics = kbResults.Select(r => r.Category).Distinct().Take(2).ToList();
                    var suggestions = new List<string>();

                    foreach (var topic in topics)
                    {
                        suggestions.Add($"Bagaimana cara menggunakan {topic}?");
                    }

                    if (suggestions.Count < 3)
                    {
                        suggestions.Add("Apa saja fitur utama JIFAS?");
                    }

                    return suggestions.Take(3).ToList();
                }

                // Default suggestions based on intent
                return intent switch
                {
                    IntentType.HowTo => new List<string>
                    {
                        "Bagaimana cara membuat Invoice?",
                        "Bagaimana cara approve Payment?",
                        "Apa langkah-langkah mengajukan PUM?"
                    },
                    IntentType.Troubleshooting => new List<string>
                    {
                        "Apa yang harus dilakukan jika Invoice gagal di-approve?",
                        "Bagaimana cara mengatasi Budget Over?",
                        "Kenapa Payment tidak bisa diproses?"
                    },
                    _ => new List<string>
                    {
                        "Apa itu JIFAS?",
                        "Modul apa saja yang ada di JIFAS?",
                        "Bagaimana cara mengakses JIFAS?"
                    }
                };
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Merge KB results from multiple searches, removing duplicates
        /// </summary>
        private List<KnowledgeBaseResult> MergeKBResults(List<KnowledgeBaseResult> primary, List<KnowledgeBaseResult> secondary)
        {
            var merged = new Dictionary<int, KnowledgeBaseResult>();

            // Add primary results
            foreach (var result in primary)
            {
                merged[result.DocumentId] = result;
            }

            // Add secondary results (only if not already present)
            foreach (var result in secondary)
            {
                if (!merged.ContainsKey(result.DocumentId))
                {
                    merged[result.DocumentId] = result;
                }
                else
                {
                    // Average the scores if same document found in both
                    var existing = merged[result.DocumentId];
                    existing.Score = (existing.Score + result.Score) / 2;
                }
            }

            return merged.Values.OrderByDescending(r => r.Score).ToList();
        }

        /// <summary>
        /// Truncate text for display purposes
        /// </summary>
        private string TruncateForDisplay(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "...";
        }

        #endregion
    }
}


