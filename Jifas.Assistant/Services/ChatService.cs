using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Jifas.Assistant.Models;
using Jifas.Assistant.Utilities;
using jifas_assistant.DAL.Models;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Orchestrator utama chatbot JIFAS.
    /// Service ini mengatur validasi input, ticket flow, cache, pencarian KB, LLM, dan monitoring.
    /// Jawaban dibatasi ke konteks JIFAS/Knowledge Base agar AI tidak menjawab topik umum yang tidak relevan.
    /// </summary>
    public interface IChatService
    {
        Task<ChatResponse> ProcessMessageAsync(ChatRequest request, CancellationToken cancellationToken = default);
    }

    public class ChatService : IChatService
    {
        private readonly IOllamaService _ollamaService;
        private readonly IKnowledgeBaseService _knowledgeBaseService;
        private readonly IOutOfScopeDetector _outOfScopeDetector;
        private readonly ILoggerService _logger;
        private readonly ICacheService _cacheService;
        private readonly IJifasContextService _jifasContextService;
        private readonly IKnowledgeBaseContextService _kbContextService;
        private readonly ILocalizationService _localizationService;
        private readonly IConfiguration _configuration;
        private readonly IInputValidator _inputValidator;
        private readonly IChatHistoryService _chatHistoryService;
        
        // Service kualitas AI: intent detection, validasi jawaban, memori percakapan, dan context packing.
        private readonly IQueryUnderstandingService _queryUnderstanding;
        private readonly IResponseQualityService _responseQuality;
        private readonly IConversationIntelligenceService _conversationIntelligence;
        private readonly IUserMemoryService _userMemory;
        private readonly IAdaptiveContextPackService _contextPackService;
        private readonly IEmbeddingService _embeddingService;
        private readonly ITicketService _ticketService;

        private const int MIN_KB_RESULTS_REQUIRED = 1;
        private const int MAX_REGENERATION_ATTEMPTS = 2;

        public ChatService(
            IOllamaService ollamaService,
            IKnowledgeBaseService knowledgeBaseService,
            IOutOfScopeDetector outOfScopeDetector,
            ILoggerService logger,
            ICacheService cacheService,
            IJifasContextService jifasContextService,
            IKnowledgeBaseContextService kbContextService,
            ILocalizationService localizationService,
            IConfiguration configuration,
            IInputValidator inputValidator,
            IChatHistoryService chatHistoryService,
            IQueryUnderstandingService queryUnderstanding,
            IResponseQualityService responseQuality,
            IConversationIntelligenceService conversationIntelligence,
            IUserMemoryService userMemory,
            IAdaptiveContextPackService contextPackService,
            IEmbeddingService embeddingService,
            ITicketService ticketService)
        {
            _ollamaService = ollamaService;
            _knowledgeBaseService = knowledgeBaseService;
            _outOfScopeDetector = outOfScopeDetector;
            _logger = logger;
            _cacheService = cacheService;
            _jifasContextService = jifasContextService;
            _kbContextService = kbContextService ?? throw new ArgumentNullException(nameof(kbContextService));
            _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
            _configuration = configuration;
            _inputValidator = inputValidator ?? throw new ArgumentNullException(nameof(inputValidator));
            _chatHistoryService = chatHistoryService ?? throw new ArgumentNullException(nameof(chatHistoryService));
            _queryUnderstanding = queryUnderstanding ?? throw new ArgumentNullException(nameof(queryUnderstanding));
            _responseQuality = responseQuality ?? throw new ArgumentNullException(nameof(responseQuality));
            _conversationIntelligence = conversationIntelligence ?? throw new ArgumentNullException(nameof(conversationIntelligence));
            _userMemory = userMemory ?? throw new ArgumentNullException(nameof(userMemory));
            _contextPackService = contextPackService ?? throw new ArgumentNullException(nameof(contextPackService));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _ticketService = ticketService ?? throw new ArgumentNullException(nameof(ticketService));
        }

        public async Task<ChatResponse> ProcessMessageAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Timer total dipakai untuk performance metrics di response dan monitoring.
            var totalStopwatch = Stopwatch.StartNew();

            // CorrelationId memudahkan tracing satu request dari log sampai response API.
            var correlationId = request?.CorrelationId ?? Guid.NewGuid().ToString();

            var response = new ChatResponse
            {
                Sender = "JIFAS AI Assistant",
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                Success = true,
                SessionId = request?.SessionId ?? Guid.NewGuid().ToString(),
                CorrelationId = correlationId
            };

            // Metrics diisi bertahap di setiap fase pemrosesan.
            var metrics = new PerformanceMetrics();

            // Context monitoring diset sedini mungkin agar semua call AI punya user/session/module.
            _ollamaService.SetCallContext(
                userId:       request?.UserId,
                sessionId:    request?.SessionId,
                activeModule: request?.Context?.ActiveModule,
                callType:     "chat");

            var isFirstMessage = string.IsNullOrWhiteSpace(request?.SessionId);

            // Log context dari frontend untuk membantu analisis issue company/module/page.
            _logger.LogDebug(
                $"[ChatService] Processing message — UserId: {request?.UserId}, " +
                $"IsFirstMessage: {isFirstMessage} (or request field: {request?.IsFirstMessage}), " +
                $"UserRole: {request?.UserRole}, " +
                $"UserCompCode: {request?.UserCompCode}, " +
                $"ActiveModule: {request?.Context?.ActiveModule}");

            var jifasIntroductionKey = $"JIFAS_Intro_{response.SessionId}";
            var hasSeenIntroduction = _cacheService.Get<bool>(jifasIntroductionKey);

            // Step 1: validasi input sebelum menyentuh cache, KB, atau LLM.
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
                
                // Simpan riwayat agar request invalid tetap bisa diaudit.
                await SaveChatHistoryAsync(response, validationResult.ErrorMessage ?? "", request, cancellationToken);
                
                return response;
            }

            var userMessage = validationResult.Value.Message;

            try
            {
                // Ticket flow harus diproses lebih dulu karena user sedang berada di dialog pembuatan tiket.
                if (_ticketService.IsInTicketFlow(response.SessionId))
                {
                    _logger.LogInformation($"[ChatService] User is in active ticket flow - routing to TicketService");
                    var ticketDialogResult = await _ticketService.HandleTicketDialogAsync(
                        response.SessionId, request?.UserId ?? "anonymous", userMessage, cancellationToken: cancellationToken);

                    response.Message = ticketDialogResult.Message;
                    response.Source = "Ticket Flow";
                    response.IsFromKnowledgeBase = false;
                    response.Success = true;
                    response.Suggestions = ticketDialogResult.Suggestions ?? new List<string>();

                    if (ticketDialogResult.Ticket != null)
                    {
                        response.Ticket = new TicketInfo
                        {
                            TicketNumber = ticketDialogResult.Ticket.TicketNumber,
                            Status = ticketDialogResult.Ticket.Status,
                            Message = ticketDialogResult.Ticket.Message,
                            Url = ticketDialogResult.Ticket.Url
                        };
                    }

                    totalStopwatch.Stop();
                    metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                    response.PerformanceMetrics = metrics;
                    await SaveChatHistoryAsync(response, userMessage, request, cancellationToken);
                    return response;
                }

                // Cache response dicek sebelum KB/LLM agar pertanyaan berulang bisa dijawab cepat dari Redis.
                var cacheStopwatch = Stopwatch.StartNew();
                var enableCache = _configuration.GetValue<bool>("Caching:EnableResponseCache");
                if (enableCache && !string.IsNullOrWhiteSpace(userMessage))
                {
                    var cacheScope = BuildResponseCacheScope(userMessage, request);
                    metrics.CacheScope = cacheScope;
                    var cacheKey = BuildResponseCacheKey(userMessage, request);
                    var cachedResponse = _cacheService.Get<ChatResponse>(cacheKey);
                    cacheStopwatch.Stop();
                    metrics.CacheLookupMs = cacheStopwatch.ElapsedMilliseconds;
                    
                    if (cachedResponse != null)
                    {
                        cachedResponse.Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                        cachedResponse.SessionId = response.SessionId;
                        cachedResponse.CorrelationId = correlationId;
                        cachedResponse.PerformanceMetrics ??= new PerformanceMetrics();
                        cachedResponse.PerformanceMetrics.WasCacheLit = true;
                        cachedResponse.PerformanceMetrics.CacheScope = cacheScope;
                        cachedResponse.PerformanceMetrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                        _logger.LogInformation($"[ChatService] Response cache HIT - Total: {cachedResponse.PerformanceMetrics.TotalMs}ms | {cachedResponse.PerformanceMetrics.GetSummary()}");
                        return cachedResponse;
                    }
                }

                // Pesan pertama sesi menampilkan intro JIFAS satu kali per session.
                if (isFirstMessage && !hasSeenIntroduction)
                {
                    _logger.LogInformation($"[ChatService] New session detected - showing JIFAS introduction");
                    await ShowJIFASIntroductionAsync(response, cancellationToken);
                    _cacheService.Set(jifasIntroductionKey, true, 24 * 60);
                    
                    totalStopwatch.Stop();
                    metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                    response.PerformanceMetrics = metrics;
                    _logger.LogInformation($"[INTRO] {metrics.GetSummary()}");
                    
                    // Simpan intro agar histori sesi tetap lengkap.
                    await SaveChatHistoryAsync(response, "[JIFAS Introduction]", request, cancellationToken);
                    
                    return response;
                }

                // Step 2: pahami intent dan scope pertanyaan sebelum pencarian KB.
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
                    response.Suggestions = new List<string>();
                    
                    totalStopwatch.Stop();
                    metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                    response.PerformanceMetrics = metrics;
                    await SaveChatHistoryAsync(response, userMessage, request, cancellationToken);
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
                    await SaveChatHistoryAsync(response, userMessage, request, cancellationToken);
                    return response;
                }

                // Step 2A: deteksi apakah user ingin membuat tiket support.
                if (intentResult.Intent == IntentType.TicketRequest)
                {
                    _logger.LogInformation("[ChatService] Ticket request detected - starting ticket flow");
                    var ticketDialogResult = await _ticketService.HandleTicketDialogAsync(
                        response.SessionId, request?.UserId ?? "anonymous", userMessage, cancellationToken: cancellationToken);

                    response.Message = ticketDialogResult.Message;
                    response.Source = "Ticket Flow";
                    response.IsFromKnowledgeBase = false;
                    response.Success = true;
                    response.Suggestions = ticketDialogResult.Suggestions ?? new List<string>();

                    if (ticketDialogResult.Ticket != null)
                    {
                        response.Ticket = new TicketInfo
                        {
                            TicketNumber = ticketDialogResult.Ticket.TicketNumber,
                            Status = ticketDialogResult.Ticket.Status,
                            Message = ticketDialogResult.Ticket.Message,
                            Url = ticketDialogResult.Ticket.Url
                        };
                    }

                    totalStopwatch.Stop();
                    metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                    response.PerformanceMetrics = metrics;
                    await SaveChatHistoryAsync(response, userMessage, request, cancellationToken);
                    return response;
                }

                // Step 2B: cek apakah pertanyaan masih berada dalam scope JIFAS.
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
                    response.Suggestions = new List<string>();
                    
                    totalStopwatch.Stop();
                    metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                    response.PerformanceMetrics = metrics;
                    _logger.LogInformation($"[OUT_OF_SCOPE] {metrics.GetSummary()}");
                    
                    await SaveChatHistoryAsync(response, userMessage, request, cancellationToken);
                    return response;
                }

                // Step 2C: perluas query agar pencarian KB menangkap istilah sinonim.
                var expandedQuery = await _queryUnderstanding.ExpandQueryAsync(userMessage);
                _logger.LogDebug($"[ChatService] Query expanded: Keywords={expandedQuery.Keywords.Count}, Synonyms={expandedQuery.Synonyms.Count}");

                // Step 2D: bangun konteks percakapan, halaman aktif, dan memori user.
                var conversationContext = await _conversationIntelligence.GetFormattedContextAsync(response.SessionId);
                var isFollowUp = await _conversationIntelligence.IsFollowUpQueryAsync(response.SessionId, userMessage);
                if (isFollowUp)
                {
                    _logger.LogInformation("[ChatService] Detected follow-up question - using conversation context");
                }

                // Pass conversation turns to Ollama for true multi-turn context
                var fullContext = await _conversationIntelligence.BuildContextAsync(response.SessionId);
                if (fullContext?.RecentTurns?.Count > 0)
                {
                    var turns = fullContext.RecentTurns
                        .Select(t => (t.UserMessage, t.AssistantResponse))
                        .ToList();
                    _ollamaService.SetConversationHistory(turns);
                }
                else
                {
                    _ollamaService.SetConversationHistory(null);
                }

                // Step 3: cari Knowledge Base dengan hybrid search keyword + semantic pgvector.
                var kbSearchStopwatch = Stopwatch.StartNew();

                // Generate query embedding for semantic search
                float[]? queryEmbedding = null;
                try
                {
                    queryEmbedding = await _embeddingService.GenerateEmbeddingAsFloatArrayAsync(userMessage, cancellationToken);
                    if (queryEmbedding != null && queryEmbedding.Length > 0)
                        _logger.LogInformation($"[ChatService] Generated query embedding: {queryEmbedding.Length} dimensions");
                }
                catch (Exception embEx)
                {
                    _logger.LogWarning($"[ChatService] Embedding generation failed, falling back to keyword-only: {embEx.Message}");
                }

                // Hybrid search: keyword + semantic embedding
                var kbResults = await _knowledgeBaseService.SearchWithEmbeddingAsync(userMessage, queryEmbedding, topK: 5, cancellationToken: cancellationToken);

                // If results are weak, also search with expanded terms
                if (kbResults == null || kbResults.Count < 2 || (kbResults.Count > 0 && kbResults.Max(r => r.Score) < 0.3))
                {
                    var expandedResults = await _knowledgeBaseService.SearchWithEmbeddingAsync(expandedQuery.EnhancedSearchQuery, queryEmbedding, topK: 3, cancellationToken: cancellationToken);
                    if (expandedResults != null && expandedResults.Count > 0)
                    {
                        kbResults = MergeKBResults(kbResults ?? new List<KnowledgeBaseResult>(), expandedResults);
                        _logger.LogInformation($"[ChatService] Enhanced search with query expansion - merged {expandedResults.Count} additional results");
                    }
                }
                
                kbSearchStopwatch.Stop();
                metrics.KbSearchMs = kbSearchStopwatch.ElapsedMilliseconds;
                metrics.KbResultsBeforeValidation = kbResults?.Count ?? 0;

                // Step 4: validasi hasil KB supaya jawaban tidak mengambil sumber yang lemah.
                var validationStopwatch2 = Stopwatch.StartNew();
                var validatedResults = ValidateKBResults(kbResults ?? new List<KnowledgeBaseResult>());
                validationStopwatch2.Stop();
                metrics.ResultValidationMs = validationStopwatch2.ElapsedMilliseconds;
                metrics.KbResultsAfterValidation = validatedResults.Count;

                if (validatedResults.Count == 0 && kbResults?.Count > 0)
                {
                    _logger.LogWarning($"[ChatService] KB results validation failed - all results filtered out");
                }

                // Step 5: hitung confidence adaptif berdasarkan intent, relevance, dan konteks.
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

                // Claude Code-inspired context packing: give the model a compact briefing,
                // not just raw snippets, so intent and constraints stay visible.
                var activePageContext = BuildActivePageContextFromRequest(request);
                var contextPack = await _contextPackService.BuildAsync(
                    request,
                    userMessage,
                    intentResult,
                    expandedQuery,
                    validatedResults.Count > 0 ? validatedResults : kbResults ?? new List<KnowledgeBaseResult>(),
                    conversationContext,
                    isFollowUp,
                    activePageContext,
                    confidenceScore);

                // Step 5B: tangani low confidence dengan jawaban aman dan arahan support.
                if (!_responseQuality.ShouldGenerateResponse(confidenceResult))
                {
                    var llmStopwatch = Stopwatch.StartNew();
                    
                    // Generate intelligent response based on what we have
                    string fallbackMessage;
                    if (validatedResults != null && validatedResults.Count > 0)
                    {
                        // We have some results - try to help with partial info
                        fallbackMessage = await GenerateIntelligentPartialResponseAsync(
                            userMessage,
                            validatedResults,
                            intentResult.Intent,
                            contextPack.FormattedContext,
                            userId: request?.UserId,
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        // No results - use system knowledge to answer
                        fallbackMessage = await GenerateHelpfulNoMatchResponseAsync(
                            userMessage,
                            intentResult.Intent,
                            contextPack.FormattedContext,
                            userId: request?.UserId,
                            cancellationToken: cancellationToken);
                    }
                    
                    llmStopwatch.Stop();
                    metrics.LlmResponseMs = (long)llmStopwatch.Elapsed.TotalMilliseconds;
                    
                    response.Message = fallbackMessage;
                    response.Source = validatedResults?.Count > 0 ? "Partial Match" : "JIFAS System Knowledge";
                    response.IsFromKnowledgeBase = validatedResults?.Count > 0;
                    response.ConfidenceScore = confidenceScore;
                    response.KnowledgeBaseResults = validatedResults ?? kbResults ?? new List<KnowledgeBaseResult>();
                    
                    response.Suggestions = new List<string>();
                    metrics.SuggestionsMs = 0;
                    metrics.SuggestionsCached = false;
                    
                    totalStopwatch.Stop();
                    metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                    response.PerformanceMetrics = metrics;
                    
                    _logger.LogInformation($"[PARTIAL_RESPONSE] Confidence({confidenceScore:F2}) | {metrics.GetSummary()}");
                    
                    await SaveChatHistoryAsync(response, userMessage, request, cancellationToken);
                    return response;
                }

                // Step 6: generate jawaban berbasis KB dengan konteks yang sudah dipadatkan.
                var responseStopwatch = Stopwatch.StartNew();

                var aiResponse = await GenerateEnhancedResponseAsync(
                    userMessage, 
                    validatedResults, 
                    intentResult.Intent,
                    conversationContext,
                    isFollowUp,
                    activePageContext,
                    contextPack.FormattedContext,
                    userId: request?.UserId,
                    cancellationToken: cancellationToken);
                responseStopwatch.Stop();
                metrics.LlmResponseMs = (long)responseStopwatch.Elapsed.TotalMilliseconds;

                // Step 6B: cek kualitas jawaban sebelum dikirim ke user.
                var qualityResult = await _responseQuality.ValidateResponseAsync(userMessage, aiResponse, validatedResults);
                
                // If quality is poor, try to regenerate once
                if (qualityResult.ShouldRegenerate && qualityResult.OverallScore < 0.4)
                {
                    _logger.LogWarning($"[ChatService] Low quality response detected ({qualityResult.OverallScore:P0}), regenerating...");
                    
                    aiResponse = await RegenerateImprovedResponseAsync(userMessage, validatedResults, intentResult.Intent, qualityResult, cancellationToken);
                    
                    // Re-validate
                    qualityResult = await _responseQuality.ValidateResponseAsync(userMessage, aiResponse, validatedResults);
                }
                
                _logger.LogInformation($"[ChatService] Response quality: {qualityResult.OverallScore:P0}, Grounding: {qualityResult.GroundingScore:P0}");

                 response.Message = aiResponse;
                 response.Source = validatedResults.Count > 0 
                     ? $"JIFAS ({validatedResults.Count} hasil)"
                     : "JIFAS System Knowledge";
                 response.IsFromKnowledgeBase = validatedResults.Count > 0;
                 response.ConfidenceScore = confidenceScore;
                 response.KnowledgeBaseResults = validatedResults;
                 response.Success = true;

                 // Step 7: suggestion terpisah dimatikan untuk produksi.
                 // Arahan lanjutan sudah diminta di prompt utama agar tidak ada LLM call kedua.
                 response.Suggestions = new List<string>();
                 metrics.SuggestionsMs = 0;
                 metrics.SuggestionsCached = false;

                // Step 8: simpan response final ke Redis agar request identik bisa cepat.
                var cacheStopwatch2 = Stopwatch.StartNew();
                if (enableCache && response.Success)
                {
                    metrics.CacheScope = BuildResponseCacheScope(userMessage, request);
                    var cacheKey = BuildResponseCacheKey(userMessage, request);
                    var cacheDuration = _configuration.GetValue<int>("Caching:ResponseCacheDurationHours", 24);
                    _cacheService.Set(cacheKey, response, cacheDuration * 60);
                }
                cacheStopwatch2.Stop();
                metrics.CachingMs = cacheStopwatch2.ElapsedMilliseconds;

                totalStopwatch.Stop();
                metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                response.PerformanceMetrics = metrics;
                response.CorrelationId = correlationId;
                
                // Catat performance metrics untuk monitoring.
                _logger.LogPerformance("ChatProcessing", metrics.TotalMs, correlationId);
                _logger.LogInformationWithCorrelation(correlationId, $"[KB_RESPONSE] {metrics.GetSummary()}");
                
                // Catat audit trail request berhasil.
                _logger.LogAudit(request?.UserId ?? "Unknown", "ProcessMessage", 
                    $"Source: {response.Source}, Confidence: {response.ConfidenceScore:F2}", correlationId);

                // Save chat history asynchronously
                await SaveChatHistoryAsync(response, userMessage, request, cancellationToken);

                // Update long-term user memory after a successful grounded response.
                // Pass new fields: isFirstMessage, userCompCode, userEmpCode, userRole, currentModule
                await _userMemory.UpdateMemoryAsync(
                    request?.UserId ?? "anonymous",
                    userMessage,
                    aiResponse,
                    intentResult.Intent,
                    confidenceScore,
                    currentModule: request?.Context?.ActiveModule,
                    userRole: request?.UserRole,
                    sessionId: response.SessionId);

                // Ekstraksi pola user tetap best-effort, tetapi harus awaited agar scoped DbContext
                // tidak dipakai setelah request selesai dan service scope sudah disposed.
                try
                {
                    await _userMemory.ExtractAndPersistPatternsAsync(
                        request?.UserId ?? "anonymous",
                        userMessage,
                        aiResponse,
                        sessionId: response.SessionId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[ChatService] Pattern extraction skipped: {ex.Message}");
                }

                _logger.LogDebug(
                    $"[ChatService] Memory updated for user: {request?.UserId}, " +
                    $"Message count incremented, Expertise calculated");

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogErrorWithCorrelation(correlationId, $"[ChatService] Error processing message", ex);
                
                response.Message = "Mohon maaf, terjadi kesalahan dalam memproses pertanyaan Anda. Silakan coba lagi.";
                response.Source = "Error";
                response.Success = false;
                response.CorrelationId = correlationId;
                // Detail exception hanya ditulis ke log. Response user cukup membawa correlation id.
                response.Errors.Add($"Terjadi kesalahan internal. CorrelationId: {correlationId}");
                
                response.Suggestions = new List<string>();

                totalStopwatch.Stop();
                metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                response.PerformanceMetrics = metrics;
                response.CorrelationId = correlationId;
                
                // Catat audit trail untuk request yang gagal.
                _logger.LogAudit(request?.UserId ?? "Unknown", "ProcessMessage_ERROR", 
                    $"{ex.GetType().Name}: {ex.Message}", correlationId);
                _logger.LogPerformance("ChatProcessing_Error", metrics.TotalMs, correlationId);
                
                // Save chat history asynchronously
                await SaveChatHistoryAsync(response, userMessage ?? "", request, cancellationToken);
                
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
                _logger.LogInformation($"[ChatService] KB validation: {results.Count} -> {validated.Count} results " +
                                      $"({results.Count - validated.Count} filtered out)");
            }

            return validated;
        }

        /// <summary>
        /// Display AI introduction to JIFAS system on first user message
        /// Generates natural welcome message from Ollama with DYNAMIC system context
        /// </summary>
        private async Task ShowJIFASIntroductionAsync(ChatResponse response, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Get dynamic context dari Knowledge Base files
                var systemContext = await _kbContextService.GetSystemContextAsync();

                var prompt = $@"Buatlah salam pembuka yang natural dan profesional untuk AI Assistant JIFAS (Jababeka Integrated Finance Accounting System). 

INFORMASI SISTEM JIFAS:
{systemContext}

Petunjuk pembuatan:
1. JIFAS adalah sistem terintegrasi untuk Finance & Accounting
2. Sebutkan secara ringkas modul-modul utama yang ada di system
3. Jelaskan bahwa AI Assistant ini membantu dengan pertanyaan seputar semua modul yang ada di sistem JIFAS
4. Jawab berdasarkan informasi yang kamu punya tentang JIFAS - jangan membuat informasi baru
5. Berikan 2-3 contoh pertanyaan yang bisa diajukan (berdasarkan modul yang ada)
6. Bahasa: Natural, friendly, tapi profesional
7. Panjang: 2-3 paragraf saja, tidak terlalu panjang

Buatlah respons langsung tanpa penjelasan tambahan.";

                var introMessage = await _ollamaService.CallOllamaApiAsync(prompt, cancellationToken);

                response.Message = introMessage;
                response.Source = "JIFAS AI Assistant";
                response.IsFromKnowledgeBase = false;
                response.Success = true;
                
                response.Suggestions = new List<string>();

                _logger.LogInformation("[ChatService] JIFAS introduction displayed with dynamic context");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ChatService] Error showing JIFAS introduction: {ex.Message}");
                response.Message = "Selamat datang di JIFAS AI Assistant! Saya siap membantu Anda dengan pertanyaan seputar Finance, Accounting, Budget, PUM, dan sistem JIFAS lainnya. Apa yang bisa saya bantu hari ini?";
                response.Source = "JIFAS AI Assistant";
                response.IsFromKnowledgeBase = false;
                response.Success = true;
                response.Suggestions = new List<string>();
            }
        }

        /// <summary>
        /// Save chat history to database.
        /// </summary>
        private async Task SaveChatHistoryAsync(
            ChatResponse response,
            string userMessage,
            ChatRequest? request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Extract document id jika response memakai hasil Knowledge Base.
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
                    UsedDocumentIds = documentIds?.Count > 0 ? string.Join(",", documentIds) : string.Empty
                };

                await _chatHistoryService.SaveChatAsync(chatHistory, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ChatService] Failed to save chat history: {ex.Message}");
                // Riwayat chat adalah audit tambahan; kegagalan simpan tidak boleh memutus jawaban user.
            }
        }

        #region Enhanced Response Generation Methods

        /// <summary>
        /// Generate natural greeting response - OPTIMIZED for natural feel
        /// </summary>
        private Task<string> GenerateNaturalGreetingAsync()
        {
            var greetings = new[]
            {
                "Hai! Ada yang bisa saya bantu soal JIFAS?",
                "Halo! Siap membantu pertanyaan seputar JIFAS. Apa yang ingin kamu tahu?",
                "Hi! Saya JIFAS Assistant. Silakan tanya apa saja tentang Invoice, Payment, PUM, atau modul JIFAS lainnya.",
                "Halo! Mau tanya apa tentang JIFAS hari ini?",
                "Hai! Ada pertanyaan tentang sistem JIFAS? Saya siap bantu."
            };

            return Task.FromResult(greetings[Random.Shared.Next(greetings.Length)]);
        }

        /// <summary>
        /// Generate natural gratitude response
        /// </summary>
        private Task<string> GenerateNaturalGratitudeResponseAsync()
        {
            var responses = new[]
            {
                "Sama-sama! Kalau ada pertanyaan lagi, langsung tanya saja.",
                "Senang bisa membantu!",
                "Sip! Hubungi lagi kalau butuh bantuan.",
                "Sama-sama! Semoga lancar ya.",
                "No problem! Tanya lagi kapan saja."
            };

            return Task.FromResult(responses[Random.Shared.Next(responses.Length)]);
        }

        /// <summary>
        /// Generate natural out-of-scope response - OPTIMIZED
        /// </summary>
        private Task<string> GenerateNaturalOutOfScopeResponseAsync(string userQuery)
        {
            var responses = new[]
            {
                $"Maaf, '{TruncateForDisplay(userQuery, 30)}' di luar area saya. Saya fokus bantu soal JIFAS - Invoice, Payment, PUM, Budget, dan Approval. Ada yang mau ditanyakan dari topik itu?",
                $"Hmm, sepertinya itu bukan topik JIFAS. Saya bisa bantu soal Invoice, Payment, PUM, Budget, Receiving, atau Approval. Mau tanya yang mana?",
                $"Itu di luar scope saya. Saya khusus untuk sistem JIFAS - mulai dari Invoice sampai Payment. Ada pertanyaan tentang itu?"
            };

            return Task.FromResult(responses[Random.Shared.Next(responses.Length)]);
        }

        /// <summary>
        /// Generate intelligent partial response when KB has some matches
        /// </summary>
        private async Task<string> GenerateIntelligentPartialResponseAsync(
            string userQuery, 
            List<KnowledgeBaseResult> partialResults,
            IntentType intent,
            string? packedContext = null,
            string? userId = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Use the full AI pipeline with KB results so system prompt knowledge kicks in
                var userMemoryContext = await _userMemory.BuildUserContextForPromptAsync(userId ?? "anonymous");
                var sessionContext = BuildSessionContext(
                    conversationContext: null,
                    isFollowUp: false,
                    activePageContext: packedContext,
                    userMemoryContext: userMemoryContext);

                return await _ollamaService.GenerateResponseAsync(userQuery, partialResults, sessionContext: sessionContext, cancellationToken: cancellationToken);
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
        private async Task<string> GenerateHelpfulNoMatchResponseAsync(
            string userQuery,
            IntentType intent,
            string? packedContext = null,
            string? userId = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Even with no KB results, use GenerateResponseAsync so the rich system prompt
                // can answer from its built-in JIFAS knowledge
                var userMemoryContext = await _userMemory.BuildUserContextForPromptAsync(userId ?? "anonymous");
                var sessionContext = BuildSessionContext(
                    conversationContext: null,
                    isFollowUp: false,
                    activePageContext: packedContext,
                    userMemoryContext: userMemoryContext);

                return await _ollamaService.GenerateResponseAsync(
                    userQuery, 
                    new List<KnowledgeBaseResult>(), 
                    sessionContext: sessionContext,
                    cancellationToken: cancellationToken);
            }
            catch
            {
                return "Maaf, saya tidak menemukan informasi yang relevan untuk pertanyaan Anda. Coba gunakan kata kunci yang berbeda, atau hubungi IT Help Desk di it@jababeka.com untuk bantuan lebih lanjut.";
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
            string? packedContext = null,
            string? userId = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Bangun user memory context (profil user jangka panjang)
                var userMemoryContext = await _userMemory.BuildUserContextForPromptAsync(userId ?? "anonymous");

                // Gabungkan user memory + active page + conversation context
                var sessionContext = BuildSessionContext(conversationContext, isFollowUp, activePageContext, userMemoryContext, packedContext);

                // Gunakan prompt engineering dengan session context yang lengkap
                var enhancedQuery = userQuery;
                if (isFollowUp && !string.IsNullOrEmpty(conversationContext))
                {
                    enhancedQuery = $@"{conversationContext}

PERTANYAAN SAAT INI (follow-up): ""{userQuery}""
Jawab dengan mempertimbangkan konteks percakapan sebelumnya.";
                }

                return await _ollamaService.GenerateResponseAsync(enhancedQuery, kbResults, sessionContext: sessionContext, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ChatService] Error generating enhanced response: {ex.Message}");
                return await _ollamaService.GenerateResponseAsync(userQuery, kbResults, sessionContext: activePageContext, cancellationToken: cancellationToken);
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
            string? userMemoryContext = null,
            string? packedContext = null)
        {
            var parts = new System.Collections.Generic.List<string>();

            if (!string.IsNullOrEmpty(packedContext))
            {
                parts.Add(packedContext);
            }
            else
            {
                if (!string.IsNullOrEmpty(userMemoryContext))
                    parts.Add(userMemoryContext);

                if (!string.IsNullOrEmpty(activePageContext))
                    parts.Add(activePageContext);

                if (isFollowUp && !string.IsNullOrEmpty(conversationContext))
                    parts.Add(conversationContext);
            }

            return string.Join("\n\n", parts);
        }

        /// <summary>
        /// Regenerate response with quality improvements
        /// </summary>
        private async Task<string> RegenerateImprovedResponseAsync(
            string userQuery,
            List<KnowledgeBaseResult> kbResults,
            IntentType intent,
            QualityValidationResult previousQuality,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var issues = string.Join(", ", previousQuality.Issues);
                var kbContext = string.Join("\n\n", kbResults.Take(3).Select(r => 
                    $"[{r.Title}]\n{r.Content}"));

                var prompt = $@"Jawaban sebelumnya untuk pertanyaan ini memiliki masalah: {issues}

Pertanyaan: ""{userQuery}""
Intent: {intent}

Informasi Referensi:
{kbContext}

Buatlah jawaban yang LEBIH BAIK dengan memperhatikan:
1. Jawaban harus SPESIFIK dan RELEVAN dengan pertanyaan
2. Hanya gunakan informasi yang tersedia (no hallucination)
3. Struktur yang jelas (langkah-langkah jika how-to)
4. Natural dan mudah dipahami
5. Minimal 2-3 kalimat untuk konteks yang cukup

Respons langsung:";

                return await _ollamaService.CallOllamaApiAsync(prompt, cancellationToken);
            }
            catch
            {
                // Fallback to original method
                return await _ollamaService.GenerateResponseAsync(userQuery, kbResults, cancellationToken: cancellationToken);
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

        private static string BuildResponseCacheKey(string message, ChatRequest? request)
        {
            var normalizedMessage = NormalizeCacheText(message);
            var language = string.IsNullOrWhiteSpace(request?.Language) ? "id" : request!.Language.Trim().ToLowerInvariant();
            var cacheScope = BuildResponseCacheScope(message, request);

            if (cacheScope == "shared")
            {
                var sharedScope = string.Join("|", new[]
                {
                    "shared",
                    language,
                    normalizedMessage
                });

                return $"Chat_Response_Shared_{HashHelper.ToShortStableHash(sharedScope)}";
            }

            var contextualScope = string.Join("|", new[]
            {
                "contextual",
                normalizedMessage,
                request?.UserId ?? "anonymous",
                request?.UserRole ?? string.Empty,
                request?.UserCompCode ?? string.Empty,
                language,
                request?.Context?.ActiveModule ?? request?.CurrentModule ?? string.Empty,
                request?.Context?.CurrentPage ?? string.Empty,
                request?.Context?.PageTitle ?? string.Empty,
                request?.Context?.SelectedDocumentId ?? string.Empty,
                request?.Context?.DocumentType ?? string.Empty,
                request?.Context?.DocumentStatus ?? string.Empty
            });

            return $"Chat_Response_Contextual_{HashHelper.ToShortStableHash(contextualScope)}";
        }

        private static string BuildResponseCacheScope(string message, ChatRequest? request)
        {
            if (ContainsContextualSignal(message, request))
                return "contextual";

            return "shared";
        }

        private static bool ContainsContextualSignal(string message, ChatRequest? request)
        {
            var normalized = NormalizeCacheText(message);
            var context = request?.Context;
            var hasDocumentContext =
                !string.IsNullOrWhiteSpace(context?.SelectedDocumentId) ||
                !string.IsNullOrWhiteSpace(context?.DocumentType) ||
                !string.IsNullOrWhiteSpace(context?.DocumentStatus);

            if (hasDocumentContext && ContainsAny(normalized, new[]
                {
                    "dokumen", "nomor", "status", "ini", "terpilih", "approve",
                    "approval", "posting", "paid", "bayar", "void", "reverse"
                }))
            {
                return true;
            }

            if ((!string.IsNullOrWhiteSpace(context?.CurrentPage) ||
                 !string.IsNullOrWhiteSpace(context?.ActiveModule) ||
                 !string.IsNullOrWhiteSpace(request?.CurrentModule)) &&
                ContainsAny(normalized, new[]
                {
                    "halaman ini", "page ini", "menu ini", "di sini", "disini",
                    "sedang dibuka", "yang sedang", "current page"
                }))
            {
                return true;
            }

            var contextualKeywords = new[]
            {
                "saya", "aku", "punya saya", "dokumen", "nomor", "no ", "status saya",
                "tiket", "ticket", "jira", "buatkan", "laporkan", "error saya",
                "halaman ini", "page ini", "yang sedang", "invoice saya", "payment saya"
            };

            return ContainsAny(normalized, contextualKeywords);
        }

        private static string NormalizeCacheText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Trim().ToLowerInvariant();
            while (normalized.Contains("  ", StringComparison.Ordinal))
            {
                normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
            }

            return normalized;
        }

        private static bool ContainsAny(string value, IEnumerable<string> keywords)
        {
            return keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}


