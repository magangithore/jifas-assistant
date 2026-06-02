using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using jifas_assistant.DAL.Models;

namespace Jifas.Assistant.Services
{
    #region Models

    /// <summary>
    /// Conversation context dari memory
    /// </summary>
    public class ConversationContext
    {
        public string SessionId { get; set; }
        public List<ConversationTurn> RecentTurns { get; set; } = new List<ConversationTurn>();
        public List<string> TopicsDiscussed { get; set; } = new List<string>();
        public string CurrentTopic { get; set; }
        public bool HasPreviousContext { get; set; }
        public string FormattedContext { get; set; }
    }

    public class ConversationTurn
    {
        public string UserMessage { get; set; }
        public string AssistantResponse { get; set; }
        public DateTime Timestamp { get; set; }
        public string Topic { get; set; }
    }

    /// <summary>
    /// Feedback dari user
    /// </summary>
    public class UserFeedbackInput
    {
        public int? ChatId { get; set; }
        public string SessionId { get; set; }
        public string MessageId { get; set; }
        public int Rating { get; set; }  // 1-5
        public string Comment { get; set; }
        public string UserId { get; set; }
    }

    /// <summary>
    /// Pattern yang sering gagal
    /// </summary>
    public class FailurePattern
    {
        public string QueryPattern { get; set; }
        public int FailureCount { get; set; }
        public List<string> CommonIssues { get; set; } = new List<string>();
        public double AverageRating { get; set; }
    }

    /// <summary>
    /// Success pattern yang bisa di-reuse
    /// </summary>
    public class SuccessPattern
    {
        public string QueryType { get; set; }
        public string Topic { get; set; }
        public string ResponseStructure { get; set; }
        public double AverageRating { get; set; }
        public int UsageCount { get; set; }
    }

    #endregion

    #region Interfaces

    /// <summary>
    /// Service untuk manage conversation memory dan context
    /// Memungkinkan AI untuk memahami follow-up questions dengan lebih baik
    /// </summary>
    public interface IConversationMemoryService
    {
        Task<ConversationContext> BuildContextAsync(string sessionId, int maxTurns = 5);
        Task<string> GetFormattedContextAsync(string sessionId);
        Task<bool> IsFollowUpQueryAsync(string sessionId, string currentQuery);
        string ExtractTopic(string message);

        /// <summary>
        /// Compact/summarize an entire session into a structured summary.
        /// Inspired by Claude Code's compaction system — preserves user intents,
        /// key decisions, errors, and pending work in a dense format.
        /// Used for: ticket descriptions, long-session context, handoff to IT.
        /// </summary>
        Task<string> CompactSessionAsync(string sessionId, int maxTurns = 20);
    }

    /// <summary>
    /// Service untuk collect dan analyze user feedback
    /// Membantu improve AI responses over time
    /// </summary>
    public interface IFeedbackLearningService
    {
        Task RecordFeedbackAsync(UserFeedbackInput feedback);
        Task<List<FailurePattern>> GetFailurePatternsAsync(int top = 10);
        Task<List<SuccessPattern>> GetSuccessPatternsAsync(string topic = null);
        Task<bool> IsKnownFailurePatternAsync(string query);
        Task<List<string>> GetImprovementSuggestionsAsync(string query);
    }

    /// <summary>
    /// Unified interface combining conversation memory and feedback learning
    /// </summary>
    public interface IConversationIntelligenceService : IConversationMemoryService, IFeedbackLearningService
    {
    }

    #endregion

    /// <summary>
    /// Unified service untuk conversation intelligence:
    /// - Conversation Memory: Manage context dan follow-up detection
    /// - Feedback Learning: Learn dari user feedback untuk improve responses
    /// </summary>
    public class ConversationIntelligenceService : IConversationIntelligenceService
    {
        private readonly JIFAS_AssistantContext _db;
        private readonly IChatHistoryService _chatHistoryService;
        private readonly ICacheService _cacheService;
        private readonly ILoggerService _logger;

        // Memory constants
        private const int MEMORY_WINDOW = 5;
        private const int MAX_CONTEXT_LENGTH = 2000;

        // Rating thresholds
        private const int POOR_RATING = 2;
        private const int GOOD_RATING = 4;

        // Follow-up indicators
        private static readonly List<string> FollowUpIndicators = new List<string>
        {
            "itu", "ini", "tersebut", "tadi", "sebelumnya", "lanjut",
            "terus", "kemudian", "selanjutnya", "bagaimana dengan",
            "dan juga", "satu lagi", "gimana kalau", "kalau",
            "apakah bisa", "apa lagi", "yang lain"
        };

        // Pronouns that might refer to previous context
        private static readonly List<string> ReferencePronouns = new List<string>
        {
            "itu", "ini", "dia", "mereka", "nya", "tersebut", "-nya"
        };

        // Topic keywords mapping
        private static readonly Dictionary<string, List<string>> TopicKeywords = new Dictionary<string, List<string>>
        {
            { "Invoice", new List<string> { "invoice", "tagihan", "faktur", "inv" } },
            { "Payment", new List<string> { "payment", "pembayaran", "bayar", "transfer" } },
            { "PUM", new List<string> { "pum", "uang muka", "advance", "kasbon" } },
            { "Receiving", new List<string> { "receiving", "penerimaan", "rv", "terima barang" } },
            { "Budget", new List<string> { "budget", "anggaran", "overbudget" } },
            { "Approval", new List<string> { "approval", "approve", "reject", "otorisasi" } },
            { "Master Data", new List<string> { "master", "vendor", "coa", "company", "divisi" } },
            { "Accounting", new List<string> { "posting", "jurnal", "gl", "ledger", "akuntansi" } },
            { "Report", new List<string> { "report", "laporan", "dashboard", "monitor" } },
            { "Troubleshooting", new List<string> { "error", "masalah", "gagal", "tidak bisa" } }
        };

        public ConversationIntelligenceService(
            JIFAS_AssistantContext db,
            IChatHistoryService chatHistoryService,
            ICacheService cacheService,
            ILoggerService logger)
        {
            _db = db;
            _chatHistoryService = chatHistoryService;
            _cacheService = cacheService;
            _logger = logger;
        }

        #region Conversation Memory Methods

        public async Task<ConversationContext> BuildContextAsync(string sessionId, int maxTurns = 5)
        {
            var context = new ConversationContext { SessionId = sessionId };

            try
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    return context;
                }

                // Check cache first
                var cacheKey = $"ConversationContext_{sessionId}";
                var cached = _cacheService.Get<ConversationContext>(cacheKey);
                if (cached != null && cached.RecentTurns.Count > 0)
                {
                    _logger.LogDebug("[ConversationMemory] Cache HIT");
                    return cached;
                }

                // Get recent history from database
                var history = await _chatHistoryService.GetSessionHistoryAsync(sessionId, maxTurns);
                
                if (history == null || history.Count == 0)
                {
                    return context;
                }

                // Build conversation turns (oldest first)
                var turns = history
                    .OrderBy(h => h.CreatedAt)
                    .Select(h => new ConversationTurn
                    {
                        UserMessage = h.UserMessage,
                        AssistantResponse = TruncateResponse(h.AiResponse, 300),
                        Timestamp = h.CreatedAt,
                        Topic = ExtractTopic(h.UserMessage)
                    })
                    .Take(maxTurns)
                    .ToList();

                context.RecentTurns = turns;
                context.HasPreviousContext = turns.Count > 0;
                context.TopicsDiscussed = turns.Select(t => t.Topic).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
                context.CurrentTopic = turns.LastOrDefault()?.Topic;
                context.FormattedContext = FormatContext(turns);

                // Cache for 30 minutes
                _cacheService.Set(cacheKey, context, 30);

                _logger.LogInformation($"[ConversationMemory] Built context for session {sessionId}: {turns.Count} turns, Topics: {string.Join(", ", context.TopicsDiscussed)}");

                return context;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ConversationMemory] Error building context: {ex.Message}");
                return context;
            }
        }

        public async Task<string> GetFormattedContextAsync(string sessionId)
        {
            var context = await BuildContextAsync(sessionId);
            return context.FormattedContext;
        }

        public async Task<bool> IsFollowUpQueryAsync(string sessionId, string currentQuery)
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
        }

        public string ExtractTopic(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return null;

            var messageLower = message.ToLower();

            foreach (var topic in TopicKeywords)
            {
                foreach (var keyword in topic.Value)
                {
                    if (messageLower.Contains(keyword))
                    {
                        return topic.Key;
                    }
                }
            }

            return "General";
        }

        private string FormatContext(List<ConversationTurn> turns)
        {
            if (turns == null || turns.Count == 0)
                return null;

            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("=== KONTEKS PERCAKAPAN SEBELUMNYA ===");

            // Section 1: Topics discussed (structured overview)
            var topics = turns.Select(t => t.Topic)
                .Where(t => !string.IsNullOrEmpty(t) && t != "General")
                .Distinct().ToList();
            if (topics.Count > 0)
                contextBuilder.AppendLine($"Topik yang dibahas: {string.Join(", ", topics)}");

            // Section 2: Conversation intent history
            var lastTurn = turns.LastOrDefault();
            if (lastTurn != null)
                contextBuilder.AppendLine($"Topik terakhir: {lastTurn.Topic ?? "General"}");

            contextBuilder.AppendLine();

            // Section 3: Conversation turns (most recent)
            contextBuilder.AppendLine("Percakapan terkini:");
            foreach (var turn in turns.TakeLast(MEMORY_WINDOW))
            {
                contextBuilder.AppendLine($"User: {turn.UserMessage}");
                contextBuilder.AppendLine($"AI: {turn.AssistantResponse}");
                contextBuilder.AppendLine();
            }

            contextBuilder.AppendLine("=== AKHIR KONTEKS ===");
            contextBuilder.AppendLine();
            contextBuilder.AppendLine("INSTRUKSI KONTEKS:");
            contextBuilder.AppendLine("- Jika user merujuk 'itu', 'ini', 'tersebut', 'tadi' → hubungkan dengan konteks di atas.");
            contextBuilder.AppendLine("- Jika user bertanya hal baru → jawab tanpa memaksakan hubungan ke konteks sebelumnya.");
            contextBuilder.AppendLine("- Jika user minta klarifikasi → berikan detail lebih dalam dari topik sebelumnya.");

            var formatted = contextBuilder.ToString();

            // Truncate if too long
            if (formatted.Length > MAX_CONTEXT_LENGTH)
            {
                formatted = formatted.Substring(0, MAX_CONTEXT_LENGTH) + "\n... [context diperpendek]";
            }

            return formatted;
        }

        private string TruncateResponse(string response, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(response))
                return "";

            if (response.Length <= maxLength)
                return response;

            // Find natural break point
            var truncated = response.Substring(0, maxLength);
            var lastPeriod = truncated.LastIndexOf('.');
            var lastNewline = truncated.LastIndexOf('\n');

            var breakPoint = Math.Max(lastPeriod, lastNewline);
            if (breakPoint > maxLength * 0.6)
            {
                return truncated.Substring(0, breakPoint + 1).Trim();
            }

            return truncated.Trim() + "...";
        }

        /// <summary>
        /// Compact/summarize the entire session conversation into a structured summary.
        /// Inspired by Claude Code's compaction system — preserves user intents,
        /// key decisions, errors/issues, and context in a dense format.
        /// </summary>
        public async Task<string> CompactSessionAsync(string sessionId, int maxTurns = 20)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                    return string.Empty;

                var history = await _chatHistoryService.GetSessionHistoryAsync(sessionId, maxTurns);
                if (history == null || history.Count == 0)
                    return string.Empty;

                var turns = history
                    .OrderBy(h => h.CreatedAt)
                    .Select(h => new ConversationTurn
                    {
                        UserMessage = h.UserMessage,
                        AssistantResponse = TruncateResponse(h.AiResponse, 500),
                        Timestamp = h.CreatedAt,
                        Topic = ExtractTopic(h.UserMessage)
                    })
                    .ToList();

                // Build structured summary (deterministic, no AI call needed)
                var sb = new StringBuilder();

                // 1. Topics discussed
                var topics = turns.Select(t => t.Topic)
                    .Where(t => !string.IsNullOrEmpty(t) && t != "General")
                    .Distinct().ToList();
                if (topics.Count > 0)
                    sb.AppendLine($"Topik: {string.Join(", ", topics)}");

                // 2. User's messages (the core problems/questions)
                sb.AppendLine();
                sb.AppendLine("Kronologi percakapan:");
                foreach (var turn in turns)
                {
                    sb.AppendLine($"- [{turn.Timestamp:HH:mm}] User: {turn.UserMessage}");
                    if (!string.IsNullOrEmpty(turn.AssistantResponse))
                        sb.AppendLine($"  AI: {turn.AssistantResponse}");
                }

                // 3. Detect unresolved issues
                var lastUserMsg = turns.LastOrDefault()?.UserMessage?.ToLowerInvariant() ?? "";
                var hasUnresolved = lastUserMsg.Contains("error") || lastUserMsg.Contains("gagal") ||
                                    lastUserMsg.Contains("masalah") || lastUserMsg.Contains("tidak bisa") ||
                                    lastUserMsg.Contains("tiket");
                if (hasUnresolved)
                    sb.AppendLine("\nStatus: Masalah belum sepenuhnya terselesaikan");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ConversationMemory] Error compacting session: {ex.Message}");
                return string.Empty;
            }
        }

        #endregion

        #region Feedback Learning Methods

        public async Task RecordFeedbackAsync(UserFeedbackInput feedback)
        {
            try
            {
                if (feedback == null)
                {
                    _logger.LogWarning("[FeedbackLearning] Invalid feedback input");
                    return;
                }

                // Save to UserFeedbacks table
                var userFeedback = new UserFeedbacks
                {
                    ChatId = feedback.ChatId,
                    UserId = feedback.UserId ?? "anonymous",
                    Rating = feedback.Rating,
                    Comment = feedback.Comment,
                    IsHelpful = feedback.Rating >= 3,
                    CreatedAt = DateTime.UtcNow
                };

                _db.UserFeedbacks.Add(userFeedback);
                await _db.SaveChangesAsync();

                // Process feedback for learning
                await ProcessFeedbackForLearningAsync(feedback);

                _logger.LogInformation($"[FeedbackLearning] Recorded feedback: ChatId={feedback.ChatId}, Rating={feedback.Rating}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[FeedbackLearning] Error recording feedback: {ex.Message}");
            }
        }

        public async Task<List<FailurePattern>> GetFailurePatternsAsync(int top = 10)
        {
            try
            {
                // Check cache
                var cacheKey = $"FailurePatterns_{top}";
                var cached = _cacheService.Get<List<FailurePattern>>(cacheKey);
                if (cached != null)
                {
                    return cached;
                }

                // Query poor-rated feedbacks with their chat IDs
                var poorFeedbacks = await _db.UserFeedbacks
                    .Where(f => f.Rating != null && f.Rating <= POOR_RATING && f.ChatId != null)
                    .OrderByDescending(f => f.CreatedAt)
                    .Take(100)
                    .Select(f => f.ChatId.Value)
                    .ToListAsync();

                if (poorFeedbacks.Count == 0)
                {
                    return new List<FailurePattern>();
                }

                // Get related chat history by matching with ChatId
                var failedChats = await _db.ChatHistories
                    .Where(ch => poorFeedbacks.Contains(ch.Id))
                    .ToListAsync();

                // Analyze patterns
                var patterns = AnalyzeFailurePatterns(failedChats);

                // Cache for 1 hour
                _cacheService.Set(cacheKey, patterns, 60);

                return patterns.Take(top).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[FeedbackLearning] Error getting failure patterns: {ex.Message}");
                return new List<FailurePattern>();
            }
        }

        public async Task<List<SuccessPattern>> GetSuccessPatternsAsync(string topic = null)
        {
            try
            {
                // Check cache
                var cacheKey = $"SuccessPatterns_{topic ?? "all"}";
                var cached = _cacheService.Get<List<SuccessPattern>>(cacheKey);
                if (cached != null)
                {
                    return cached;
                }

                // Query good-rated feedbacks
                var goodFeedbacks = await _db.UserFeedbacks
                    .Where(f => f.Rating != null && f.Rating >= GOOD_RATING && f.ChatId != null)
                    .OrderByDescending(f => f.CreatedAt)
                    .Take(100)
                    .Select(f => f.ChatId.Value)
                    .ToListAsync();

                if (goodFeedbacks.Count == 0)
                {
                    return new List<SuccessPattern>();
                }

                // Get related chat history
                var successChats = await _db.ChatHistories
                    .Where(ch => goodFeedbacks.Contains(ch.Id) && ch.Success == true)
                    .ToListAsync();

                // Analyze success patterns
                var patterns = AnalyzeSuccessPatterns(successChats, topic);

                // Cache for 1 hour
                _cacheService.Set(cacheKey, patterns, 60);

                return patterns;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[FeedbackLearning] Error getting success patterns: {ex.Message}");
                return new List<SuccessPattern>();
            }
        }

        public async Task<bool> IsKnownFailurePatternAsync(string query)
        {
            try
            {
                var failurePatterns = await GetFailurePatternsAsync(20);
                var queryLower = query.ToLower();

                foreach (var pattern in failurePatterns)
                {
                    if (!string.IsNullOrEmpty(pattern.QueryPattern))
                    {
                        // Simple substring match
                        var patternWords = pattern.QueryPattern.ToLower().Split(' ');
                        var matchCount = patternWords.Count(w => queryLower.Contains(w));
                        
                        if (matchCount >= patternWords.Length * 0.6) // 60% match
                        {
                            _logger.LogWarning($"[FeedbackLearning] Query matches known failure pattern: {pattern.QueryPattern}");
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[FeedbackLearning] Error checking failure pattern: {ex.Message}");
                return false;
            }
        }

        public async Task<List<string>> GetImprovementSuggestionsAsync(string query)
        {
            var suggestions = new List<string>();

            try
            {
                // Check if matches failure pattern
                var failurePatterns = await GetFailurePatternsAsync(10);
                
                foreach (var pattern in failurePatterns)
                {
                    if (!string.IsNullOrEmpty(pattern.QueryPattern) && 
                        query.ToLower().Contains(pattern.QueryPattern.ToLower()))
                    {
                        suggestions.AddRange(pattern.CommonIssues.Take(2));
                    }
                }

                // Get successful patterns for similar topic
                var successPatterns = await GetSuccessPatternsAsync();
                if (successPatterns.Any())
                {
                    var topPattern = successPatterns.First();
                    suggestions.Add($"Gunakan struktur jawaban: {topPattern.ResponseStructure}");
                }

                return suggestions.Distinct().Take(3).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[FeedbackLearning] Error getting improvement suggestions: {ex.Message}");
                return new List<string>();
            }
        }

        private async Task ProcessFeedbackForLearningAsync(UserFeedbackInput feedback)
        {
            try
            {
                // For poor ratings, flag for review
                if (feedback.Rating <= POOR_RATING && feedback.ChatId.HasValue)
                {
                    var chatHistory = await _db.ChatHistories
                        .Where(ch => ch.Id == feedback.ChatId.Value)
                        .FirstOrDefaultAsync();

                    if (chatHistory != null)
                    {
                        _logger.LogWarning($"[FeedbackLearning] Poor rating flagged for review - " +
                            $"Query: '{chatHistory.UserMessage}', Response source: {chatHistory.ResponseSource}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[FeedbackLearning] Error processing feedback: {ex.Message}");
            }
        }

        private List<FailurePattern> AnalyzeFailurePatterns(List<ChatHistory> failedChats)
        {
            var patterns = new Dictionary<string, FailurePattern>();

            foreach (var chat in failedChats)
            {
                if (string.IsNullOrWhiteSpace(chat.UserMessage))
                    continue;

                // Extract key topic/pattern from message
                var topic = ExtractQueryTopic(chat.UserMessage);
                
                if (!patterns.ContainsKey(topic))
                {
                    patterns[topic] = new FailurePattern
                    {
                        QueryPattern = topic,
                        FailureCount = 0,
                        CommonIssues = new List<string>()
                    };
                }

                patterns[topic].FailureCount++;

                // Analyze why it might have failed
                if (!chat.IsFromKnowledgeBase)
                {
                    patterns[topic].CommonIssues.Add("No KB match found");
                }
                if (chat.ConfidenceScore < 0.5)
                {
                    patterns[topic].CommonIssues.Add("Low confidence score");
                }
            }

            // Remove duplicate issues and sort by failure count
            return patterns.Values
                .Select(p => new FailurePattern
                {
                    QueryPattern = p.QueryPattern,
                    FailureCount = p.FailureCount,
                    CommonIssues = p.CommonIssues.Distinct().Take(3).ToList()
                })
                .OrderByDescending(p => p.FailureCount)
                .ToList();
        }

        private List<SuccessPattern> AnalyzeSuccessPatterns(List<ChatHistory> successChats, string filterTopic)
        {
            var patterns = new Dictionary<string, SuccessPattern>();

            foreach (var chat in successChats)
            {
                var topic = ExtractQueryTopic(chat.UserMessage);
                
                if (!string.IsNullOrEmpty(filterTopic) && topic != filterTopic)
                    continue;

                if (!patterns.ContainsKey(topic))
                {
                    patterns[topic] = new SuccessPattern
                    {
                        Topic = topic,
                        QueryType = DetectQueryType(chat.UserMessage),
                        ResponseStructure = AnalyzeResponseStructure(chat.AiResponse),
                        UsageCount = 0
                    };
                }

                patterns[topic].UsageCount++;
            }

            return patterns.Values
                .OrderByDescending(p => p.UsageCount)
                .ToList();
        }

        private string ExtractQueryTopic(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "unknown";

            var messageLower = message.ToLower();

            foreach (var topic in TopicKeywords)
            {
                if (topic.Value.Any(k => messageLower.Contains(k)))
                {
                    return topic.Key;
                }
            }

            return "General";
        }

        private string DetectQueryType(string message)
        {
            var messageLower = message.ToLower();

            if (messageLower.Contains("cara") || messageLower.Contains("bagaimana"))
                return "HowTo";
            if (messageLower.Contains("error") || messageLower.Contains("masalah"))
                return "Troubleshooting";
            if (messageLower.Contains("apa"))
                return "Definition";

            return "General";
        }

        private string AnalyzeResponseStructure(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return "Unknown";

            if (response.Contains("1.") || response.Contains("1)"))
                return "Numbered Steps";
            if (response.Contains("-") || response.Contains("�"))
                return "Bullet Points";
            if (response.Contains(":\n"))
                return "Labeled Sections";

            return "Paragraph";
        }

        #endregion
    }
}
