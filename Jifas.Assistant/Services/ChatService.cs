using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
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
        private readonly IAssistantCommandService _assistantCommandService;
        private readonly IChatHistoryService _chatHistoryService;
        
        // Service kualitas AI: intent detection, validasi jawaban, memori percakapan, dan context packing.
        private readonly IQueryUnderstandingService _queryUnderstanding;
        private readonly IResponseQualityService _responseQuality;
        private readonly IConversationIntelligenceService _conversationIntelligence;
        private readonly IUserMemoryService _userMemory;
        private readonly IAdaptiveContextPackService _contextPackService;
        private readonly IEmbeddingService _embeddingService;
        private readonly ICrossSessionContextService _crossSessionContext;
        private readonly ITicketService _ticketService;
        private readonly IMonitoringService _monitoringService;
        private readonly IDbContextFactory<JIFAS_AssistantContext> _dbFactory;

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
            IAssistantCommandService assistantCommandService,
            IChatHistoryService chatHistoryService,
            IQueryUnderstandingService queryUnderstanding,
            IResponseQualityService responseQuality,
            IConversationIntelligenceService conversationIntelligence,
            IUserMemoryService userMemory,
            IAdaptiveContextPackService contextPackService,
            IEmbeddingService embeddingService,
            ITicketService ticketService,
            IMonitoringService monitoringService,
            IDbContextFactory<JIFAS_AssistantContext> dbFactory,
            ICrossSessionContextService crossSessionContext)
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
            _assistantCommandService = assistantCommandService ?? throw new ArgumentNullException(nameof(assistantCommandService));
            _chatHistoryService = chatHistoryService ?? throw new ArgumentNullException(nameof(chatHistoryService));
            _queryUnderstanding = queryUnderstanding ?? throw new ArgumentNullException(nameof(queryUnderstanding));
            _responseQuality = responseQuality ?? throw new ArgumentNullException(nameof(responseQuality));
            _conversationIntelligence = conversationIntelligence ?? throw new ArgumentNullException(nameof(conversationIntelligence));
            _userMemory = userMemory ?? throw new ArgumentNullException(nameof(userMemory));
            _contextPackService = contextPackService ?? throw new ArgumentNullException(nameof(contextPackService));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _ticketService = ticketService ?? throw new ArgumentNullException(nameof(ticketService));
            _monitoringService = monitoringService ?? throw new ArgumentNullException(nameof(monitoringService));
            _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
            _crossSessionContext = crossSessionContext ?? throw new ArgumentNullException(nameof(crossSessionContext));
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

            // isFirstMessage: true jika frontend mengirim flag ini (new session/page load).
            // SessionId selalu ada karena controller selalu generate GUID jika kosong.
            // Cross-session: jika isFirstMessage=true, cek apakah user punya prior sessions.
            var isFirstMessage = request?.IsFirstMessage == true;

            // Track apakah response ini greeting/intro — jika ya, skip semua cache KB (shared scope, lintas user).
            var isGreetingResponse = false;

            // Track apakah returning user (buka sesi baru tapi punya history di sesi sebelumnya).
            // Cross-session memory: AI inject konteks dari LastSessionId user.
            var isReturningUser = false;
            CrossSessionContext? priorContext = null;
            if (isFirstMessage)
            {
                var userProfile = await _userMemory.GetUserProfileAsync(request?.UserId ?? "anonymous");
                if (!userProfile.IsNewUser && userProfile.TotalSessions > 0)
                {
                    priorContext = await _crossSessionContext.GetPriorSessionContextAsync(
                        request?.UserId ?? "anonymous", maxTurns: 3);
                    isReturningUser = priorContext?.HasPriorSession == true;
                    if (isReturningUser)
                        _logger.LogInformation($"[ChatService] Returning user: {request?.UserId}, Sessions: {userProfile.TotalSessions}, PriorTopics: {string.Join(", ", priorContext?.TopicsFromPriorSession ?? new List<string>())}");
                }
            }

            // Log context dari frontend untuk membantu analisis issue company/module/page.
            _logger.LogDebug(
                $"[ChatService] Processing message — UserId: {request?.UserId}, " +
                $"IsFirstMessage: {isFirstMessage} (or request field: {request?.IsFirstMessage}), " +
                $"UserRole: {request?.UserRole}, " +
                $"UserCompCode: {request?.UserCompCode}, " +
                $"ActiveModule: {request?.Context?.ActiveModule}");

            var jifasIntroductionKey = $"JIFAS_Intro_{response.SessionId}";
            var hasSeenIntroduction = _cacheService.Get<bool>(jifasIntroductionKey);

            // Cek apakah sesi ini sudah punya chat history. Jika ya, skip greeting/intro.
            var hasSessionHistory = false;
            if (!isFirstMessage && !string.IsNullOrWhiteSpace(response.SessionId))
            {
                var sessionHistory = await _chatHistoryService.GetSessionHistoryAsync(response.SessionId, request?.UserId, 1, cancellationToken);
                hasSessionHistory = sessionHistory.Count > 0;
            }

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
                metrics.Route = "validation";
                response.PerformanceMetrics = metrics;
                
                // Simpan riwayat agar request invalid tetap bisa diaudit.
                await SaveChatHistoryAsync(response, validationResult.ErrorMessage ?? "", request, cancellationToken);
                
                return response;
            }

            var validatedRequest = validationResult.Value;
            var userMessage = validatedRequest.Message;

            try
            {
                // Command slash seperti /help ditangani cepat tanpa cache, KB, atau LLM.
                var commandResponse = _assistantCommandService.TryHandleCommand(
                    userMessage,
                    validatedRequest,
                    response.SessionId,
                    correlationId);

                if (commandResponse != null)
                {
                    totalStopwatch.Stop();
                    metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                    metrics.Route = "command";
                    commandResponse.PerformanceMetrics = metrics;
                    await SaveChatHistoryAsync(commandResponse, userMessage, request, cancellationToken);
                    return commandResponse;
                }

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
                    metrics.Route = "ticket";
                    response.PerformanceMetrics = metrics;
                    await SaveChatHistoryAsync(response, userMessage, request, cancellationToken);
                    return response;
                }

                var enableCache = _configuration.GetValue<bool>("Caching:EnableResponseCache");

                // Pesan pertama sesi menampilkan intro JIFAS satu kali per session.
                // Returning user: personalized greeting + cross-session context injection.
                if (isFirstMessage && !hasSeenIntroduction && !hasSessionHistory)
                {
                    if (isReturningUser && priorContext?.HasPriorSession == true)
                    {
                        _logger.LogInformation($"[ChatService] Returning user detected - showing personalized return greeting");
                        var returnGreeting = await _crossSessionContext.GenerateReturnGreetingAsync(
                            priorContext, request?.Language ?? "id");
                        response.Message = returnGreeting;
                        response.Source = "JIFAS AI Assistant - Welcome Back";
                        metrics.Route = "return-greeting";
                    }
                    else
                    {
                        _logger.LogInformation($"[ChatService] New session detected - showing JIFAS introduction");
                        await ShowJIFASIntroductionAsync(response, cancellationToken);
                        metrics.Route = "intro";
                    }

                    _cacheService.Set(jifasIntroductionKey, true, 24 * 60);
                    isGreetingResponse = true;

                    totalStopwatch.Stop();
                    metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                    response.PerformanceMetrics = metrics;
                    _logger.LogInformation($"[{(isReturningUser ? "RETURN_GREETING" : "INTRO")}] {metrics.GetSummary()}");

                    await SaveChatHistoryAsync(response, "[Session Greeting]", request, cancellationToken);

                    // Update LastSessionId untuk cross-session memory
                    if (!string.IsNullOrWhiteSpace(request?.UserId) && request.UserId != "anonymous")
                        await _crossSessionContext.UpdateLastSessionAsync(request.UserId, response.SessionId);

                    return response;
                }

                var learningDecision = await ResolveAiLearningDecisionAsync(userMessage, cancellationToken);
                var cacheKnowledgeVersion = learningDecision.KnowledgeVersion;

                if (learningDecision.HasMatch)
                {
                    var learningRoute = learningDecision.MatchType == "exact" ? "learning-exact" : "learning-similar";
                    if (enableCache && !isGreetingResponse)
                    {
                        var learningCacheStopwatch = Stopwatch.StartNew();
                        var cacheScope = BuildResponseCacheScope(userMessage, request);
                        metrics.CacheScope = cacheScope;
                        metrics.KnowledgeVersion = cacheKnowledgeVersion;
                        var cacheKey = BuildResponseCacheKey(userMessage, request, cacheKnowledgeVersion);
                        var cachedResponse = _cacheService.Get<ChatResponse>(cacheKey);
                        learningCacheStopwatch.Stop();
                        metrics.CacheLookupMs = learningCacheStopwatch.ElapsedMilliseconds;

                        if (cachedResponse != null)
                        {
                            cachedResponse.Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                            cachedResponse.SessionId = response.SessionId;
                            cachedResponse.CorrelationId = correlationId;
                            cachedResponse.Source = "Cache";
                            cachedResponse.PerformanceMetrics ??= new PerformanceMetrics();
                            cachedResponse.PerformanceMetrics.WasCacheLit = true;
                            cachedResponse.PerformanceMetrics.CacheScope = cacheScope;
                            cachedResponse.PerformanceMetrics.Route = "cache";
                            cachedResponse.PerformanceMetrics.KnowledgeVersion = cacheKnowledgeVersion;
                            cachedResponse.PerformanceMetrics.LearningMatchType = learningDecision.MatchType;
                            cachedResponse.PerformanceMetrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                            await RecordCacheHitAsync(cachedResponse, request, userMessage, totalStopwatch.ElapsedMilliseconds);
                            _logger.LogInformationWithCorrelation(correlationId, $"[CACHE_AFTER_LEARNING] {cachedResponse.PerformanceMetrics.GetSummary()}");
                            return cachedResponse;
                        }
                    }

                    var learningStopwatch = Stopwatch.StartNew();
                    var shouldFormat = !IsAnswerStructuredEnough(learningDecision.Answer);

                    response.Message = shouldFormat
                        ? await FormatLearningAnswerAsync(userMessage, learningDecision, request, cancellationToken)
                        : learningDecision.Answer;
                    learningStopwatch.Stop();

                    response.Source = learningDecision.MatchType == "exact"
                        ? "AI Learning Exact"
                        : "AI Learning Similar";
                    response.IsFromKnowledgeBase = true;
                    response.ConfidenceScore = CalculateDynamicConfidence(learningDecision.MatchType, learningDecision.MatchScore);
                    response.Success = true;
                    response.Suggestions = new List<string>();
                    response.KnowledgeBaseResults = learningDecision.Results;
                    metrics.LlmResponseMs = 0;
                    metrics.LearningFormatterMs = shouldFormat ? learningStopwatch.ElapsedMilliseconds : 0;
                    metrics.SuggestionsMs = 0;
                    metrics.SuggestionsCached = false;
                    metrics.Route = learningRoute;
                    metrics.LearningMatchType = learningDecision.MatchType;
                    metrics.KnowledgeVersion = cacheKnowledgeVersion;

                    var authoritativeCacheStopwatch = Stopwatch.StartNew();
                    // Only cache authoritative learning responses (grounded in prior answers).
                    if (enableCache && response.IsFromKnowledgeBase)
                    {
                        metrics.CacheScope = BuildResponseCacheScope(userMessage, request);
                        var cacheKey = BuildResponseCacheKey(userMessage, request, cacheKnowledgeVersion);
                        var cacheDuration = _configuration.GetValue<int>("Caching:ResponseCacheDurationHours", 24);
                        _cacheService.Set(cacheKey, response, cacheDuration * 60);
                    }
                    authoritativeCacheStopwatch.Stop();
                    metrics.CachingMs = authoritativeCacheStopwatch.ElapsedMilliseconds;

                    totalStopwatch.Stop();
                    metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                    response.PerformanceMetrics = metrics;
                    response.CorrelationId = correlationId;

                    await RecordNonLlmRouteAsync(response, request, userMessage, learningRoute, metrics.TotalMs);
                    _logger.LogInformationWithCorrelation(correlationId, $"[AI_LEARNING_{learningDecision.MatchType.ToUpperInvariant()}] {metrics.GetSummary()}");
                    await SaveChatHistoryAsync(response, userMessage, request, cancellationToken);
                    return response;
                }

                cacheKnowledgeVersion = await GetKnowledgeVersionAsync(cancellationToken);

                var cacheStopwatch = Stopwatch.StartNew();
                if (enableCache && !string.IsNullOrWhiteSpace(userMessage))
                {
                    var cacheScope = BuildResponseCacheScope(userMessage, request);
                    metrics.CacheScope = cacheScope;
                    metrics.KnowledgeVersion = cacheKnowledgeVersion;
                    var cacheKey = BuildResponseCacheKey(userMessage, request, cacheKnowledgeVersion);
                    var cachedResponse = _cacheService.Get<ChatResponse>(cacheKey);
                    cacheStopwatch.Stop();
                    metrics.CacheLookupMs = cacheStopwatch.ElapsedMilliseconds;

                    if (cachedResponse != null)
                    {
                        cachedResponse.Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                        cachedResponse.SessionId = response.SessionId;
                        cachedResponse.CorrelationId = correlationId;
                        cachedResponse.Source = "Cache";
                        cachedResponse.PerformanceMetrics ??= new PerformanceMetrics();
                        cachedResponse.PerformanceMetrics.WasCacheLit = true;
                        cachedResponse.PerformanceMetrics.CacheScope = cacheScope;
                        cachedResponse.PerformanceMetrics.Route = "cache";
                        cachedResponse.PerformanceMetrics.KnowledgeVersion = cacheKnowledgeVersion;
                        cachedResponse.PerformanceMetrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                        await RecordCacheHitAsync(cachedResponse, request, userMessage, totalStopwatch.ElapsedMilliseconds);
                        _logger.LogInformation($"[ChatService] Response cache HIT after learning check - Total: {cachedResponse.PerformanceMetrics.TotalMs}ms | {cachedResponse.PerformanceMetrics.GetSummary()}");
                        return cachedResponse;
                    }
                }
                else
                {
                    cacheStopwatch.Stop();
                    metrics.CacheLookupMs = cacheStopwatch.ElapsedMilliseconds;
                }

                // ===== SCOPE PRE-GATE =====
                // High-precision, low-recall hard-block SEBELUM pipeline mahal.
                // - ESCAPE: activeModule atau istilah JIFAS -> teruskan ke LLM.
                // - HARD-BLOCK: topik OOS jelas tanpa sinyal JIFAS -> return OOS, skip Ollama.
                // - PASS THROUGH: sisanya (ambigu/nuansa) -> lanjut pipeline normal.
                // OOS hard-block TIDAK di-cache (konsisten dengan gate cache existing).
                var scopeStopwatch = Stopwatch.StartNew();
                var preGateResult = _outOfScopeDetector.EvaluatePreGate(userMessage, request);
                scopeStopwatch.Stop();
                metrics.ScopeDetectionMs = scopeStopwatch.ElapsedMilliseconds;

                if (preGateResult.IsHardBlocked)
                {
                    _logger.LogInformation("[ScopePreGate] Hard-block: {0} — skipping Ollama", preGateResult.Reason);
                    var oosMessage = $"Maaf, pertanyaan tentang \"{userMessage}\" di luar cakupan JIFAS AI Assistant. " +
                        "Saya dirancang khusus untuk membantu hal-hal terkait sistem JIFAS — " +
                        "Invoice, Payment, PUM, Receiving, Budget, Accounting (GL/AP/AR), Laporan Keuangan, dan proses approval. " +
                        "Ada yang ingin Anda tanyakan tentang JIFAS?";
                    response.Message = oosMessage;
                    response.Success = true;
                    response.IsFromKnowledgeBase = false;
                    response.Source = "OutOfScope PreGate";
                    response.ConfidenceScore = 1.0;
                    response.Suggestions = new List<string>();
                    response.KnowledgeBaseResults = new List<KnowledgeBaseResult>();
                    metrics.Route = "oos-pregate";
                    metrics.LlmResponseMs = 0;

                    totalStopwatch.Stop();
                    metrics.TotalMs = totalStopwatch.ElapsedMilliseconds;
                    response.PerformanceMetrics = metrics;
                    response.CorrelationId = correlationId;

                    _logger.LogInformationWithCorrelation(correlationId, $"[OOS_PREGATE] {metrics.GetSummary()}");
                    await SaveChatHistoryAsync(response, userMessage, request, cancellationToken);
                    return response;
                }

                // ===== NEW PIPELINE: Context FIRST, then AI-driven intent classification =====
                // Bug fix #1: Build conversation history BEFORE any scope/intent decisions.
                // Previously context was built AFTER scope check, causing false OOS rejections.
                // Bug fix #3: Scope is now determined by AI intent classifier with conversation context,
                // not by hardcoded keyword arrays in OutOfScopeDetector.

                // Build full conversation context upfront — needed for ALL downstream decisions.
                // maxTurns=15: sliding window untuk single-pass pipeline (fix: was 5, now 15).
                var fullContext = await _conversationIntelligence.BuildContextAsync(response.SessionId, request?.UserId, maxTurns: 15);

                // Build conversation turns for the AI intent classifier.
                var conversationHistory = new List<(string UserMessage, string AssistantResponse)>();
                if (fullContext?.RecentTurns != null)
                {
                    conversationHistory = fullContext.RecentTurns
                        .Select(t => (t.UserMessage, t.AssistantResponse))
                        .ToList();
                }

                // === SINGLE-PASS CONVERSATIONAL PIPELINE ===
                // No intent classifier -- Ollama handles all routing internally.
                // Context (history list) built above. Cross-session handled in the code below.

                // Build active page context for RAG grounding
                var activePageContext = BuildActivePageContextFromRequest(request);

                // Step 3: cari Knowledge Base untuk grounding (tanpa routing chain)
                var kbSearchStopwatch = Stopwatch.StartNew();

                float[]? queryEmbedding = null;
                try
                {
                    queryEmbedding = await _embeddingService.GenerateEmbeddingAsFloatArrayAsync(userMessage, cancellationToken);
                }
                catch (Exception embEx)
                {
                    _logger.LogWarning($"[ChatService] Embedding generation failed: {embEx.Message}");
                }

                var kbResults = await _knowledgeBaseService.SearchWithEmbeddingAsync(userMessage, queryEmbedding, topK: 5, cancellationToken: cancellationToken);
                kbSearchStopwatch.Stop();
                metrics.KbSearchMs = kbSearchStopwatch.ElapsedMilliseconds;
                metrics.KbResultsBeforeValidation = kbResults?.Count ?? 0;

                // Validate KB results (lightweight quality filter)
                var validatedResults = ValidateKBResults(kbResults ?? new List<KnowledgeBaseResult>());
                metrics.KbResultsAfterValidation = validatedResults.Count;
                metrics.AverageKbScore = validatedResults.Count > 0 ? validatedResults.Average(r => r.Score) : 0;

                _logger.LogInformation($"[ChatService] KB Search ({metrics.KbSearchMs}ms): {validatedResults.Count} valid results");

                // Step 4: SINGLE CALL — everything bundled: history + RAG + scope + format rules
                // This replaces the old 4-5 call pipeline: intent-classify → route → meta-clarify-generate → enhance
                var responseStopwatch = Stopwatch.StartNew();

                var aiResponse = await _ollamaService.GenerateConversationalResponseAsync(
                    userMessage,
                    validatedResults,
                    conversationHistory,
                    activePageContext,
                    request?.UserId,
                    fullContext?.RunningSummary,
                    cancellationToken);
                responseStopwatch.Stop();
                metrics.LlmResponseMs = (long)responseStopwatch.Elapsed.TotalMilliseconds;
                metrics.Route = validatedResults.Count > 0 ? "kb-rag-conversational" : "system-knowledge-conversational";

                response.Message = aiResponse;
                response.Source = validatedResults.Count > 0 ? "Knowledge Base RAG" : "JIFAS System Knowledge";
                response.IsFromKnowledgeBase = validatedResults.Count > 0;
                response.ConfidenceScore = validatedResults.Count > 0 && validatedResults.Count > 0
                    ? Math.Min(0.95, 0.7 + (validatedResults.Average(r => r.Score) * 0.25))
                    : 0.5;
                response.KnowledgeBaseResults = validatedResults;
                response.Success = true;
                response.Suggestions = new List<string>();

                // Step 8: simpan response final ke Redis — KB-grounded responses only.
                // OOS, system-knowledge-only, and error responses are NOT cached
                // to prevent polluting cache with ungrounded answers.
                var cacheStopwatch2 = Stopwatch.StartNew();
                if (enableCache && response.Success && response.IsFromKnowledgeBase)
                {
                    metrics.CacheScope = BuildResponseCacheScope(userMessage, request);
                    var cacheKey = BuildResponseCacheKey(userMessage, request, cacheKnowledgeVersion);
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
                // intentResult and confidenceScore computed in the simplified single-pass pipeline above.
                await _userMemory.UpdateMemoryAsync(
                    request?.UserId ?? "anonymous",
                    userMessage,
                    aiResponse,
                    IntentType.General,
                    response.ConfidenceScore,
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

                // Update LastSessionId untuk cross-session memory tracking
                if (!string.IsNullOrWhiteSpace(request?.UserId) && request.UserId != "anonymous")
                {
                    await _crossSessionContext.UpdateLastSessionAsync(request.UserId, response.SessionId);
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

        private async Task RecordCacheHitAsync(ChatResponse cachedResponse, ChatRequest? request, string userMessage, long totalMs)
        {
            try
            {
                await _monitoringService.RecordAsync(new AiCallMetrics
                {
                    UserId = request?.UserId ?? "anonymous",
                    SessionId = cachedResponse.SessionId,
                    ActiveModule = request?.Context?.ActiveModule ?? request?.CurrentModule ?? "Unknown",
                    Model = _configuration["Ollama:Model"] ?? "cache",
                    CallType = "cache-hit",
                    PromptTokens = EstimateTokenCount(userMessage),
                    CompletionTokens = EstimateTokenCount(cachedResponse.Message),
                    TotalDurationMs = Math.Max(0, totalMs),
                    LoadDurationMs = 0,
                    PromptEvalDurationMs = 0,
                    EvalDurationMs = 0,
                    PromptLengthChars = userMessage?.Length ?? 0,
                    ResponseLengthChars = cachedResponse.Message?.Length ?? 0,
                    ConfidenceScore = cachedResponse.ConfidenceScore,
                    IsError = !cachedResponse.Success,
                    ErrorMessage = cachedResponse.Success ? null : "Cached response marked unsuccessful",
                    CreatedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ChatService] Cache-hit monitoring skipped: {ex.Message}");
            }
        }

        private async Task RecordNonLlmRouteAsync(ChatResponse response, ChatRequest? request, string userMessage, string route, long totalMs)
        {
            try
            {
                await _monitoringService.RecordAsync(new AiCallMetrics
                {
                    UserId = request?.UserId,
                    SessionId = request?.SessionId ?? response.SessionId,
                    ActiveModule = request?.Context?.ActiveModule ?? request?.CurrentModule,
                    Model = "jifas-routing",
                    CallType = route,
                    PromptTokens = EstimateTokenCount(userMessage),
                    CompletionTokens = EstimateTokenCount(response.Message),
                    TotalDurationMs = Math.Max(0, totalMs),
                    LoadDurationMs = 0,
                    PromptEvalDurationMs = 0,
                    EvalDurationMs = 0,
                    PromptLengthChars = userMessage?.Length ?? 0,
                    ResponseLengthChars = response.Message?.Length ?? 0,
                    ConfidenceScore = response.ConfidenceScore,
                    IsError = !response.Success,
                    ErrorMessage = response.Success ? null : $"Route {route} response marked unsuccessful",
                    CreatedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ChatService] Route monitoring skipped for {route}: {ex.Message}");
            }
        }

        private static int EstimateTokenCount(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
        }

        private async Task<string> FormatLearningAnswerAsync(
            string userMessage,
            LearningDecision decision,
            ChatRequest? request,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ollamaService.SetCallContext(
                    request?.UserId,
                    request?.SessionId,
                    request?.Context?.ActiveModule ?? request?.CurrentModule,
                    "learning-formatter");

                var prompt = $@"Rapikan jawaban resmi admin untuk chatbot JIFAS.

ATURAN WAJIB:
1. Gunakan HANYA informasi dari JAWABAN RESMI ADMIN.
2. Jangan menambah fakta baru dari knowledge base lain.
3. Jangan membuat contoh baru yang tidak ada di jawaban resmi.
4. Pertahankan istilah penting apa adanya, termasuk Divisi Tax, Payment, Invoice, PUM, Finance, dan manajemen.
5. Bahasa Indonesia natural, rapi, mudah dibaca user bisnis.
6. Jika jawaban resmi sudah jelas, cukup rapikan struktur tanpa mengubah makna.

PERTANYAAN USER:
{userMessage}

JAWABAN RESMI ADMIN:
{decision.Answer}

Berikan jawaban final saja tanpa penjelasan tambahan.";

                var timeoutSeconds = Math.Clamp(
                    _configuration.GetValue<int?>("AiLearning:FormatterTimeoutSeconds") ?? 25,
                    5,
                    60);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                var formatted = await _ollamaService.CallOllamaApiAsync(prompt, timeoutCts.Token);
                return string.IsNullOrWhiteSpace(formatted) ? decision.Answer : formatted.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ChatService] Learning formatter fallback to admin answer: {ex.Message}");
                return decision.Answer;
            }
        }

        private static bool IsAnswerStructuredEnough(string answer)
        {
            if (string.IsNullOrWhiteSpace(answer))
                return false;

            var hasParagraphs = answer.Contains("\n\n", StringComparison.Ordinal);
            var hasList = answer.Contains("\n1.", StringComparison.Ordinal) ||
                answer.Contains("\n- ", StringComparison.Ordinal) ||
                answer.Contains("\n* ", StringComparison.Ordinal);
            var longEnough = answer.Length >= 220;
            return longEnough && (hasParagraphs || hasList);
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

        private async Task<LearningDecision> ResolveAiLearningDecisionAsync(
            string userMessage,
            CancellationToken cancellationToken)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

            // Exact match: cek hash tag user message, plus semua learning-hash tags di dokumen.
            // Ini menangani kasus admin edit pertanyaan - document punya 2 hash tags.
            var userHash = AiLearningPolicy.BuildQuestionHash(userMessage);
            var userHashTag = $"learning-hash:{userHash[..Math.Min(16, userHash.Length)]}";

            // Ambil dokumen AI Learning yang punya tag matching, atau punya ai-learning tag.
            var candidateDocs = await db.KnowledgeBaseDocuments
                .AsNoTracking()
                .Where(d => d.IsActive == true && d.Tags != null &&
                    (d.Tags.Contains(userHashTag) || d.Tags.Contains("ai-learning")))
                .Select(d => new LearningDocumentProjection
                {
                    Id = d.Id,
                    Title = d.Title ?? string.Empty,
                    Content = d.Content ?? string.Empty,
                    Tags = d.Tags ?? string.Empty,
                    Category = d.Category ?? string.Empty,
                    UpdatedAt = d.UpdatedAt
                })
                .ToListAsync(cancellationToken);

            // Cek exact match: dokumen dengan user hash tag.
            var exactMatchDoc = candidateDocs.FirstOrDefault(d =>
                d.Tags.Contains(userHashTag) &&
                IsAiLearningDocument(d.Title, d.Tags, d.Content));

            if (exactMatchDoc != null)
            {
                var officialQuestion = ExtractSection(exactMatchDoc.Content, "Pertanyaan:", "Jawaban resmi:");
                if (IsStrongQuestionMatch(userMessage, officialQuestion))
                {
                    var directAnswer = ExtractSection(exactMatchDoc.Content, "Jawaban resmi:", "Kategori:");
                    if (!string.IsNullOrWhiteSpace(directAnswer) && directAnswer.Length >= 40)
                    {
                        var matchScore = CalculateQuestionSimilarity(userMessage, officialQuestion);
                        _logger.LogDebug($"[AI_Learning] Exact match found: doc={exactMatchDoc.Id}, score={matchScore:F3}");
                        return BuildLearningDecision(exactMatchDoc, directAnswer, "exact", matchScore);
                    }
                }
            }

            // Similar match: cari dari semua AI Learning documents.
            // Prioritas: dokumen dengan learning hash tag, lalu berdasarkan similarity score.
            var aiLearningDocs = candidateDocs
                .Where(d => IsAiLearningDocument(d.Title, d.Tags, d.Content) && !d.Tags.Contains(userHashTag))
                .ToList();

            // Jika candidateDocs kosong atau tidak ada AI Learning docs, load semua.
            if (aiLearningDocs.Count == 0)
            {
                aiLearningDocs = await db.KnowledgeBaseDocuments
                    .AsNoTracking()
                    .Where(d => d.IsActive == true &&
                        ((d.Tags != null && d.Tags.Contains("ai-learning")) ||
                         (d.Title != null && d.Title.StartsWith("AI Learning -"))))
                    .OrderByDescending(d => d.UpdatedAt)
                    .Take(200)
                    .Select(d => new LearningDocumentProjection
                    {
                        Id = d.Id,
                        Title = d.Title ?? string.Empty,
                        Content = d.Content ?? string.Empty,
                        Tags = d.Tags ?? string.Empty,
                        Category = d.Category ?? string.Empty,
                        UpdatedAt = d.UpdatedAt
                    })
                    .ToListAsync(cancellationToken);
            }

            var best = aiLearningDocs
                .Select(d => new
                {
                    Document = d,
                    Question = ExtractSection(d.Content, "Pertanyaan:", "Jawaban resmi:"),
                    Answer = ExtractSection(d.Content, "Jawaban resmi:", "Kategori:")
                })
                .Select(x => new
                {
                    x.Document,
                    x.Question,
                    x.Answer,
                    Score = CalculateQuestionSimilarityImproved(userMessage, x.Question)
                })
                .Where(x => x.Score >= 0.75 && !string.IsNullOrWhiteSpace(x.Answer) && x.Answer.Length >= 40)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Document.UpdatedAt)
                .FirstOrDefault();

            if (best != null)
            {
                _logger.LogDebug($"[AI_Learning] Similar match found: doc={best.Document.Id}, score={best.Score:F3}");
                return BuildLearningDecision(best.Document, best.Answer, "similar", best.Score);
            }

            return LearningDecision.None;
        }

        private static bool IsAiLearningResult(KnowledgeBaseResult result) =>
            IsAiLearningDocument(result.Title, string.Empty, result.Content);

        private static LearningDecision BuildLearningDecision(
            LearningDocumentProjection document,
            string answer,
            string matchType,
            double score)
        {
            return new LearningDecision
            {
                HasMatch = true,
                MatchType = matchType,
                Answer = answer.Trim(),
                KnowledgeVersion = BuildKnowledgeVersion(document.Id, document.UpdatedAt),
                MatchScore = score,
                Results = new List<KnowledgeBaseResult>
                {
                    new()
                    {
                        DocumentId = document.Id,
                        Title = document.Title,
                        Content = document.Content,
                        Category = string.IsNullOrWhiteSpace(document.Category) ? "AI Learning" : document.Category,
                        Score = score,
                        IsOfficial = true,
                        UpdatedDate = document.UpdatedAt
                    }
                }
            };
        }

        private static string BuildKnowledgeVersion(int documentId, DateTime? updatedAt)
        {
            var ticks = (updatedAt ?? DateTime.MinValue).ToUniversalTime().Ticks;
            return HashHelper.ToShortStableHash($"learning|{documentId}|{ticks}");
        }

        private async Task<string> GetKnowledgeVersionAsync(CancellationToken cancellationToken)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
                var latest = await db.KnowledgeBaseDocuments
                    .AsNoTracking()
                    .Where(d => d.IsActive == true)
                    .MaxAsync(d => (DateTime?)d.UpdatedAt, cancellationToken);

                return HashHelper.ToShortStableHash($"kb|{latest?.ToUniversalTime().Ticks ?? 0}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ChatService] Knowledge version fallback: {ex.Message}");
                return "kb-unknown";
            }
        }

        private static double CalculateQuestionSimilarityImproved(string userMessage, string officialQuestion)
        {
            // Improved similarity dengan handling untuk:
            // 1. Question word variations (apa/fungsi, fungsi/apa)
            // 2. Word order independence
            // 3. Common JIFAS terms preservation

            var userTerms = GetMeaningfulTerms(userMessage);
            var officialTerms = GetMeaningfulTerms(officialQuestion);
            if (userTerms.Count == 0 || officialTerms.Count == 0)
                return 0;

            // Hitung intersection dan union
            var intersection = userTerms.Intersect(officialTerms, StringComparer.OrdinalIgnoreCase).Count();
            var minBase = Math.Max(1, Math.Min(userTerms.Count, officialTerms.Count));
            var containment = intersection / (double)minBase;

            var union = userTerms.Union(officialTerms, StringComparer.OrdinalIgnoreCase).Count();
            var jaccard = intersection / (double)Math.Max(1, union);

            // Bonus untuk exact match setelah normalize
            var userNormalized = AiLearningPolicy.NormalizeQuestion(userMessage);
            var officialNormalized = AiLearningPolicy.NormalizeQuestion(officialQuestion);
            var exactBonus = userNormalized.Equals(officialNormalized, StringComparison.OrdinalIgnoreCase) ? 0.15 : 0;

            // Bonus untuk substring match (question variations)
            var substringBonus = 0.0;
            if (userNormalized.Length >= 8 && officialNormalized.Length >= 8)
            {
                if (userNormalized.Contains(officialNormalized) || officialNormalized.Contains(userNormalized))
                    substringBonus = 0.08;
            }

            // Bonus untuk JIFAS terms overlap (modul, invoice, payment, dll)
            var jifasTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "invoice", "payment", "pum", "budget", "approval", "report",
                "cashbank", "receiving", "vendor", "coa", "journal", "tax", "pajak"
            };
            var userJifas = userTerms.Intersect(jifasTerms).Count();
            var officialJifas = officialTerms.Intersect(jifasTerms).Count();
            var jifasBonus = (userJifas > 0 && officialJifas > 0) ? 0.05 : 0;

            var baseScore = (containment * 0.6) + (jaccard * 0.2);
            var totalScore = Math.Min(1.0, baseScore + exactBonus + substringBonus + jifasBonus);

            return Math.Round(totalScore, 3);
        }

        private static double CalculateQuestionSimilarity(string userMessage, string officialQuestion)
        {
            var userTerms = GetMeaningfulTerms(userMessage);
            var officialTerms = GetMeaningfulTerms(officialQuestion);
            if (userTerms.Count == 0 || officialTerms.Count == 0)
                return 0;

            var intersection = userTerms.Intersect(officialTerms, StringComparer.OrdinalIgnoreCase).Count();
            var minBase = Math.Max(1, Math.Min(userTerms.Count, officialTerms.Count));
            var containment = intersection / (double)minBase;

            var union = userTerms.Union(officialTerms, StringComparer.OrdinalIgnoreCase).Count();
            var jaccard = intersection / (double)Math.Max(1, union);

            return Math.Round((containment * 0.75) + (jaccard * 0.25), 3);
        }

        private static HashSet<string> GetMeaningfulTerms(string text)
        {
            // Minimal stopwords untuk question similarity - pertahankan terms penting
            var stopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "yang", "dan", "atau", "untuk", "dengan", "jelaskan", "tolong",
                "mau", "saya", "ingin", "tahu", "tentang", "modul", "jifas"
            };

            return AiLearningPolicy.NormalizeQuestion(text)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizeLearningTerm)
                .Where(t => t.Length > 2 && !stopwords.Contains(t))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static string NormalizeLearningTerm(string term)
        {
            return term.ToLowerInvariant() switch
            {
                "guna" or "gunanya" or "kegunaan" or "manfaat" or "peran" => "fungsi",
                "bayar" or "pembayaran" => "payment",
                "persetujuan" or "menyetujui" => "approval",
                "setujui" or "approve" => "approval",
                "halaman" or "page" => "menu",
                _ => term
            };
        }

        /// <summary>
        /// Hitung confidence dinamis berdasarkan match type dan match score.
        /// Exact match: base 0.95 + bonus dari match score.
        /// Similar match: base 0.88 + bonus dari match score.
        /// </summary>
        private static double CalculateDynamicConfidence(string matchType, double matchScore)
        {
            var baseConfidence = matchType == "exact" ? 0.95 : 0.88;
            var scoreBonus = (matchScore - 0.75) * 0.15; // Bonus 0-0.0375 berdasarkan score
            return Math.Round(Math.Min(0.99, Math.Max(baseConfidence, baseConfidence + scoreBonus)), 3);
        }

        private static bool IsAiLearningDocument(string? title, string? tags, string? content)
        {
            return (title ?? string.Empty).StartsWith("AI Learning -", StringComparison.OrdinalIgnoreCase) ||
                (tags ?? string.Empty).Contains("ai-learning", StringComparison.OrdinalIgnoreCase) ||
                (content ?? string.Empty).Contains("JIFAS AI Learning Knowledge", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStrongQuestionMatch(string userMessage, string officialQuestion)
        {
            if (CalculateQuestionSimilarity(userMessage, officialQuestion) >= 0.78)
                return true;

            var user = AiLearningPolicy.NormalizeQuestion(userMessage);
            var official = AiLearningPolicy.NormalizeQuestion(officialQuestion);
            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(official))
                return false;

            if (user.Equals(official, StringComparison.OrdinalIgnoreCase))
                return true;

            if (user.Length >= 12 && official.Length >= 12 &&
                (user.Contains(official, StringComparison.OrdinalIgnoreCase) ||
                 official.Contains(user, StringComparison.OrdinalIgnoreCase)))
                return true;

            var userWords = user.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var officialWords = official.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var overlapWords = userWords
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            overlapWords.IntersectWith(officialWords);

            var overlap = overlapWords.Count / (double)Math.Max(1, Math.Min(userWords.Length, officialWords.Length));
            return overlap >= 0.85;
        }

        private static string ExtractSection(string content, string startMarker, string endMarker)
        {
            if (string.IsNullOrWhiteSpace(content))
                return string.Empty;

            var startIndex = content.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
            if (startIndex < 0)
                return string.Empty;

            startIndex += startMarker.Length;
            var endIndex = content.IndexOf(endMarker, startIndex, StringComparison.OrdinalIgnoreCase);
            if (endIndex < 0)
                endIndex = content.Length;

            return content[startIndex..endIndex].Trim();
        }

        private sealed class LearningDocumentProjection
        {
            public int Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
            public string Tags { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public DateTime? UpdatedAt { get; set; }
        }

        private sealed class LearningDecision
        {
            public static LearningDecision None { get; } = new();
            public bool HasMatch { get; set; }
            public string MatchType { get; set; } = string.Empty;
            public string Answer { get; set; } = string.Empty;
            public string KnowledgeVersion { get; set; } = string.Empty;
            public double MatchScore { get; set; }
            public List<KnowledgeBaseResult> Results { get; set; } = new();
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

                // Bug fix #1: Invalidate conversation context cache AFTER saving.
                // Previously, the cache was NOT invalidated, causing stale context for ~30 minutes.
                // Every new message invalidates the cached context so the next request gets fresh data.
                try
                {
                    var safeUserId = string.IsNullOrWhiteSpace(request?.UserId) ? "anon" : request!.UserId;
                    var contextCacheKey = $"ConversationContext_{safeUserId}_{response.SessionId}";
                    _cacheService.Remove(contextCacheKey);
                    _logger.LogDebug($"[ChatService] Invalidated conversation context cache for session {response.SessionId} (userId={safeUserId})");
                }
                catch (Exception cacheEx)
                {
                    _logger.LogWarning($"[ChatService] Failed to invalidate context cache: {cacheEx.Message}");
                    // Cache failure tidak boleh mengganggu penyimpanan chat history.
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ChatService] Failed to save chat history: {ex.Message}");
                // Riwayat chat adalah audit tambahan; kegagalan simpan tidak boleh memutus jawaban user.
            }
        }


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

        private static string BuildResponseCacheKey(string message, ChatRequest? request, string knowledgeVersion = "")
        {
            var normalizedMessage = NormalizeCacheText(message);
            var language = string.IsNullOrWhiteSpace(request?.Language) ? "id" : request!.Language.Trim().ToLowerInvariant();
            var cacheScope = BuildResponseCacheScope(message, request);
            var version = string.IsNullOrWhiteSpace(knowledgeVersion) ? "no-version" : knowledgeVersion.Trim();

            if (cacheScope == "shared")
            {
                var sharedScope = string.Join("|", new[]
                {
                    "shared",
                    version,
                    language,
                    normalizedMessage
                });

                return $"Chat_Response_Shared_{HashHelper.ToShortStableHash(sharedScope)}";
            }

            var contextualScope = string.Join("|", new[]
            {
                "contextual",
                version,
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

        private static bool ContainsAny(string value, IEnumerable<string> keywords)
        {
            return keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
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

    }
}


