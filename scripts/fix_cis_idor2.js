// Fix CIS: add userId to BuildContextAsync + all internal callers + cache key
const fs = require('fs');
const p = 'D:/Users/magang.it8/jifas-assistant/Jifas.Assistant/Services/ConversationIntelligenceService.cs';
let c = fs.readFileSync(p, 'utf8');

// ---- 1. Interface: BuildContextAsync ----
c = c.replace(
    'Task<ConversationContext> BuildContextAsync(string sessionId, int maxTurns = 15);',
    'Task<ConversationContext> BuildContextAsync(string sessionId, string? userId, int maxTurns = 15);'
);

// ---- 2. Interface: GetFormattedContextAsync ----
c = c.replace(
    'Task<string> GetFormattedContextAsync(string sessionId);',
    'Task<string> GetFormattedContextAsync(string sessionId, string? userId);'
);

// ---- 3. Interface: IsFollowUpQueryAsync ----
c = c.replace(
    'Task<bool> IsFollowUpQueryAsync(string sessionId, string currentQuery);',
    'Task<bool> IsFollowUpQueryAsync(string sessionId, string? userId, string currentQuery);'
);

// ---- 4. Interface: CompactSessionAsync ----
c = c.replace(
    'Task<string> CompactSessionAsync(string sessionId, int maxTurns = 20);',
    'Task<string> CompactSessionAsync(string sessionId, string? userId, int maxTurns = 20);'
);

// ---- 5. BuildContextAsync: method signature ----
c = c.replace(
    'public async Task<ConversationContext> BuildContextAsync(string sessionId, int maxTurns = 15)\r\n        {\r\n            const int HISTORY_DEPTH = 200; // max turns to fetch for running summary detection',
    'public async Task<ConversationContext> BuildContextAsync(string sessionId, string? userId, int maxTurns = 15)\r\n        {\r\n            const int HISTORY_DEPTH = 200;'
);

// ---- 6. BuildContextAsync: cache key (first occurrence) ----
c = c.replace(
    'var cacheKey = $"ConversationContext_{sessionId}";\r\n                var cached = _cacheService.Get<ConversationContext>(cacheKey);',
    'var safeUserId = string.IsNullOrWhiteSpace(userId) ? "anon" : userId;\r\n                var cacheKey = $"ConversationContext_{safeUserId}_{sessionId}";\r\n                var cached = _cacheService.Get<ConversationContext>(cacheKey);'
);

// ---- 7. BuildContextAsync: summaryKey first occurrence ----
c = c.replace(
    'var summaryKey = $"RunningSummary_{sessionId}";\r\n                        var cachedSummary = _cacheService.Get<string>(summaryKey);\r\n                        if (string.IsNullOrEmpty(cachedSummary))\r\n                        {\r\n                            cached.RunningSummary = await ComputeRunningSummaryAsync(sessionId, HISTORY_DEPTH, RECENT_WINDOW);',
    'var summaryKey = $"RunningSummary_{safeUserId}_{sessionId}";\r\n                        var cachedSummary = _cacheService.Get<string>(summaryKey);\r\n                        if (string.IsNullOrEmpty(cachedSummary))\r\n                        {\r\n                            cached.RunningSummary = await ComputeRunningSummaryAsync(sessionId, userId, HISTORY_DEPTH, RECENT_WINDOW);'
);

// ---- 8. BuildContextAsync: GetSessionHistoryAsync call ----
c = c.replace(
    'var allHistory = await _chatHistoryService.GetSessionHistoryAsync(sessionId, HISTORY_DEPTH);',
    'var allHistory = await _chatHistoryService.GetSessionHistoryAsync(sessionId, userId, HISTORY_DEPTH);'
);

// ---- 9. BuildContextAsync: summaryKey second occurrence (in the else branch) ----
c = c.replace(
    'var summaryKey = $"RunningSummary_{sessionId}";\r\n                    var cachedSummary = _cacheService.Get<string>(summaryKey);\r\n                    if (!string.IsNullOrEmpty(cachedSummary))\r\n                    {\r\n                        context.RunningSummary = cachedSummary;\r\n                        _logger.LogDebug("[ConversationMemory] Running summary CACHE HIT ({0} older turns)", context.OlderTurnsCount);\r\n                    }\r\n                    else\r\n                    {\r\n                        context.RunningSummary = await ComputeRunningSummaryAsync(sessionId, HISTORY_DEPTH, RECENT_WINDOW);',
    'var summaryKey = $"RunningSummary_{safeUserId}_{sessionId}";\r\n                    var cachedSummary = _cacheService.Get<string>(summaryKey);\r\n                    if (!string.IsNullOrEmpty(cachedSummary))\r\n                    {\r\n                        context.RunningSummary = cachedSummary;\r\n                        _logger.LogDebug("[ConversationMemory] Running summary CACHE HIT ({0} older turns)", context.OlderTurnsCount);\r\n                    }\r\n                    else\r\n                    {\r\n                        context.RunningSummary = await ComputeRunningSummaryAsync(sessionId, userId, HISTORY_DEPTH, RECENT_WINDOW);'
);

// ---- 10. ComputeRunningSummaryAsync: add userId param ----
c = c.replace(
    'private async Task<string> ComputeRunningSummaryAsync(string sessionId, int historyDepth, int recentWindow)\r\n        {\r\n            try\r\n            {\r\n                var allHistory = await _chatHistoryService.GetSessionHistoryAsync(sessionId, historyDepth);',
    'private async Task<string> ComputeRunningSummaryAsync(string sessionId, string? userId, int historyDepth, int recentWindow)\r\n        {\r\n            try\r\n            {\r\n                var allHistory = await _chatHistoryService.GetSessionHistoryAsync(sessionId, userId, historyDepth);'
);

// ---- 11. GetFormattedContextAsync ----
c = c.replace(
    'public async Task<string> GetFormattedContextAsync(string sessionId)\r\n        {\r\n            var context = await BuildContextAsync(sessionId);',
    'public async Task<string> GetFormattedContextAsync(string sessionId, string? userId)\r\n        {\r\n            var context = await BuildContextAsync(sessionId, userId);'
);

// ---- 12. IsFollowUpQueryAsync ----
c = c.replace(
    'public async Task<bool> IsFollowUpQueryAsync(string sessionId, string currentQuery)',
    'public async Task<bool> IsFollowUpQueryAsync(string sessionId, string? userId, string currentQuery)'
);
c = c.replace(
    'var context = await BuildContextAsync(sessionId, 1);',
    'var context = await BuildContextAsync(sessionId, userId, 1);'
);

// ---- 13. CompactSessionAsync ----
c = c.replace(
    'public async Task<string> CompactSessionAsync(string sessionId, int maxTurns = 20)\r\n        {\r\n            try\r\n            {\r\n                if (string.IsNullOrWhiteSpace(sessionId))\r\n                    return string.Empty;\r\n\r\n                var history = await _chatHistoryService.GetSessionHistoryAsync(sessionId, maxTurns);',
    'public async Task<string> CompactSessionAsync(string sessionId, string? userId, int maxTurns = 20)\r\n        {\r\n            try\r\n            {\r\n                if (string.IsNullOrWhiteSpace(sessionId))\r\n                    return string.Empty;\r\n\r\n                var history = await _chatHistoryService.GetSessionHistoryAsync(sessionId, userId, maxTurns);'
);

// Verify key changes
const checks = [
    ['BuildContextAsync(string sessionId, string? userId, int maxTurns = 15)', 'sig'],
    ['GetFormattedContextAsync(string sessionId, string? userId)', 'sig'],
    ['IsFollowUpQueryAsync(string sessionId, string? userId, string currentQuery)', 'sig'],
    ['CompactSessionAsync(string sessionId, string? userId, int maxTurns = 20)', 'sig'],
    ['var safeUserId = string.IsNullOrWhiteSpace(userId) ? "anon" : userId;', 'safeUserId'],
    ['ConversationContext_{safeUserId}_{sessionId}', 'ctxKey'],
    ['RunningSummary_{safeUserId}_{sessionId}', 'sumKey'],
    ['GetSessionHistoryAsync(sessionId, userId, HISTORY_DEPTH)', 'histCall1'],
    ['GetSessionHistoryAsync(sessionId, userId, historyDepth);', 'histCall2'],
    ['ComputeRunningSummaryAsync(sessionId, userId, HISTORY_DEPTH, RECENT_WINDOW)', 'compCall1'],
    ['ComputeRunningSummaryAsync(sessionId, userId, historyDepth, recentWindow)', 'compCall2'],
];

let ok = 0, fail = 0;
for (const [pattern, name] of checks) {
    if (c.includes(pattern)) { ok++; }
    else { fail++; console.error('MISSING: ' + name + ' => ' + pattern); }
}

console.log('Checks: ' + ok + ' OK, ' + fail + ' MISSING');

fs.writeFileSync(p, c, 'utf8');
console.log('Written to: ' + p);
