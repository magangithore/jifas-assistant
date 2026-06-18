using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jifas.Assistant.Utilities;
using jifas_assistant.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pgvector;

namespace Jifas.Assistant.Services;

public static class LearningCandidateStatuses
{
    public const string NeedsEdit = "NeedsEdit";
    public const string ReadyForPublish = "ReadyForPublish";
    public const string Published = "Published";
    public const string Archived = "Archived";
    public const string PublishFailed = "PublishFailed";
}

public sealed class LearningCandidateQuery
{
    public string? Status { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 30;
}

public sealed class LearningCandidateEditRequest
{
    public string? EditedQuestion { get; set; }
    public string? EditedAnswer { get; set; }
    public string? Category { get; set; }
    public string? Tags { get; set; }
    public string? ReviewNotes { get; set; }
    public bool? ContainsSensitiveData { get; set; }
    public string? SensitiveReason { get; set; }
}

public sealed class LearningCandidateDto
{
    public int Id { get; set; }
    public int? SourceChatHistoryId { get; set; }
    public string QuestionHash { get; set; } = string.Empty;
    public string OriginalQuestion { get; set; } = string.Empty;
    public string OriginalAnswer { get; set; } = string.Empty;
    public string EditedQuestion { get; set; } = string.Empty;
    public string EditedAnswer { get; set; } = string.Empty;
    public string EffectiveQuestion { get; set; } = string.Empty;
    public string EffectiveAnswer { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public double? ConfidenceScore { get; set; }
    public double? QualityScore { get; set; }
    public string CandidateReason { get; set; } = string.Empty;
    public string Flags { get; set; } = string.Empty;
    public bool ContainsSensitiveData { get; set; }
    public string SensitiveReason { get; set; } = string.Empty;
    public int Frequency { get; set; }
    public string ReviewNotes { get; set; } = string.Empty;
    public string ReviewedBy { get; set; } = string.Empty;
    public int? PublishedDocumentId { get; set; }
    public string PublishedBy { get; set; } = string.Empty;
    public string PublishError { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
}

public sealed class LearningCandidateListResult
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<LearningCandidateDto> Items { get; set; } = new();
}

public sealed class LearningStatsDto
{
    public int Total { get; set; }
    public int NeedsEdit { get; set; }
    public int ReadyForPublish { get; set; }
    public int Published { get; set; }
    public int Archived { get; set; }
    public int PublishFailed { get; set; }
    public int PublishedToday { get; set; }
    public double AverageQualityScore { get; set; }
    public List<LearningQuestionStatDto> TopRepeatedQuestions { get; set; } = new();
    public List<LearningQuestionStatDto> LowConfidenceTopics { get; set; } = new();
}

public sealed class LearningQuestionStatDto
{
    public int CandidateId { get; set; }
    public string Question { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Frequency { get; set; }
    public double? ConfidenceScore { get; set; }
}

public sealed class LearningCollectionResult
{
    public int Scanned { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
}

public sealed class LearningPublishRunResult
{
    public int Attempted { get; set; }
    public int Published { get; set; }
    public int Failed { get; set; }
    public List<string> Messages { get; set; } = new();
}

public sealed class CandidateEvaluation
{
    public bool ShouldCreate { get; set; }
    public bool ShouldSkip { get; set; }
    public bool ContainsSensitiveData { get; set; }
    public string SensitiveReason { get; set; } = string.Empty;
    public double QualityScore { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<string> Flags { get; set; } = new();
}

public static class AiLearningPolicy
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex UnsafeCharsRegex = new(@"[^\p{L}\p{N}\s\-_/]", RegexOptions.Compiled);
    private static readonly Regex SensitiveRegex = new(
        @"\b(password|token|api\s*key|secret|npwp|rekening|account\s*number|email|nomor\s*dokumen|doc\s*no|invoice\s*number)\b|[A-Z]{2,5}[-/]\d{2,}|\b\d{8,}\b|[\w\.-]+@[\w\.-]+\.\w+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static CandidateEvaluation Evaluate(ChatHistory chat, int frequency = 1, int? feedbackRating = null)
    {
        var result = new CandidateEvaluation();
        var question = chat.UserMessage ?? string.Empty;
        var answer = chat.AiResponse ?? string.Empty;
        var source = chat.ResponseSource ?? string.Empty;

        if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(answer))
            return Skip("empty-chat");

        if (!chat.Success)
            return Skip("chat-failed");

        if (IsTicketOrSystemFlow(source) || IsInvalidInputAnswer(answer))
            return Skip("system-flow");

        var isOutOfScope = source.Contains("Out of Scope", StringComparison.OrdinalIgnoreCase);
        var possibleFalseOutOfScope = isOutOfScope && IsLikelyJifasQuestion(question);
        if (isOutOfScope && !possibleFalseOutOfScope && feedbackRating is null or > 2)
            return Skip("out-of-scope");

        var confidence = chat.ConfidenceScore ?? 0;
        var answerLengthScore = Math.Clamp(answer.Length / 1500.0, 0.15, 1.0);
        var confidenceScore = Math.Clamp(confidence, 0, 1);
        var kbScore = chat.IsFromKnowledgeBase ? 1.0 : 0.35;
        result.QualityScore = Math.Round((confidenceScore * 0.45) + (answerLengthScore * 0.25) + (kbScore * 0.30), 3);

        if (possibleFalseOutOfScope)
            result.Flags.Add("PossibleFalseOutOfScope");
        if (confidence > 0 && confidence < 0.5)
            result.Flags.Add("LowConfidence");
        if (!chat.IsFromKnowledgeBase)
            result.Flags.Add("WeakSource");
        if (answer.Length < 220)
            result.Flags.Add("TooShort");
        if (frequency >= 2)
            result.Flags.Add("RepeatedQuestion");
        if (feedbackRating >= 4)
            result.Flags.Add("PositiveFeedback");
        if (feedbackRating <= 2)
            result.Flags.Add("NeedsCorrectionFromFeedback");

        var sensitive = DetectSensitiveData(question + "\n" + answer);
        result.ContainsSensitiveData = sensitive.containsSensitive;
        result.SensitiveReason = sensitive.reason;
        if (result.ContainsSensitiveData)
            result.Flags.Add("SensitiveReviewRequired");

        var highQuality = chat.IsFromKnowledgeBase && confidence >= 0.7 && answer.Length >= 300;
        var feedbackDriven = feedbackRating.HasValue;
        var faqLike = IsPotentialFaq(question);
        result.ShouldCreate = highQuality || feedbackDriven || frequency >= 2 || possibleFalseOutOfScope || faqLike;

        result.Reason = BuildReason(highQuality, feedbackDriven, frequency, possibleFalseOutOfScope, faqLike);
        return result.ShouldCreate ? result : Skip("not-learning-material");

        CandidateEvaluation Skip(string reason) => new()
        {
            ShouldSkip = true,
            ShouldCreate = false,
            Reason = reason,
            QualityScore = 0
        };
    }

    public static string NormalizeQuestion(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
            return string.Empty;

        var normalized = question.ToLowerInvariant().Trim();
        normalized = normalized.Replace("saya ingin tahu", string.Empty)
            .Replace("tolong jelaskan", string.Empty)
            .Replace("mau tahu", string.Empty)
            .Trim();
        normalized = UnsafeCharsRegex.Replace(normalized, " ");
        normalized = WhitespaceRegex.Replace(normalized, " ");
        return normalized.Trim();
    }

    public static string BuildQuestionHash(string question) =>
        HashHelper.ToStableHash(NormalizeQuestion(question));

    public static bool IsLikelyJifasQuestion(string question)
    {
        var q = question.ToLowerInvariant();
        var terms = new[]
        {
            "jifas", "invoice", "payment", "pum", "cashbank", "budget", "approval",
            "approve", "report", "laporan", "receiving", "vendor", "coa", "journal",
            "posting", "tax", "pajak", "history", "riwayat", "menu", "halaman"
        };
        return terms.Any(q.Contains);
    }

    private static bool IsTicketOrSystemFlow(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        return source.Contains("Ticket", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("Greeting", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("Gratitude", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("Command", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInvalidInputAnswer(string answer) =>
        answer.Contains("Invalid message format", StringComparison.OrdinalIgnoreCase) ||
        answer.Contains("format pesan tidak valid", StringComparison.OrdinalIgnoreCase);

    private static bool IsPotentialFaq(string question)
    {
        var q = question.ToLowerInvariant();
        return q.Contains("apa itu") ||
            q.Contains("cara ") ||
            q.Contains("bagaimana") ||
            q.Contains("kenapa") ||
            q.Contains("di mana") ||
            q.Contains("fungsi") ||
            q.Contains("perbedaan");
    }

    public static (bool containsSensitive, string reason) DetectSensitiveData(string text)
    {
        var matches = SensitiveRegex.Matches(text);
        if (!matches.Any())
            return (false, string.Empty);

        var uniquePatterns = matches.Select(m => m.Value).Distinct().Take(3).ToList();
        var reason = uniquePatterns.Count == 1
            ? $"Terdeteksi pola sensitif: {uniquePatterns.First()}"
            : $"Terdeteksi {matches.Count} pola sensitif: {string.Join(", ", uniquePatterns)}";
        return (true, reason);
    }

    private static string BuildReason(bool highQuality, bool feedbackDriven, int frequency, bool possibleFalseOutOfScope, bool faqLike)
    {
        var reasons = new List<string>();
        if (highQuality) reasons.Add("jawaban KB berkualitas tinggi");
        if (feedbackDriven) reasons.Add("dipicu feedback user/admin");
        if (frequency >= 2) reasons.Add($"pertanyaan berulang {frequency}x");
        if (possibleFalseOutOfScope) reasons.Add("kemungkinan false out-of-scope");
        if (faqLike) reasons.Add("format pertanyaan cocok menjadi FAQ");
        return reasons.Count == 0 ? "kandidat pembelajaran umum" : string.Join(", ", reasons);
    }
}

public interface IAiLearningService
{
    Task<LearningStatsDto> GetStatsAsync(CancellationToken cancellationToken = default);
    Task<LearningCandidateListResult> GetCandidatesAsync(LearningCandidateQuery query, CancellationToken cancellationToken = default);
    Task<LearningCandidateDto?> GetCandidateAsync(int id, CancellationToken cancellationToken = default);
    Task<LearningCandidateDto?> UpdateCandidateAsync(int id, LearningCandidateEditRequest request, string actor, CancellationToken cancellationToken = default);
    Task<LearningCandidateDto?> MarkReadyAsync(int id, string actor, CancellationToken cancellationToken = default);
    Task<LearningCandidateDto?> ArchiveAsync(int id, string actor, string? notes = null, CancellationToken cancellationToken = default);
    Task<LearningCollectionResult> CollectCandidatesAsync(int scanLimit = 250, CancellationToken cancellationToken = default);
    Task<LearningCandidateDto?> CreateCandidateFromFeedbackAsync(int chatHistoryId, int rating, string? comment, CancellationToken cancellationToken = default);
    Task<LearningPublishRunResult> PublishReadyAsync(int? limit = null, string actor = "scheduler", CancellationToken cancellationToken = default);
}

public class AiLearningService : IAiLearningService
{
    private const int DefaultChunkSize = 700;
    private const int DefaultChunkOverlapWords = 50;

    private readonly IDbContextFactory<JIFAS_AssistantContext> _dbFactory;
    private readonly IEmbeddingService _embeddingService;
    private readonly ICacheService _cacheService;
    private readonly ILoggerService _logger;
    private readonly IConfiguration _configuration;

    public AiLearningService(
        IDbContextFactory<JIFAS_AssistantContext> dbFactory,
        IEmbeddingService embeddingService,
        ICacheService cacheService,
        ILoggerService logger,
        IConfiguration configuration)
    {
        _dbFactory = dbFactory;
        _embeddingService = embeddingService;
        _cacheService = cacheService;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<LearningStatsDto> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await db.LearningCandidates.AsNoTracking().ToListAsync(cancellationToken);
        var today = DateTime.UtcNow.Date;

        var qualityScores = candidates
            .Where(c => c.QualityScore.HasValue)
            .Select(c => c.QualityScore!.Value)
            .ToList();

        return new LearningStatsDto
        {
            Total = candidates.Count,
            NeedsEdit = candidates.Count(c => c.Status == LearningCandidateStatuses.NeedsEdit),
            ReadyForPublish = candidates.Count(c => c.Status == LearningCandidateStatuses.ReadyForPublish),
            Published = candidates.Count(c => c.Status == LearningCandidateStatuses.Published),
            Archived = candidates.Count(c => c.Status == LearningCandidateStatuses.Archived),
            PublishFailed = candidates.Count(c => c.Status == LearningCandidateStatuses.PublishFailed),
            PublishedToday = candidates.Count(c => c.PublishedAt.HasValue && c.PublishedAt.Value.Date == today),
            AverageQualityScore = qualityScores.Count == 0 ? 0 : Math.Round(qualityScores.Average(), 3),
            TopRepeatedQuestions = candidates
                .OrderByDescending(c => c.Frequency)
                .ThenByDescending(c => c.UpdatedAt)
                .Take(5)
                .Select(ToQuestionStat)
                .ToList(),
            LowConfidenceTopics = candidates
                .Where(c => (c.ConfidenceScore ?? 1) <= 0.5)
                .OrderBy(c => c.ConfidenceScore ?? 0)
                .ThenByDescending(c => c.UpdatedAt)
                .Take(5)
                .Select(ToQuestionStat)
                .ToList()
        };
    }

    public async Task<LearningCandidateListResult> GetCandidatesAsync(LearningCandidateQuery query, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var candidates = db.LearningCandidates.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            candidates = candidates.Where(c => c.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLower();
            candidates = candidates.Where(c =>
                c.OriginalQuestion.ToLower().Contains(search) ||
                c.EditedQuestion.ToLower().Contains(search) ||
                c.Category.ToLower().Contains(search) ||
                c.Tags.ToLower().Contains(search));
        }

        var total = await candidates.CountAsync(cancellationToken);
        var items = await candidates
            .OrderByDescending(c => c.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => ToDto(c))
            .ToListAsync(cancellationToken);

        return new LearningCandidateListResult
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = items
        };
    }

    public async Task<LearningCandidateDto?> GetCandidateAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var candidate = await db.LearningCandidates.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return candidate == null ? null : ToDto(candidate);
    }

    public async Task<LearningCandidateDto?> UpdateCandidateAsync(int id, LearningCandidateEditRequest request, string actor, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var candidate = await db.LearningCandidates.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (candidate == null)
            return null;

        var oldStatus = candidate.Status;
        if (request.EditedQuestion != null)
            candidate.EditedQuestion = request.EditedQuestion.Trim();
        if (request.EditedAnswer != null)
            candidate.EditedAnswer = request.EditedAnswer.Trim();
        if (!string.IsNullOrWhiteSpace(request.Category))
            candidate.Category = request.Category.Trim();
        if (!string.IsNullOrWhiteSpace(request.Tags))
            candidate.Tags = request.Tags.Trim();
        if (request.ReviewNotes != null)
            candidate.ReviewNotes = request.ReviewNotes.Trim();
        if (request.ContainsSensitiveData.HasValue)
            candidate.ContainsSensitiveData = request.ContainsSensitiveData.Value;
        if (request.SensitiveReason != null)
            candidate.SensitiveReason = request.SensitiveReason.Trim();

        candidate.ReviewedBy = actor;
        candidate.ReviewedAt = DateTime.UtcNow;
        candidate.UpdatedAt = DateTime.UtcNow;
        candidate.PublishError = string.Empty;

        await AddAuditAsync(db, candidate.Id, "Edit", actor, oldStatus, candidate.Status, "Candidate edited", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(candidate);
    }

    public async Task<LearningCandidateDto?> MarkReadyAsync(int id, string actor, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var candidate = await db.LearningCandidates.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (candidate == null)
            return null;

        var finalQuestion = GetEffectiveQuestion(candidate);
        var finalAnswer = GetEffectiveAnswer(candidate);

        if (candidate.ContainsSensitiveData)
            throw new InvalidOperationException("Candidate masih bertanda sensitif. Edit dan bersihkan data sensitif sebelum publish.");

        var finalSensitive = AiLearningPolicy.DetectSensitiveData(finalQuestion + "\n" + finalAnswer);
        if (finalSensitive.containsSensitive)
        {
            candidate.ContainsSensitiveData = true;
            candidate.SensitiveReason = finalSensitive.reason;
            candidate.Flags = MergeCsv(candidate.Flags, new[] { "SensitiveReviewRequired" });
            candidate.UpdatedAt = DateTime.UtcNow;
            await AddAuditAsync(db, candidate.Id, "SensitiveBlocked", actor, candidate.Status, candidate.Status, finalSensitive.reason, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException($"Jawaban final masih mengandung data sensitif. {finalSensitive.reason}");
        }

        if (string.IsNullOrWhiteSpace(finalQuestion) || finalQuestion.Length < 8)
            throw new InvalidOperationException("Pertanyaan final terlalu pendek.");
        if (string.IsNullOrWhiteSpace(finalAnswer) || finalAnswer.Length < 120)
            throw new InvalidOperationException("Jawaban final terlalu pendek untuk dijadikan Knowledge Base.");

        var oldStatus = candidate.Status;
        candidate.Status = LearningCandidateStatuses.ReadyForPublish;
        candidate.ReviewedBy = actor;
        candidate.ReviewedAt = DateTime.UtcNow;
        candidate.UpdatedAt = DateTime.UtcNow;
        candidate.PublishError = string.Empty;

        await AddAuditAsync(db, candidate.Id, "ReadyForPublish", actor, oldStatus, candidate.Status, "Approved for scheduled publish", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(candidate);
    }

    public async Task<LearningCandidateDto?> ArchiveAsync(int id, string actor, string? notes = null, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var candidate = await db.LearningCandidates.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (candidate == null)
            return null;

        var oldStatus = candidate.Status;
        candidate.Status = LearningCandidateStatuses.Archived;
        candidate.ReviewedBy = actor;
        candidate.ReviewedAt = DateTime.UtcNow;
        candidate.UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(notes))
            candidate.ReviewNotes = notes.Trim();

        await AddAuditAsync(db, candidate.Id, "Archive", actor, oldStatus, candidate.Status, notes ?? "Archived by admin", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(candidate);
    }

    public async Task<LearningCollectionResult> CollectCandidatesAsync(int scanLimit = 250, CancellationToken cancellationToken = default)
    {
        var result = new LearningCollectionResult();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var since = DateTime.UtcNow.AddDays(-30);

        var chats = await db.ChatHistories
            .AsNoTracking()
            .Where(c => c.CreatedAt >= since)
            .OrderByDescending(c => c.CreatedAt)
            .Take(Math.Clamp(scanLimit, 10, 1000))
            .ToListAsync(cancellationToken);

        result.Scanned = chats.Count;

        var frequencies = chats
            .GroupBy(c => AiLearningPolicy.NormalizeQuestion(c.UserMessage))
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var chat in chats)
        {
            var normalized = AiLearningPolicy.NormalizeQuestion(chat.UserMessage);
            var frequency = frequencies.TryGetValue(normalized, out var count) ? count : 1;
            var evaluation = AiLearningPolicy.Evaluate(chat, frequency);
            if (!evaluation.ShouldCreate)
            {
                result.Skipped++;
                continue;
            }

            var saved = await CreateOrUpdateCandidateAsync(db, chat, evaluation, frequency, "collector", cancellationToken);
            if (saved.created)
                result.Created++;
            else
                result.Updated++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<LearningCandidateDto?> CreateCandidateFromFeedbackAsync(int chatHistoryId, int rating, string? comment, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var chat = await db.ChatHistories.FirstOrDefaultAsync(c => c.Id == chatHistoryId, cancellationToken);
        if (chat == null)
            return null;

        var frequency = await EstimateFrequencyAsync(db, chat.UserMessage, cancellationToken);
        var evaluation = AiLearningPolicy.Evaluate(chat, frequency, rating);
        if (!evaluation.ShouldCreate)
            return null;

        if (!string.IsNullOrWhiteSpace(comment))
        {
            evaluation.Flags.Add(rating <= 2 ? "AdminCorrectionNeeded" : "FeedbackNote");
            evaluation.Reason = $"{evaluation.Reason}; feedback: {comment.Trim()}";
        }

        var saved = await CreateOrUpdateCandidateAsync(db, chat, evaluation, frequency, "feedback", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(saved.candidate);
    }

    public async Task<LearningPublishRunResult> PublishReadyAsync(int? limit = null, string actor = "scheduler", CancellationToken cancellationToken = default)
    {
        var result = new LearningPublishRunResult();
        var maxPublish = Math.Clamp(limit ?? _configuration.GetValue<int?>("AiLearning:MaxPublishPerRun") ?? 10, 1, 50);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await db.LearningCandidates
            .Where(c => c.Status == LearningCandidateStatuses.ReadyForPublish)
            .OrderBy(c => c.UpdatedAt)
            .Take(maxPublish)
            .ToListAsync(cancellationToken);

        result.Attempted = candidates.Count;

        foreach (var candidate in candidates)
        {
            try
            {
                var documentId = await PublishCandidateAsync(db, candidate, actor, cancellationToken);
                candidate.Status = LearningCandidateStatuses.Published;
                candidate.PublishedDocumentId = documentId;
                candidate.PublishedBy = actor;
                candidate.PublishedAt = DateTime.UtcNow;
                candidate.UpdatedAt = DateTime.UtcNow;
                candidate.PublishError = string.Empty;
                await AddAuditAsync(db, candidate.Id, "Publish", actor, LearningCandidateStatuses.ReadyForPublish, candidate.Status, $"Published to KB document {documentId}", cancellationToken);
                result.Published++;
                result.Messages.Add($"Candidate {candidate.Id} published as KB document {documentId}.");
            }
            catch (Exception ex)
            {
                candidate.Status = LearningCandidateStatuses.PublishFailed;
                candidate.PublishError = ex.Message;
                candidate.UpdatedAt = DateTime.UtcNow;
                await AddAuditAsync(db, candidate.Id, "PublishFailed", actor, LearningCandidateStatuses.ReadyForPublish, candidate.Status, ex.Message, cancellationToken);
                result.Failed++;
                result.Messages.Add($"Candidate {candidate.Id} failed: {ex.Message}");
                _logger.LogError($"[AiLearning] Publish failed for candidate {candidate.Id}: {ex.Message}", ex);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        if (result.Published > 0)
            _cacheService.Clear();
        return result;
    }

    private async Task<(LearningCandidate candidate, bool created)> CreateOrUpdateCandidateAsync(
        JIFAS_AssistantContext db,
        ChatHistory chat,
        CandidateEvaluation evaluation,
        int frequency,
        string actor,
        CancellationToken cancellationToken)
    {
        var hash = AiLearningPolicy.BuildQuestionHash(chat.UserMessage);
        var existing = await db.LearningCandidates.FirstOrDefaultAsync(c => c.QuestionHash == hash, cancellationToken);
        if (existing != null)
        {
            existing.Frequency = Math.Max(existing.Frequency, frequency);
            existing.LastSeenAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.QualityScore = Math.Max(existing.QualityScore ?? 0, evaluation.QualityScore);
            existing.Flags = MergeCsv(existing.Flags, evaluation.Flags);
            if (string.IsNullOrWhiteSpace(existing.CandidateReason))
                existing.CandidateReason = evaluation.Reason;

            await AddAuditAsync(db, existing.Id, "SeenAgain", actor, existing.Status, existing.Status, $"Frequency updated to {existing.Frequency}", cancellationToken);
            return (existing, false);
        }

        var candidate = new LearningCandidate
        {
            SourceChatHistoryId = chat.Id,
            QuestionHash = hash,
            OriginalQuestion = chat.UserMessage,
            OriginalAnswer = chat.AiResponse,
            Status = LearningCandidateStatuses.NeedsEdit,
            Category = InferCategory(chat),
            Tags = BuildDefaultTags(hash, chat),
            Source = chat.ResponseSource ?? string.Empty,
            SourceDocumentIds = chat.UsedDocumentIds ?? string.Empty,
            ConfidenceScore = chat.ConfidenceScore,
            QualityScore = evaluation.QualityScore,
            CandidateReason = evaluation.Reason,
            Flags = string.Join(",", evaluation.Flags.Distinct(StringComparer.OrdinalIgnoreCase)),
            ContainsSensitiveData = evaluation.ContainsSensitiveData,
            SensitiveReason = evaluation.SensitiveReason,
            Frequency = Math.Max(1, frequency),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };

        db.LearningCandidates.Add(candidate);
        await db.SaveChangesAsync(cancellationToken);
        await AddAuditAsync(db, candidate.Id, "Create", actor, string.Empty, candidate.Status, evaluation.Reason, cancellationToken);
        return (candidate, true);
    }

    private async Task<int> PublishCandidateAsync(JIFAS_AssistantContext db, LearningCandidate candidate, string actor, CancellationToken cancellationToken)
    {
        if (candidate.ContainsSensitiveData)
            throw new InvalidOperationException("Candidate masih mengandung data sensitif.");

        var question = GetEffectiveQuestion(candidate);
        var answer = GetEffectiveAnswer(candidate);
        EnsureFinalAnswerIsSafe(candidate, question, answer);

        var finalQuestionHash = AiLearningPolicy.BuildQuestionHash(question);
        var finalLearningHashTag = BuildLearningHashTag(finalQuestionHash);

        // Preserve original hash tag jika admin mengedit pertanyaan (menggunakan normalized comparison).
        // Ini memastikan user yang tanya versi original masih bisa exact match.
        var originalLearningHashTag = BuildLearningHashTag(candidate.QuestionHash);
        var preserveOriginalHash = !string.IsNullOrWhiteSpace(candidate.EditedQuestion) &&
            AiLearningPolicy.NormalizeQuestion(candidate.EditedQuestion) != AiLearningPolicy.NormalizeQuestion(candidate.OriginalQuestion);

        var content = BuildKnowledgeContent(question, answer, candidate);

        var existingDocument = await db.KnowledgeBaseDocuments
            .Where(d => d.IsActive == true &&
                ((candidate.PublishedDocumentId.HasValue && d.Id == candidate.PublishedDocumentId.Value) ||
                 (d.Tags != null && (d.Tags.Contains(finalLearningHashTag) || d.Tags.Contains(originalLearningHashTag)))))
            .FirstOrDefaultAsync(cancellationToken);
        if (existingDocument != null)
        {
            var updatedChunks = await BuildEmbeddedChunksAsync(existingDocument.Id, content, cancellationToken);
            var oldChunks = await db.KnowledgeBaseChunks
                .Where(c => c.DocumentId == existingDocument.Id)
                .ToListAsync(cancellationToken);

            db.KnowledgeBaseChunks.RemoveRange(oldChunks);
            existingDocument.Title = BuildKnowledgeTitle(question);
            existingDocument.Content = content;
            existingDocument.Category = string.IsNullOrWhiteSpace(candidate.Category) ? "AI Learning" : candidate.Category;

            // Selalu punya final hash tag; preserve original hash jika pertanyaan diedit.
            var tagsToMerge = new List<string> { finalLearningHashTag, "ai-learning", "approved", "reviewed" };
            if (preserveOriginalHash)
                tagsToMerge.Add(originalLearningHashTag);
            existingDocument.Tags = MergeCsv(candidate.Tags, tagsToMerge);

            existingDocument.IsActive = true;
            existingDocument.UpdatedAt = DateTime.UtcNow;
            existingDocument.UpdatedBy = actor;
            existingDocument.RelevanceScore = 1.0;
            db.KnowledgeBaseChunks.AddRange(updatedChunks);
            await db.SaveChangesAsync(cancellationToken);
            KnowledgeBaseSearchService.InvalidateEmbeddingCache();
            return existingDocument.Id;
        }

        var document = new KnowledgeBaseDocuments
        {
            Title = BuildKnowledgeTitle(question),
            Content = content,
            Category = string.IsNullOrWhiteSpace(candidate.Category) ? "AI Learning" : candidate.Category,

            // Selalu punya final hash tag; preserve original hash jika pertanyaan diedit.
            Tags = preserveOriginalHash
                ? MergeCsv(candidate.Tags, new[] { finalLearningHashTag, originalLearningHashTag, "ai-learning", "approved", "reviewed" })
                : MergeCsv(candidate.Tags, new[] { finalLearningHashTag, "ai-learning", "approved", "reviewed" }),

            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = actor,
            UpdatedBy = actor,
            RelevanceScore = 1.0
        };

        db.KnowledgeBaseDocuments.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        var chunks = await BuildEmbeddedChunksAsync(document.Id, content, cancellationToken);
        db.KnowledgeBaseChunks.AddRange(chunks);
        await db.SaveChangesAsync(cancellationToken);
        KnowledgeBaseSearchService.InvalidateEmbeddingCache();
        return document.Id;
    }

    private async Task<List<KnowledgeBaseChunks>> BuildEmbeddedChunksAsync(int documentId, string content, CancellationToken cancellationToken)
    {
        var chunks = BuildChunks(documentId, content);
        var embedded = 0;
        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var embedding = await _embeddingService.GenerateEmbeddingAsFloatArrayAsync(chunk.Content, cancellationToken);
            if (embedding.Length == 0)
                continue;

            chunk.Embedding = EmbeddingSerializer.Serialize(embedding);
            chunk.EmbeddingVector = new Vector(embedding);
            chunk.EmbeddingDimensions = embedding.Length;
            embedded++;
        }

        if (embedded == 0)
        {
            throw new InvalidOperationException("Embedding gagal dibuat untuk seluruh chunk.");
        }

        return chunks;
    }

    private async Task<int> EstimateFrequencyAsync(JIFAS_AssistantContext db, string question, CancellationToken cancellationToken)
    {
        var normalized = AiLearningPolicy.NormalizeQuestion(question);
        var since = DateTime.UtcNow.AddDays(-30);
        var recentQuestions = await db.ChatHistories
            .AsNoTracking()
            .Where(c => c.CreatedAt >= since)
            .Select(c => c.UserMessage)
            .Take(1000)
            .ToListAsync(cancellationToken);

        return Math.Max(1, recentQuestions.Count(q => AiLearningPolicy.NormalizeQuestion(q) == normalized));
    }

    private static Task AddAuditAsync(
        JIFAS_AssistantContext db,
        int candidateId,
        string action,
        string actor,
        string oldStatus,
        string newStatus,
        string notes,
        CancellationToken cancellationToken)
    {
        db.LearningCandidateAuditLogs.Add(new LearningCandidateAuditLog
        {
            CandidateId = candidateId,
            Action = action,
            Actor = string.IsNullOrWhiteSpace(actor) ? "system" : actor,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        });
        return Task.CompletedTask;
    }

    private static string GetEffectiveQuestion(LearningCandidate candidate) =>
        string.IsNullOrWhiteSpace(candidate.EditedQuestion) ? candidate.OriginalQuestion : candidate.EditedQuestion;

    private static string GetEffectiveAnswer(LearningCandidate candidate) =>
        string.IsNullOrWhiteSpace(candidate.EditedAnswer) ? candidate.OriginalAnswer : candidate.EditedAnswer;

    private static string BuildLearningHashTag(string questionHash) =>
        $"learning-hash:{questionHash[..Math.Min(16, questionHash.Length)]}";

    private static void EnsureFinalAnswerIsSafe(LearningCandidate candidate, string question, string answer)
    {
        var sensitive = AiLearningPolicy.DetectSensitiveData(question + "\n" + answer);
        if (!sensitive.containsSensitive)
            return;

        candidate.ContainsSensitiveData = true;
        candidate.SensitiveReason = sensitive.reason;
        candidate.Flags = MergeCsv(candidate.Flags, new[] { "SensitiveReviewRequired" });
        throw new InvalidOperationException($"Jawaban final masih mengandung data sensitif. {sensitive.reason}");
    }

    private static LearningCandidateDto ToDto(LearningCandidate c) => new()
    {
        Id = c.Id,
        SourceChatHistoryId = c.SourceChatHistoryId,
        QuestionHash = c.QuestionHash,
        OriginalQuestion = c.OriginalQuestion,
        OriginalAnswer = c.OriginalAnswer,
        EditedQuestion = c.EditedQuestion,
        EditedAnswer = c.EditedAnswer,
        EffectiveQuestion = GetEffectiveQuestion(c),
        EffectiveAnswer = GetEffectiveAnswer(c),
        Status = c.Status,
        Category = c.Category,
        Tags = c.Tags,
        Source = c.Source,
        ConfidenceScore = c.ConfidenceScore,
        QualityScore = c.QualityScore,
        CandidateReason = c.CandidateReason,
        Flags = c.Flags,
        ContainsSensitiveData = c.ContainsSensitiveData,
        SensitiveReason = c.SensitiveReason,
        Frequency = c.Frequency,
        ReviewNotes = c.ReviewNotes,
        ReviewedBy = c.ReviewedBy,
        PublishedDocumentId = c.PublishedDocumentId,
        PublishedBy = c.PublishedBy,
        PublishError = c.PublishError,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
        ReviewedAt = c.ReviewedAt,
        PublishedAt = c.PublishedAt
    };

    private static LearningQuestionStatDto ToQuestionStat(LearningCandidate c) => new()
    {
        CandidateId = c.Id,
        Question = GetEffectiveQuestion(c),
        Status = c.Status,
        Frequency = c.Frequency,
        ConfidenceScore = c.ConfidenceScore
    };

    private static string InferCategory(ChatHistory chat)
    {
        var text = $"{chat.UserMessage} {chat.ResponseSource}".ToLowerInvariant();
        if (text.Contains("invoice")) return "Invoice";
        if (text.Contains("payment")) return "Payment";
        if (text.Contains("pum")) return "PUM";
        if (text.Contains("budget")) return "Budget";
        if (text.Contains("cashbank") || text.Contains("cash bank")) return "CashBank";
        if (text.Contains("report") || text.Contains("laporan")) return "Report";
        if (text.Contains("receiving")) return "Receiving";
        if (text.Contains("accounting") || text.Contains("journal") || text.Contains("ledger")) return "Accounting";
        return "AI Learning";
    }

    private static string BuildDefaultTags(string hash, ChatHistory chat)
    {
        var shortHash = hash[..Math.Min(16, hash.Length)];
        return MergeCsv("ai-learning,approved,reviewed", new[] { $"learning-hash:{shortHash}", InferCategory(chat).ToLowerInvariant() });
    }

    private static string MergeCsv(string existing, IEnumerable<string> additions)
    {
        var items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in (existing ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            items.Add(item);
        foreach (var item in additions.Where(a => !string.IsNullOrWhiteSpace(a)))
            items.Add(item.Trim());
        return string.Join(",", items);
    }

    private static string BuildKnowledgeTitle(string question)
    {
        var title = question.Trim();
        if (title.Length > 120)
            title = title[..117] + "...";
        return $"AI Learning - {title}";
    }

    private static string BuildKnowledgeContent(string question, string answer, LearningCandidate candidate) =>
        $@"JIFAS AI Learning Knowledge

Pertanyaan:
{question}

Jawaban resmi:
{answer}

Kategori: {candidate.Category}
Sumber awal: {candidate.Source}
Catatan review: {candidate.ReviewNotes}
";

    private static List<KnowledgeBaseChunks> BuildChunks(int documentId, string content)
    {
        var chunks = new List<KnowledgeBaseChunks>();
        var paragraphs = content.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        var chunkIndex = 0;
        var current = string.Empty;

        foreach (var paragraph in paragraphs)
        {
            if ((current + paragraph).Length > DefaultChunkSize && !string.IsNullOrWhiteSpace(current))
            {
                chunks.Add(CreateChunk(documentId, chunkIndex++, current.Trim()));
                var words = current.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                current = string.Join(" ", words.Skip(Math.Max(0, words.Length - DefaultChunkOverlapWords))) + "\n\n";
            }

            current += paragraph + "\n\n";
        }

        if (!string.IsNullOrWhiteSpace(current))
            chunks.Add(CreateChunk(documentId, chunkIndex, current.Trim()));

        return chunks;
    }

    private static KnowledgeBaseChunks CreateChunk(int documentId, int index, string content) => new()
    {
        DocumentId = documentId,
        ChunkIndex = index,
        Content = content,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}

public sealed class AiLearningSchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiLearningSchedulerService> _logger;

    public AiLearningSchedulerService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<AiLearningSchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue<bool>("AiLearning:Enabled", true))
        {
            _logger.LogInformation("[AiLearningScheduler] Disabled by configuration.");
            return;
        }

        var collectorInterval = TimeSpan.FromMinutes(Math.Max(1, _configuration.GetValue<int?>("AiLearning:CollectorIntervalMinutes") ?? 10));
        var publisherInterval = TimeSpan.FromMinutes(Math.Max(1, _configuration.GetValue<int?>("AiLearning:PublisherIntervalMinutes") ?? 15));
        var nextCollectAt = DateTime.UtcNow.AddSeconds(30);
        var nextPublishAt = DateTime.UtcNow.AddMinutes(2);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                await using var scope = _scopeFactory.CreateAsyncScope();
                var learning = scope.ServiceProvider.GetRequiredService<IAiLearningService>();

                if (now >= nextCollectAt)
                {
                    var collect = await learning.CollectCandidatesAsync(cancellationToken: stoppingToken);
                    _logger.LogInformation("[AiLearningScheduler] Collector scanned={Scanned}, created={Created}, updated={Updated}, skipped={Skipped}",
                        collect.Scanned, collect.Created, collect.Updated, collect.Skipped);
                    nextCollectAt = now.Add(collectorInterval);
                }

                if (now >= nextPublishAt)
                {
                    var publish = await learning.PublishReadyAsync(actor: "scheduler", cancellationToken: stoppingToken);
                    _logger.LogInformation("[AiLearningScheduler] Publisher attempted={Attempted}, published={Published}, failed={Failed}",
                        publish.Attempted, publish.Published, publish.Failed);
                    nextPublishAt = now.Add(publisherInterval);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AiLearningScheduler] Loop failed: {Message}", ex.Message);
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
