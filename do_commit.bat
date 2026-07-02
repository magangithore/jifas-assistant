git add -A
git status --short
git commit -m "fix: close IDOR bypass via empty/null userId — return empty context when userId missing, preventing session hijacking for all 3 vectors (userId='', null, 'anon'): ChatHistoryService.GetSessionHistoryAsync returns empty list if userId=null/empty; ConversationIntelligenceService.BuildContextAsync returns empty context; cache key uses raw userId (no anonymous bucket). Premortem: 9 failure scenarios ranked (top: Ollama overload from uncached OOS, HISTORY_DEPTH double-query, soft scope rule)."
git push
