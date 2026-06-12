using Jifas.Assistant.Services;
using jifas_assistant.DAL.Models;

namespace Jifas.Assistant.Tests;

public class AiLearningPolicyTests
{
    [Fact]
    public void Evaluate_SkipsTicketFlow()
    {
        var chat = SuccessfulChat(
            "Buat tiket tombol approve invoice tidak bisa diklik",
            "Saya akan bantu buatkan tiket.",
            source: "Ticket Flow",
            confidence: 0.9,
            isKb: false);

        var result = AiLearningPolicy.Evaluate(chat, frequency: 3);

        Assert.False(result.ShouldCreate);
        Assert.True(result.ShouldSkip);
        Assert.Equal("system-flow", result.Reason);
    }

    [Fact]
    public void Evaluate_CreatesCandidate_ForHighQualityKbAnswer()
    {
        var chat = SuccessfulChat(
            "Apa itu JIFAS?",
            LongAnswer("JIFAS adalah sistem finance dan accounting terintegrasi untuk approval, pencatatan, payment, dan laporan."),
            source: "JIFAS (5 hasil)",
            confidence: 0.86,
            isKb: true);

        var result = AiLearningPolicy.Evaluate(chat, frequency: 1);

        Assert.True(result.ShouldCreate);
        Assert.False(result.ShouldSkip);
        Assert.True(result.QualityScore >= 0.7);
        Assert.Contains("jawaban KB", result.Reason);
    }

    [Fact]
    public void Evaluate_FlagsFalseOutOfScope_ForJifasHistoryApprovalQuestion()
    {
        var chat = SuccessfulChat(
            "History approval invoice ada di mana?",
            LongAnswer("Maaf, saya hanya bisa membantu pertanyaan terkait JIFAS."),
            source: "Out of Scope",
            confidence: 0.35,
            isKb: false);

        var result = AiLearningPolicy.Evaluate(chat, frequency: 1);

        Assert.True(result.ShouldCreate);
        Assert.Contains("PossibleFalseOutOfScope", result.Flags);
    }

    [Fact]
    public void Evaluate_FlagsSensitiveData_ButKeepsAuditCandidate()
    {
        var chat = SuccessfulChat(
            "Invoice INV-20260608-001 atas email finance@example.com error",
            LongAnswer("Untuk kasus dokumen spesifik, cek status invoice dan hubungi IT jika akses approval tidak muncul."),
            source: "JIFAS (5 hasil)",
            confidence: 0.8,
            isKb: true);

        var result = AiLearningPolicy.Evaluate(chat, frequency: 1);

        Assert.True(result.ShouldCreate);
        Assert.True(result.ContainsSensitiveData);
        Assert.Contains("SensitiveReviewRequired", result.Flags);
    }

    [Fact]
    public void Evaluate_SkipsInvalidInputAnswer()
    {
        var chat = SuccessfulChat(
            "Apa itu JIFAS?",
            "Invalid message format. Pesan tidak valid.",
            source: "Validation",
            confidence: 0.5,
            isKb: false);

        var result = AiLearningPolicy.Evaluate(chat, frequency: 1);

        Assert.False(result.ShouldCreate);
        Assert.True(result.ShouldSkip);
        Assert.Equal("system-flow", result.Reason);
    }

    [Fact]
    public void Evaluate_SkipsGreetingAndGratitude()
    {
        var greetingChat = SuccessfulChat("Halo", "Selamat datang di JIFAS!", source: "Greeting", confidence: 0.5, isKb: false);
        Assert.False(AiLearningPolicy.Evaluate(greetingChat).ShouldCreate);

        var gratitudeChat = SuccessfulChat("Terima kasih", "Sama-sama!", source: "Gratitude", confidence: 0.5, isKb: false);
        Assert.False(AiLearningPolicy.Evaluate(gratitudeChat).ShouldCreate);
    }

    [Fact]
    public void Evaluate_SkipsFailedChat()
    {
        var chat = new ChatHistory
        {
            Id = 1,
            UserMessage = "Apa itu JIFAS?",
            AiResponse = "Error occurred",
            ResponseSource = "AI",
            ConfidenceScore = 0.5,
            IsFromKnowledgeBase = false,
            Success = false
        };

        var result = AiLearningPolicy.Evaluate(chat);

        Assert.False(result.ShouldCreate);
        Assert.True(result.ShouldSkip);
        Assert.Equal("chat-failed", result.Reason);
    }

    [Fact]
    public void Evaluate_CreatesCandidate_ForRepeatedQuestion()
    {
        var chat = SuccessfulChat(
            "Bagaimana cara approve invoice?",
            LongAnswer("Untuk approve invoice, buka menu Invoice dan klik tombol approve."),
            source: "AI",
            confidence: 0.65,
            isKb: false);

        var result = AiLearningPolicy.Evaluate(chat, frequency: 3);

        Assert.True(result.ShouldCreate);
        Assert.Contains("RepeatedQuestion", result.Flags);
    }

    [Fact]
    public void Evaluate_FlagsLowConfidenceAnswer()
    {
        var chat = SuccessfulChat(
            "Apa itu JIFAS?",
            LongAnswer("JIFAS adalah sistem."),
            source: "AI",
            confidence: 0.3,
            isKb: false);

        var result = AiLearningPolicy.Evaluate(chat, frequency: 1);

        Assert.True(result.ShouldCreate);
        Assert.Contains("LowConfidence", result.Flags);
    }

    [Fact]
    public void NormalizeQuestion_RemovesPolitePrefix()
    {
        var normalized = AiLearningPolicy.NormalizeQuestion("Tolong jelaskan, apa itu JIFAS?");

        Assert.Equal("apa itu jifas", normalized);
    }

    [Fact]
    public void NormalizeQuestion_RemovesSpecialCharacters()
    {
        var normalized = AiLearningPolicy.NormalizeQuestion("Bagaimana @cara #approve invoice?");

        Assert.DoesNotContain("@", normalized);
        Assert.DoesNotContain("#", normalized);
    }

    [Fact]
    public void BuildQuestionHash_IsStable()
    {
        var hash1 = AiLearningPolicy.BuildQuestionHash("Apa itu JIFAS?");
        var hash2 = AiLearningPolicy.BuildQuestionHash("Apa itu JIFAS?");
        var hash3 = AiLearningPolicy.BuildQuestionHash("apa itu jifas?");

        Assert.Equal(hash1, hash2);
        Assert.Equal(hash1, hash3);
    }

    [Fact]
    public void BuildQuestionHash_DifferentForDifferentQuestions()
    {
        var hash1 = AiLearningPolicy.BuildQuestionHash("Apa itu JIFAS?");
        var hash2 = AiLearningPolicy.BuildQuestionHash("Apa itu Invoice?");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void IsLikelyJifasQuestion_DetectsJifasTerms()
    {
        var terms = new[]
        {
            "invoice", "payment", "pum", "approval", "report", "laporan",
            "receiving", "vendor", "coa", "menu", "halaman", "jifas"
        };

        foreach (var term in terms)
        {
            Assert.True(AiLearningPolicy.IsLikelyJifasQuestion($"Bagaimana cara {term} di JIFAS?"));
        }
    }

    [Fact]
    public void IsLikelyJifasQuestion_RejectsNonJifasQuestions()
    {
        Assert.False(AiLearningPolicy.IsLikelyJifasQuestion("Siapa presiden Indonesia?"));
        Assert.False(AiLearningPolicy.IsLikelyJifasQuestion("Cuaca hari ini bagaimana?"));
    }

    [Fact]
    public void Evaluate_CreatesCandidate_FromPositiveFeedback()
    {
        var chat = SuccessfulChat(
            "Cara buat invoice di JIFAS",
            LongAnswer("Untuk membuat invoice, buka menu Invoice dan klik tombol Baru."),
            source: "AI",
            confidence: 0.7,
            isKb: false);

        var result = AiLearningPolicy.Evaluate(chat, frequency: 1, feedbackRating: 5);

        Assert.True(result.ShouldCreate);
        Assert.Contains("PositiveFeedback", result.Flags);
    }

    private static ChatHistory SuccessfulChat(
        string question,
        string answer,
        string source,
        double confidence,
        bool isKb) => new()
    {
        Id = 1,
        SessionId = "test-session",
        UserId = "test-user",
        UserMessage = question,
        AiResponse = answer,
        ResponseSource = source,
        ConfidenceScore = confidence,
        IsFromKnowledgeBase = isKb,
        ResponseTimeMs = 1000,
        CreatedAt = DateTime.UtcNow,
        Success = true
    };

    private static string LongAnswer(string seed) =>
        seed + " " + string.Join(" ", Enumerable.Repeat("Jawaban ini sudah direview sebagai contoh knowledge base JIFAS yang cukup lengkap dan aman untuk diuji.", 10));
}
