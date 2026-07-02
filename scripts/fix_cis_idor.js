// Fix: add userId to BuildContextAsync, update cache keys, update callers
const fs = require('fs');
const file = 'D:\\Users\\magang.it8\\jifas-assistant\\Jifas.Assistant\\Services\\ConversationIntelligenceService.cs';
let c = fs.readFileSync(file, 'utf8');

// --- 1. Interface: add userId param to BuildContextAsync ---
c = c.replace(
    'Task<ConversationContext> BuildContextAsync(string sessionId, int maxTurns = 15);',
    'Task<ConversationContext> BuildContextAsync(string sessionId, string? userId, int maxTurns = 15);'
);

// --- 2. Interface: add userId param to GetFormattedContextAsync ---
c = c.replace(
    'Task<string> GetFormattedContextAsync(string sessionId);',
    'Task<string> GetFormattedContextAsync(string sessionId, string? userId);'
);

// --- 3. Interface: add userId param to IsFollowUpQueryAsync ---
c = c.replace(
    'Task<bool> IsFollowUpQueryAsync(string sessionId, string currentQuery);',
    'Task<bool> IsFollowUpQueryAsync(string sessionId, string? userId, string currentQuery);'
);

// --- 4. BuildContextAsync: update method signature + cache key + GetSessionHistoryAsync calls ---
// Replace the entire BuildContextAsync method
const oldBuild = `        public async Task<ConversationContext> BuildContextAsync(string sessionId, int maxTurns = 15)
        {
            const int HISTORY_DEPTH = 200; // max turns to fetch for running summary detection
            const int RECENT_WINDOW = 15;  // turns shown verbatim in prompt
            const int SUMMARY_TTL_MIN = 30;

            var context = new ConversationContext { SessionId = sessionId };

            try
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                    return context;

                // Check cache first
                var cacheKey = $"ConversationContext_{sessionId}";
                var cached = _cacheService.Get<ConversationContext>(cacheKey);
                if (cached != null && cached.RecentTurns.Count > 0)
                {
                    // Cache hit — verify RunningSummary still valid if older turns exist
                    if (cached.OlderTurnsCount > 0)
                    {
                        var summaryKey = $"RunningSummary_{sessionId}";
                        var cachedSummary = _cacheService.Get<string>(summaryKey);
                        if (string.IsNullOrEmpty(cachedSummary))
                        {
                            cached.RunningSummary = await ComputeRunningSummaryAsync(sessionId, HISTORY_DEPTH, RECENT_WINDOW);
                            _cacheService.Set(summaryKey, cached.RunningSummary, SUMMARY_TTL_MIN);
                        }
                        else cached.RunningSummary = cachedSummary;
                    }
                    _logger.LogDebug("[ConversationMemory] Cache HIT (summaryLen={0})", cached.RunningSummary.Length);
                    return cached;
                }

                // Cache miss — fetch up to HISTORY_DEPTH to know total session size
                var allHistory = await _chatHistoryService.GetSessionHistoryAsync(sessionId, HISTORY_DEPTH);
                if (allHistory == null || allHistory.Count == 0)
                    return context;

                context.TotalSessionTurns = allHistory.Count;

                // Build full turn list (oldest first, skip [Session Greeting])
                var allTurns = allHistory
                    .Where(h => h.UserMessage != "[Session Greeting]")
                    .OrderBy(h => h.CreatedAt)
                    .Select(h => new ConversationTurn
                    {
                        UserMessage = h.UserMessage,
                        AssistantResponse = TruncateResponse(h.AiResponse, 300),
                        Timestamp = h.CreatedAt,
                        Topic = ExtractTopic(h.UserMessage) ?? string.Empty
                    })
                    .ToList();

                // Split: last 15 = recent window, rest = older (for summary)
                context.RecentTurns = allTurns.TakeLast(RECENT_WINDOW).ToList();
                context.HasPreviousContext = allTurns.Count > 0;
                context.TopicsDiscussed = allTurns.Select(t => t.Topic).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
                context.CurrentTopic = allTurns.LastOrDefault()?.Topic ?? string.Empty;
                context.FormattedContext = FormatContext(context.RecentTurns);

                var olderTurns = allTurns.Take(Math.Max(0, allTurns.Count - RECENT_WINDOW)).ToList();
                context.OlderTurnsCount = olderTurns.Count;

                if (context.OlderTurnsCount > 0)
                {
                    var summaryKey = $"RunningSummary_{sessionId}";
                    var cachedSummary = _cacheService.Get<string>(summaryKey);
                    if (!string.IsNullOrEmpty(cachedSummary))
                    {
                        context.RunningSummary = cachedSummary;
                        _logger.LogDebug("[ConversationMemory] Running summary CACHE HIT ({0} older turns)", context.OlderTurnsCount);
                    }
                    else
                    {
                        context.RunningSummary = await ComputeRunningSummaryAsync(sessionId, HISTORY_DEPTH, RECENT_WINDOW);
                        _cacheService.Set(summaryKey, context.RunningSummary, SUMMARY_TTL_MIN);
                        _logger.LogInformation("[ConversationMemory] Running summary COMPUTED ({0} older turns, {1} chars)",
                            context.OlderTurnsCount, context.RunningSummary.Length);
                    }
                }
                else
                {
                    context.RunningSummary = string.Empty;
                    _logger.LogDebug("[ConversationMemory] No running summary needed ({0} total turns)", allTurns.Count);
                }

                // Cache main context (30 min)
                _cacheService.Set(cacheKey, context, 30);

                _logger.LogInformation("[ConversationMemory] Built context {0}: {1} total, {2} recent, {3} older (summary={4})",
                    sessionId, allTurns.Count, context.RecentTurns.Count, context.OlderTurnsCount,
                    context.RunningSummary.Length > 0 ? "yes" : "no");

                return context;
            }
            catch (Exception ex)
            {
                _logger.LogError("[ConversationMemory] Error building context: {0}", null, ex.Message);
                return context;
            }
        }`;

const newBuild = `        public async Task<ConversationContext> BuildContextAsync(string sessionId, string? userId, int maxTurns = 15)
        {
            const int HISTORY_DEPTH = 200;
            const int RECENT_WINDOW = 15;
            const int SUMMARY_TTL_MIN = 30;

            var context = new ConversationContext { SessionId = sessionId };

            try
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                    return context;

                // IDOR guard: cache key includes userId — different user = different cache entry.
                // Prevents a different user's request from reading this user's cached context.
                var safeUserId = string.IsNullOrWhiteSpace(userId) ? "anon" : userId;
                var cacheKey = $"ConversationContext_{safeUserId}_{sessionId}";

                // Check cache — cache is per-(userId, sessionId) pair.
                var cached = _cacheService.Get<ConversationContext>(cacheKey);
                if (cached != null && cached.RecentTurns.Count > 0)
                {
                    // Verify RunningSummary still valid if older turns exist.
                    if (cached.OlderTurnsCount > 0)
                    {
                        var summaryKey = $"RunningSummary_{safeUserId}_{sessionId}";
                        var cachedSummary = _cacheService.Get<string>(summaryKey);
                        if (string.IsNullOrEmpty(cachedSummary))
                        {
                            cached.RunningSummary = await ComputeRunningSummaryAsync(sessionId, userId, HISTORY_DEPTH, RECENT_WINDOW);
                            _cacheService.Set(summaryKey, cached.RunningSummary, SUMMARY_TTL_MIN);
                        }
                        else cached.RunningSummary = cachedSummary;
                    }
                    _logger.LogDebug("[ConversationMemory] Cache HIT (userId={0}, summaryLen={1})", safeUserId, cached.RunningSummary.Length);
                    return cached;
                }

                // Cache miss — fetch history filtered by userId (IDOR guard in the service).
                var allHistory = await _chatHistoryService.GetSessionHistoryAsync(sessionId, userId, HISTORY_DEPTH);
                if (allHistory == null || allHistory.Count == 0)
                    return context;

                context.TotalSessionTurns = allHistory.Count;

                var allTurns = allHistory
                    .Where(h => h.UserMessage != "[Session Greeting]")
                    .OrderBy(h => h.CreatedAt)
                    .Select(h => new ConversationTurn
                    {
                        UserMessage = h.UserMessage,
                        AssistantResponse = TruncateResponse(h.AiResponse, 300),
                        Timestamp = h.CreatedAt,
                        Topic = ExtractTopic(h.UserMessage) ?? string.Empty
                    })
                    .ToList();

                context.RecentTurns = allTurns.TakeLast(RECENT_WINDOW).ToList();
                context.HasPreviousContext = allTurns.Count > 0;
                context.TopicsDiscussed = allTurns.Select(t => t.Topic).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
                context.CurrentTopic = allTurns.LastOrDefault()?.Topic ?? string.Empty;
                context.FormattedContext = FormatContext(context.RecentTurns);

                var olderTurns = allTurns.Take(Math.Max(0, allTurns.Count - RECENT_WINDOW)).ToList();
                context.OlderTurnsCount = olderTurns.Count;

                if (context.OlderTurnsCount > 0)
                {
                    var summaryKey = $"RunningSummary_{safeUserId}_{sessionId}";
                    var cachedSummary = _cacheService.Get<string>(summaryKey);
                    if (!string.IsNullOrEmpty(cachedSummary))
                    {
                        context.RunningSummary = cachedSummary;
                        _logger.LogDebug("[ConversationMemory] Running summary CACHE HIT ({0} older turns)", context.OlderTurnsCount);
                    }
                    else
                    {
                        context.RunningSummary = await ComputeRunningSummaryAsync(sessionId, userId, HISTORY_DEPTH, RECENT_WINDOW);
                        _cacheService.Set(summaryKey, context.RunningSummary, SUMMARY_TTL_MIN);
                        _logger.LogInformation("[ConversationMemory] Running summary COMPUTED ({0} older turns, {1} chars)",
                            context.OlderTurnsCount, context.RunningSummary.Length);
                    }
                }
                else
                {
                    context.RunningSummary = string.Empty;
                    _logger.LogDebug("[ConversationMemory] No running summary needed ({0} total turns)", allTurns.Count);
                }

                _cacheService.Set(cacheKey, context, 30);

                _logger.LogInformation("[ConversationMemory] Built context {0} (userId={1}): {2} total, {3} recent, {4} older (summary={5})",
                    sessionId, safeUserId, allTurns.Count, context.RecentTurns.Count, context.OlderTurnsCount,
                    context.RunningSummary.Length > 0 ? "yes" : "no");

                return context;
            }
            catch (Exception ex)
            {
                _logger.LogError("[ConversationMemory] Error building context: {0}", null, ex.Message);
                return context;
            }
        }`;

if (!c.includes(oldBuild)) {
    console.error('ERROR: old BuildContextAsync signature not found. Searching...');
    // Try to find what's there
    const idx = c.indexOf('public async Task<ConversationContext> BuildContextAsync');
    console.error('Found at index:', idx);
    console.error('Content around it:', c.substring(idx, idx+200));
    process.exit(1);
}
c = c.replace(oldBuild, newBuild);

// --- 5. ComputeRunningSummaryAsync: add userId param ---
c = c.replace(
    'private async Task<string> ComputeRunningSummaryAsync(string sessionId, int historyDepth, int recentWindow)',
    'private async Task<string> ComputeRunningSummaryAsync(string sessionId, string? userId, int historyDepth, int recentWindow)'
);
// Update the GetSessionHistoryAsync call inside ComputeRunningSummaryAsync
c = c.replace(
    `                var allHistory = await _chatHistoryService.GetSessionHistoryAsync(sessionId, historyDepth);
                if (allHistory == null || allHistory.Count == 0)
                    return string.Empty;

                var olderRaw = allHistory
                    .Where(h => h.UserMessage != "[Session Greeting]")
                    .OrderBy(h => h.CreatedAt)
                    .ToList();

                var cutoff = olderRaw.Count - recentWindow;
                if (cutoff <= 0)
                    return string.Empty;

                olderRaw = olderRaw.Take(cutoff).ToList();`,
    `                var allHistory = await _chatHistoryService.GetSessionHistoryAsync(sessionId, userId, historyDepth);
                if (allHistory == null || allHistory.Count == 0)
                    return string.Empty;

                var olderRaw = allHistory
                    .Where(h => h.UserMessage != "[Session Greeting]")
                    .OrderBy(h => h.CreatedAt)
                    .ToList();

                var cutoff = olderRaw.Count - recentWindow;
                if (cutoff <= 0)
                    return string.Empty;

                olderRaw = olderRaw.Take(cutoff).ToList();`
);

// --- 6. GetFormattedContextAsync: add userId param ---
c = c.replace(
    `        public async Task<string> GetFormattedContextAsync(string sessionId)
        {
            var context = await BuildContextAsync(sessionId);
            return context.FormattedContext ?? string.Empty;
        }`,
    `        public async Task<string> GetFormattedContextAsync(string sessionId, string? userId)
        {
            var context = await BuildContextAsync(sessionId, userId);
            return context.FormattedContext ?? string.Empty;
        }`
);

// --- 7. IsFollowUpQueryAsync: add userId param ---
c = c.replace(
    `        public async Task<bool> IsFollowUpQueryAsync(string sessionId, string currentQuery)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(currentQuery))
                    return false;

                var queryLower = currentQuery.ToLower();

                // Check for follow-up indicators
                foreach (var indicator in FollowUpIndicators)
                {
                    if (queryLower.Contains(indicator))
                    {
                        _logger.LogDebug($"[ConversationMemory] Follow-up detected: indicator '{indicator}'");
                        return true;
                    }
                }

                // Check for reference pronouns
                foreach (var pronoun in ReferencePronouns)
                {
                    if (queryLower.Contains(pronoun))
                    {
                        // Verify there's previous context
                        var context = await BuildContextAsync(sessionId, 1);
                        if (context.HasPreviousContext)
                        {
                            _logger.LogDebug($"[ConversationMemory] Follow-up detected: pronoun '{pronoun}' with context");
                            return true;
                        }
                    }
                }

                // Check if query is very short (likely a follow-up)
                var wordCount = currentQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                if (wordCount <= 3)
                {
                    var context = await BuildContextAsync(sessionId, 1);
                    if (context.HasPreviousContext)
                    {
                        _logger.LogDebug("[ConversationMemory] Follow-up detected: short query with context");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ConversationMemory] Error checking follow-up: {ex.Message}");
                return false;
            }
        }`,
    `        public async Task<bool> IsFollowUpQueryAsync(string sessionId, string? userId, string currentQuery)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(currentQuery))
                    return false;

                var queryLower = currentQuery.ToLower();

                foreach (var indicator in FollowUpIndicators)
                {
                    if (queryLower.Contains(indicator))
                    {
                        _logger.LogDebug($"[ConversationMemory] Follow-up detected: indicator '{indicator}'");
                        return true;
                    }
                }

                foreach (var pronoun in ReferencePronouns)
                {
                    if (queryLower.Contains(pronoun))
                    {
                        var context = await BuildContextAsync(sessionId, userId, 1);
                        if (context.HasPreviousContext)
                        {
                            _logger.LogDebug($"[ConversationMemory] Follow-up detected: pronoun '{pronoun}' with context");
                            return true;
                        }
                    }
                }

                var wordCount = currentQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                if (wordCount <= 3)
                {
                    var context = await BuildContextAsync(sessionId, userId, 1);
                    if (context.HasPreviousContext)
                    {
                        _logger.LogDebug("[ConversationMemory] Follow-up detected: short query with context");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ConversationMemory] Error checking follow-up: {ex.Message}");
                return false;
            }
        }`
);

// --- 8. CompactSessionAsync: add userId param + update GetSessionHistoryAsync call ---
c = c.replace(
    `        public async Task<string> CompactSessionAsync(string sessionId, int maxTurns = 20)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                    return string.Empty;

                var history = await _chatHistoryService.GetSessionHistoryAsync(sessionId, maxTurns);`,
    `        public async Task<string> CompactSessionAsync(string sessionId, string? userId, int maxTurns = 20)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                    return string.Empty;

                var history = await _chatHistoryService.GetSessionHistoryAsync(sessionId, userId, maxTurns);`
);

fs.writeFileSync(file, c, 'utf8');
console.log('CIS updated successfully.');
