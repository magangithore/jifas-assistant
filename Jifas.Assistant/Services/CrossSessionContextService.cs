using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using jifas_assistant.DAL.Models;
using Jifas.Assistant.Services;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Result dari cross-session context retrieval.
    /// Berisi data percakapan sesi sebelumnya untuk diinject saat user buka sesi baru.
    /// </summary>
    public class CrossSessionContext
    {
        public bool HasPriorSession { get; set; }
        public string? PriorSessionId { get; set; }
        public List<ConversationTurn> PriorTurns { get; set; } = new();
        public List<string> TopicsFromPriorSession { get; set; } = new();
        public string? LastUserMessage { get; set; }
        public string? LastAssistantResponse { get; set; }
        public DateTime? PriorSessionEndedAt { get; set; }
        public TimeSpan? TimeSinceLastSession { get; set; }
        public string FormattedContext { get; set; } = string.Empty;
    }

    public interface ICrossSessionContextService
    {
        /// <summary>
        /// Ambil konteks dari sesi terakhir user (last 3 turns).
        /// Dipanggil saat user buka sesi baru (isFirstMessage = true).
        /// </summary>
        Task<CrossSessionContext> GetPriorSessionContextAsync(string userId, int maxTurns = 3);

        /// <summary>
        /// Update LastSessionId setelah user selesai sesi.
        /// </summary>
        Task UpdateLastSessionAsync(string userId, string sessionId);

        /// <summary>
        /// Generate personalized greeting untuk returning user.
        /// </summary>
        Task<string> GenerateReturnGreetingAsync(CrossSessionContext priorContext, string userLanguage = "id");
    }

    public class CrossSessionContextService : ICrossSessionContextService
    {
        private readonly IDbContextFactory<JIFAS_AssistantContext> _dbFactory;
        private readonly IConversationIntelligenceService _conversationIntelligence;
        private readonly ICacheService _cache;
        private readonly ILoggerService _logger;

        private const int DEFAULT_MAX_TURNS = 3;
        private const int CONTEXT_CACHE_MINUTES = 30;
        private const string CACHE_PREFIX = "CrossSession_";

        public CrossSessionContextService(
            IDbContextFactory<JIFAS_AssistantContext> dbFactory,
            IConversationIntelligenceService conversationIntelligence,
            ICacheService cache,
            ILoggerService logger)
        {
            _dbFactory = dbFactory;
            _conversationIntelligence = conversationIntelligence;
            _cache = cache;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<CrossSessionContext> GetPriorSessionContextAsync(string userId, int maxTurns = DEFAULT_MAX_TURNS)
        {
            var result = new CrossSessionContext();

            if (string.IsNullOrWhiteSpace(userId) || userId == "anonymous")
                return result;

            try
            {
                // Check cache first
                var cacheKey = CACHE_PREFIX + userId;
                var cached = _cache.Get<CrossSessionContext>(cacheKey);
                if (cached != null && cached.HasPriorSession)
                {
                    _logger.LogDebug($"[CrossSession] Cache HIT for user: {userId}");
                    return cached;
                }

                // Get LastSessionId from UserMemory
                await using var db = await _dbFactory.CreateDbContextAsync();
                var userMemory = await db.UserMemories
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.UserId == userId);

                if (userMemory == null || string.IsNullOrWhiteSpace(userMemory.LastSessionId))
                {
                    _logger.LogDebug($"[CrossSession] No prior session for user: {userId}");
                    return result;
                }

                // Get history from the last session
                var priorHistory = await db.ChatHistories
                    .Where(h => h.SessionId == userMemory.LastSessionId)
                    .OrderByDescending(h => h.CreatedAt)
                    .Take(maxTurns)
                    .ToListAsync();

                if (priorHistory.Count == 0)
                    return result;

                // Filter out [Session Greeting] placeholder — it's not a real user message.
                var realHistory = priorHistory
                    .Where(h => h.UserMessage != "[Session Greeting]")
                    .OrderBy(h => h.CreatedAt)
                    .ToList();

                if (realHistory.Count == 0)
                    return result;

                // Primary reference: the first question that started the topic (not a follow-up).
                var firstQuestion = realHistory.First();
                // Secondary reference: the most recent question (for time/context).
                var lastEntry = realHistory.Last();

                result.HasPriorSession = true;
                result.PriorSessionId = userMemory.LastSessionId;
                result.PriorSessionEndedAt = realHistory.Max(h => h.CreatedAt);
                result.TimeSinceLastSession = DateTime.UtcNow - result.PriorSessionEndedAt;

                // LastUserMessage = initiating question, not a follow-up like "jelaskan lebih detail".
                result.LastUserMessage = firstQuestion.UserMessage;
                result.LastAssistantResponse = TruncateResponse(lastEntry.AiResponse, 300);

                result.PriorTurns = realHistory
                    .Select(h => new ConversationTurn
                    {
                        UserMessage = h.UserMessage,
                        AssistantResponse = TruncateResponse(h.AiResponse, 300),
                        Timestamp = h.CreatedAt,
                        Topic = _conversationIntelligence.ExtractTopic(h.UserMessage) ?? "General"
                    })
                    .ToList();

                result.TopicsFromPriorSession = result.PriorTurns
                    .Select(t => t.Topic)
                    .Where(t => !string.IsNullOrEmpty(t) && t != "General")
                    .Distinct()
                    .ToList();

                result.FormattedContext = FormatCrossSessionContext(result);

                _cache.Set(cacheKey, result, CONTEXT_CACHE_MINUTES);

                _logger.LogInformation(
                    $"[CrossSession] Loaded context for {userId}: " +
                    $"Session={result.PriorSessionId}, Turns={result.PriorTurns.Count}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[CrossSession] Error loading prior context for {userId}: {ex.Message}");
            }

            return result;
        }

        /// <inheritdoc />
        public async Task UpdateLastSessionAsync(string userId, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(userId) || userId == "anonymous")
                return;

            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var memory = await db.UserMemories.FirstOrDefaultAsync(m => m.UserId == userId);

                if (memory != null)
                {
                    memory.LastSessionId = sessionId;
                    memory.LastSessionAt = DateTime.UtcNow;
                    memory.LastSeenAt = DateTime.UtcNow;
                    memory.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();

                    _cache.Remove(CACHE_PREFIX + userId);
                    _logger.LogDebug($"[CrossSession] Updated LastSessionId for {userId}: {sessionId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[CrossSession] Error updating LastSessionId for {userId}: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public Task<string> GenerateReturnGreetingAsync(CrossSessionContext priorContext, string userLanguage = "id")
        {
            if (!priorContext.HasPriorSession)
                return Task.FromResult(string.Empty);

            var timeSince = priorContext.TimeSinceLastSession;
            var lastTopic = priorContext.TopicsFromPriorSession.LastOrDefault() ?? "topik sebelumnya";
            var timeContext = timeSince.HasValue ? FormatTimeSpan(timeSince.Value) : "beberapa waktu lalu";

            string greeting;
            if (userLanguage == "en")
            {
                greeting = $"Welcome back! Last time we discussed **{lastTopic}** ({timeContext}). How can I help you today?";
            }
            else
            {
                greeting = $"Selamat datang kembali! 👋 Kita terakhir membahas tentang **{lastTopic}** ({timeContext}). " +
                           $"Ada yang ingin dilanjut atau ada topik baru?";
            }

            if (!string.IsNullOrEmpty(priorContext.LastUserMessage))
            {
                var truncated = priorContext.LastUserMessage.Length > 60
                    ? priorContext.LastUserMessage[..60] + "..."
                    : priorContext.LastUserMessage;

                if (priorContext.PriorTurns.Count > 1)
                {
                    if (userLanguage == "en")
                        greeting += $"\n\nYour last session started with: \"{truncated}\"";
                    else
                        greeting += $"\n\nTopik terakhir kamu: \"{truncated}\"";
                }
            }

            return Task.FromResult(greeting);
        }

        private string FormatCrossSessionContext(CrossSessionContext context)
        {
            if (!context.HasPriorSession || context.PriorTurns.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("│          KONTEKS DARI SESI SEBELUMNYA                        │");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            if (context.TimeSinceLastSession.HasValue)
                sb.AppendLine($"📅 Sesi sebelumnya: {FormatTimeSpan(context.TimeSinceLastSession.Value)}");

            if (context.TopicsFromPriorSession.Count > 0)
                sb.AppendLine($"💬 Topik yang dibahas: {string.Join(" → ", context.TopicsFromPriorSession)}");

            sb.AppendLine();
            sb.AppendLine("--- Percakapan terakhir (sebelum sesi ini) ---");

            foreach (var turn in context.PriorTurns.TakeLast(3))
            {
                sb.AppendLine();
                sb.AppendLine($"👤 User: {turn.UserMessage}");
                sb.AppendLine($"🤖 AI: {turn.AssistantResponse}");
            }

            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine("📌 INSTRUKSI: User saat ini kemungkinan ingin:");
            sb.AppendLine("   1. Melanjutkan topik yang sama dari sesi sebelumnya");
            sb.AppendLine("   2. Bertanya klarifikasi dari jawaban terakhir");
            sb.AppendLine("   3. Memulai topik baru");
            sb.AppendLine();
            sb.AppendLine("   → Jika user merujuk ke 'itu', 'tadi', 'sebelumnya' → konteks di atas");
            sb.AppendLine("   → Jika user memulai dengan topik berbeda → abaikan konteks");

            return sb.ToString();
        }

        private string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalMinutes < 60)
                return $"{(int)ts.TotalMinutes} menit yang lalu";
            if (ts.TotalHours < 24)
                return $"{(int)ts.TotalHours} jam yang lalu";
            if (ts.TotalDays < 7)
                return $"{(int)ts.TotalDays} hari yang lalu";
            return $"{(int)(ts.TotalDays / 7)} minggu yang lalu";
        }

        private static string TruncateResponse(string response, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(response))
                return string.Empty;
            if (response.Length <= maxLength)
                return response;

            var truncated = response[..maxLength];
            var lastPeriod = truncated.LastIndexOf('.');
            var breakPoint = lastPeriod > maxLength * 0.6 ? lastPeriod + 1 : maxLength;
            return truncated[..breakPoint].Trim() + "…";
        }
    }
}
