using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Jifas.Assistant.Models;
using Jifas.Assistant.Utilities;
using jifas_assistant.DAL.Models;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Main chat service that orchestrates all JIFAS AI Assistant components
    /// Strictly knowledge base only - NO general AI responses
    /// With comprehensive performance tracking for response time metrics
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
        private readonly IInputValidator _inputValidator;
        private readonly IChatHistoryService _chatHistoryService;

        private const double MIN_KB_CONFIDENCE = 0.5;
        private const int MIN_KB_RESULTS_REQUIRED = 1;

        public ChatService(
            IGeminiService geminiService,
            IKnowledgeBaseService knowledgeBaseService,
            IOutOfScopeDetector outOfScopeDetector,
            ISuggestionService suggestionService,
            ILoggerService logger,
            ICacheService cacheService,
            IJifasContextService jifasContextService,
            IConfiguration configuration,
            IInputValidator inputValidator,
            IChatHistoryService chatHistoryService)
        {
            _geminiService = geminiService;
            _knowledgeBaseService = knowledgeBaseService;
            _outOfScopeDetector = outOfScopeDetector;
            _suggestionService = suggestionService;
            _logger = logger;
            _cacheService = cacheService;
            _jifasContextService = jifasContextService;
            _configuration = configuration;
            _inputValidator = inputValidator ?? throw new ArgumentNullException(nameof(inputValidator));
            _chatHistoryService = chatHistoryService ?? throw new ArgumentNullException(nameof(chatHistoryService));
        }

        public async Task<ChatResponse> ProcessMessageAsync(ChatRequest request)
        {
            // ?? START TOTAL TIMER
            var totalStopwatch = Stopwatch.StartNew();

            var response = new ChatResponse
            {
                Sender = "JIFAS AI Assistant",
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Success = true,
                SessionId = request?.SessionId ?? Guid.NewGuid().ToString()
            };

            // ?? INITIALIZE PERFORMANCE METRICS
            var metrics = new PerformanceMetrics();

            var isFirstMessage = string.IsNullOrWhiteSpace(request?.SessionId);
            
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
                _logger.LogWarning($"[ChatService] Input validation failed: {validationResult.ErrorMessage}");
                
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

                // ?? STEP 2: SCOPE DETECTION
                var scopeStopwatch = Stopwatch.StartNew();
                var scopeCheckResult = await _outOfScopeDetector.CheckScopeAsync(userMessage);
                scopeStopwatch.Stop();
                metrics.ScopeDetectionMs = scopeStopwatch.ElapsedMilliseconds;
                
                if (!scopeCheckResult.IsInScope)
                {
                    response.Message = scopeCheckResult.Message;
                    response.Source = "Out of Scope";
                    response.IsFromKnowledgeBase = false;
                    response.Suggestions = await _suggestionService.GenerateSuggestionsAsync(userMessage, response.Message);
                    
                    totalStopwatch.Stop();
                    metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                    response.PerformanceMetrics = metrics;
                    _logger.LogInformation($"[OUT_OF_SCOPE] {metrics.GetSummary()}");
                    
                    // Save chat history asynchronously
                    _ = SaveChatHistoryAsync(response, userMessage, request);
                    
                    return response;
                }

                // ?? STEP 3: KNOWLEDGE BASE SEARCH
                var kbSearchStopwatch = Stopwatch.StartNew();
                var kbResults = await _knowledgeBaseService.SearchAsync(userMessage, topK: 3);
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

                // ?? STEP 5: CONFIDENCE CALCULATION
                var confidenceStopwatch = Stopwatch.StartNew();
                var confidenceScore = CalculateKBConfidence(validatedResults.Count > 0 ? validatedResults : kbResults);
                confidenceStopwatch.Stop();
                metrics.ConfidenceCalculationMs = confidenceStopwatch.ElapsedMilliseconds;
                metrics.AverageKbScore = validatedResults.Count > 0 ? validatedResults.Average(r => r.Score) : 0;
                
                _logger.LogInformation($"[ChatService] KB Search ({metrics.KbSearchMs}ms): {validatedResults.Count} valid results, Confidence: {confidenceScore:F2}");

                 // If confidence is too low or no results found, generate natural response using Gemini
                 if (confidenceScore < MIN_KB_CONFIDENCE || validatedResults == null || validatedResults.Count < MIN_KB_RESULTS_REQUIRED)
                 {
                     // FIX #5: SMARTER FALLBACK LOGIC with metadata
                     var llmStopwatch = Stopwatch.StartNew();
                     
                     // Use partial match response if we have some results, otherwise no-match
                     string noMatchMessage;
                     if (validatedResults != null && validatedResults.Count > 0)
                     {
                         noMatchMessage = await GeneratePartialMatchResponseAsync(userMessage, validatedResults);
                     }
                     else
                     {
                         noMatchMessage = await GenerateNoMatchResponseAsync(userMessage);
                     }
                     
                     llmStopwatch.Stop();
                     metrics.LlmResponseMs = (long)llmStopwatch.Elapsed.TotalMilliseconds;
                     
                     response.Message = noMatchMessage;
                     response.Source = "Out of Scope - Low KB Match";
                     response.IsFromKnowledgeBase = false;
                     response.ConfidenceScore = confidenceScore;
                     response.KnowledgeBaseResults = validatedResults ?? kbResults ?? new List<KnowledgeBaseResult>();
                     
                     // FIX #5: Generate helpful KB-specific suggestions for better results
                     var suggestionsStopwatch = Stopwatch.StartNew();
                     var suggestions = await _suggestionService.GenerateSuggestionsAsync(userMessage, noMatchMessage);
                     suggestionsStopwatch.Stop();
                     metrics.SuggestionsMs = (long)suggestionsStopwatch.Elapsed.TotalMilliseconds;
                     response.Suggestions = suggestions;
                     
                     // FIX #5: Add debug metadata for monitoring and improvement
                     var debugMetadata = new Dictionary<string, object>
                     {
                         ["ConfidenceScore"] = confidenceScore,
                         ["KBResultsCount"] = validatedResults?.Count ?? 0,
                         ["TopResultScore"] = validatedResults?.FirstOrDefault()?.Score ?? 0,
                         ["QueryProcessingTimeMs"] = metrics.KbSearchMs,
                         ["FailureReason"] = confidenceScore < MIN_KB_CONFIDENCE ? "LowConfidence" : "NoResults"
                     };
                     _logger.LogWarning($"[LOW_CONFIDENCE] Details: {string.Join(", ", debugMetadata.Select(kv => $"{kv.Key}={kv.Value}"))}");
                    
                    totalStopwatch.Stop();
                    metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                    response.PerformanceMetrics = metrics;
                    
                    _logger.LogWarning($"[FALLBACK_RESPONSE] Confidence({confidenceScore:F2}) | {metrics.GetSummary()}");
                    
                    // Save chat history asynchronously
                    _ = SaveChatHistoryAsync(response, userMessage, request);
                    
                    return response;
                }

                 // ?? STEP 6: GENERATE KB-BASED RESPONSE
                 var responseStopwatch = Stopwatch.StartNew();
                 var aiResponse = await _geminiService.GenerateResponseAsync(userMessage, validatedResults);
                 responseStopwatch.Stop();
                 metrics.LlmResponseMs = (long)responseStopwatch.Elapsed.TotalMilliseconds;

                 response.Message = aiResponse;
                 response.Source = "Knowledge Base (100% dari KB)";
                 response.IsFromKnowledgeBase = true;
                 response.ConfidenceScore = confidenceScore;
                 response.KnowledgeBaseResults = validatedResults;
                 response.Success = true;

                 // ?? STEP 7: GENERATE SUGGESTIONS (with caching)
                 var suggestionsStopwatch2 = Stopwatch.StartNew();
                 if (enableCache)
                 {
                     var suggestionCacheKey = $"Suggestions_{HashHelper.ToShortStableHash(aiResponse)}";
                     var cachedResult = _cacheService.Get<List<string>>(suggestionCacheKey);
                     
                     if (cachedResult != null)
                     {
                         _logger.LogInformation("[ChatService] ?? Suggestions cache HIT");
                         response.Suggestions = cachedResult;
                         metrics.SuggestionsCached = true;
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
                
                _logger.LogInformation($"[KB_RESPONSE] {metrics.GetSummary()}");
                
                // Save chat history asynchronously
                _ = SaveChatHistoryAsync(response, userMessage, request);
                
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

                totalStopwatch.Stop();
                metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                response.PerformanceMetrics = metrics;
                
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
            
            return confidence;
        }

        /// <summary>
        /// Generate natural no-match response using Gemini
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

                var response = await _geminiService.CallGeminiApiAsync(prompt);
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
        /// Generates natural welcome message from Gemini with system context
        /// </summary>
        private async Task ShowJIFASIntroductionAsync(ChatResponse response)
        {
            try
            {
                var prompt = @"Buatlah salam pembuka yang natural dan profesional untuk AI Assistant JIFAS (Jababeka Integrated Finance Accounting System). 

Informasi yang harus disampaikan:
1. JIFAS adalah sistem terintegrasi untuk Finance & Accounting
2. AI Assistant ini bisa membantu pertanyaan tentang:
   - AR (Account Receivable): Invoice, penerimaan pembayaran, approval
   - AP (Account Payable): Invoice, pembayaran, approval proses
   - GL (General Ledger): Chart of Account, posting transaksi
   - Budget: Setup, approval, monitoring
   - PUM (Dana untuk Pengeluaran Mendadak): Pengajuan, approval, realisasi
   - Master Data: Company, Department, Division, Employee, Vendor, dll
   - Payment: Transfer, BG, Payment Process
   - Over Budget: Approval workflows
3. Assistant ini HANYA menjawab berdasarkan Knowledge Base JIFAS
4. Berikan contoh 2-3 pertanyaan yang bisa diajukan
5. Bahasa: Natural, friendly, tapi profesional
6. Panjang: 2-3 paragraf saja, tidak terlalu panjang

Buatlah respons langsung tanpa penjelasan tambahan.";

                var introMessage = await _geminiService.CallGeminiApiAsync(prompt);

                response.Message = introMessage;
                response.Source = "JIFAS AI Assistant";
                response.IsFromKnowledgeBase = false;
                response.Success = true;
                response.Suggestions = new List<string>
                {
                    "Bagaimana cara membuat Invoice di AR?",
                    "Apa itu PUM dan bagaimana pengajuannya?",
                    "Siapa saja yang bisa approve over budget?"
                };

                _logger.LogInformation("[ChatService] JIFAS introduction displayed to new user");
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
    }
}

