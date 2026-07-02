using Jifas.Assistant.Models;
using Jifas.Assistant.Services;

namespace Jifas.Assistant.Tests;

public class ScopePreGateTests
{
    // NullLoggerService for dependency-free testing
    private sealed class NullLoggerService : ILoggerService
    {
        public void LogInformation(string message, params object[] args) { }
        public void LogWarning(string message, params object[] args) { }
        public void LogError(string message, Exception? ex = null, params object[] args) { }
        public void LogDebug(string message, params object[] args) { }
        public void LogInformationWithCorrelation(string correlationId, string message, params object[] args) { }
        public void LogWarningWithCorrelation(string correlationId, string message, params object[] args) { }
        public void LogErrorWithCorrelation(string correlationId, string message, Exception? ex = null, params object[] args) { }
        public void LogAudit(string userId, string action, string details, string? correlationId = null) { }
        public void LogPerformance(string operation, long milliseconds, string? correlationId = null) { }
    }

    private static OutOfScopeDetector Create() =>
        new OutOfScopeDetector(new NoOpOllamaService(), new NullLoggerService());

    private sealed class NoOpOllamaService : IOllamaService
    {
        public int DocumentId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public double Score { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public int? ViewCount { get; set; }
        public bool IsOfficial { get; set; }
        public Task<string> CallOllamaApiAsync(string prompt, CancellationToken ct = default) =>
            Task.FromResult(string.Empty);
        public Task<string> GenerateConversationalResponseAsync(string query, List<KnowledgeBaseResult> kbResults,
            List<(string UserMessage, string AssistantResponse)> conversationHistory,
            string? activePageContext, string? userId, string? runningSummary,
            CancellationToken ct = default) =>
            Task.FromResult(string.Empty);
        public Task<string> GenerateResponseAsync(string userQuery, List<KnowledgeBaseResult> kbResults,
            string? sessionContext = null, CancellationToken ct = default) =>
            Task.FromResult(string.Empty);
        public Task<bool> IsInScopeAsync(string userQuery) =>
            Task.FromResult(true);
        public void SetCallContext(string? userId, string? sessionId, string? activeModule, string callType = "chat") { }
        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(Array.Empty<float>());
        public bool IsAvailable => true;
    }

    // =====================================================================
    // KASUS 1: OOS GAMBANG — HARUS hard-block, TIDAK lolos
    // =====================================================================

    [Theory]
    [InlineData("Bagaimana cuaca hari ini?")]
    [InlineData("Apa kabar bitcoin sekarang?")]
    [InlineData("Jelaskan resep rendang yang enak")]
    [InlineData("Siapa pemenangnya pemilu kemarin?")]
    [InlineData("Film terbaru yang bagus apa?")]
    [InlineData("Berapa suhu di jakarta hari ini?")]
    [InlineData("Tutorial belajar python untuk pemula")]
    [InlineData("Saham PT.AAA naik nggak ya?")]
    [InlineData("Jadwal penerbangan jakarta-bali apa?")]
    [InlineData("Hotel murah di bandung yang bagus?")]
    public void EvaluatePreGate_OosJelas_HardBlocks(string oosQuestion)
    {
        var detector = Create();
        var request = new ChatRequest { Message = oosQuestion };

        var result = detector.EvaluatePreGate(oosQuestion, request);

        Assert.True(result.IsHardBlocked, $"Seharusnya hard-block: {oosQuestion}");
        Assert.False(result.HasJifasSignal);
        Assert.False(result.PassThrough);
        Assert.NotEmpty(result.Reason);
    }

    [Fact]
    public void EvaluatePreGate_OosJelas_TidakPanggilOllama()
    {
        var detector = Create();
        var request = new ChatRequest { Message = "Bagaimana cuaca di jakarta?" };

        // NoOpOllamaService tidak akan pernah dipanggil
        var result = detector.EvaluatePreGate("Bagaimana cuaca di jakarta?", request);

        Assert.True(result.IsHardBlocked);
    }

    [Fact]
    public void EvaluatePreGate_OosTanpaSinyalJifas_HardBlocks()
    {
        var detector = Create();
        // "belajar coding" = OOS keyword, tidak ada istilah JIFAS
        var result = detector.EvaluatePreGate("Tips belajar coding untuk pemula dari nol", null);

        Assert.True(result.IsHardBlocked);
    }

    // =====================================================================
    // KASUS 2: JIFAS ESCAPE — HARUS lolos ke LLM pipeline
    // =====================================================================

    [Theory]
    [InlineData("Apa itu invoice di JIFAS?")]
    [InlineData("Bagaimana cara approve payment?")]
    [InlineData("Kenapa PUM saya rejected?")]
    [InlineData("Where is the budget approval menu?")]
    [InlineData("Cara submit RV di JIFAS?")]
    public void EvaluatePreGate_IstilahJifas_Escapes(string jifasQuestion)
    {
        var detector = Create();
        var request = new ChatRequest { Message = jifasQuestion };

        var result = detector.EvaluatePreGate(jifasQuestion, request);

        Assert.True(result.HasJifasSignal, $"Seharusnya lolos: {jifasQuestion}");
        Assert.False(result.IsHardBlocked);
        Assert.True(result.PassThrough);
    }

    [Fact]
    public void EvaluatePreGate_ActiveModuleTerisi_Escapes()
    {
        var detector = Create();
        var request = new ChatRequest
        {
            Message = "Apa yang harus saya lakukan di halaman ini?",
            Context = new RequestContext { ActiveModule = "Invoice" }
        };

        var result = detector.EvaluatePreGate(request.Message, request);

        Assert.True(result.HasJifasSignal);
        Assert.False(result.IsHardBlocked);
        Assert.True(result.PassThrough);
    }

    [Fact]
    public void EvaluatePreGate_CurrentModuleTerisi_Escapes()
    {
        var detector = Create();
        var request = new ChatRequest
        {
            Message = "Kirim data ke email",
            CurrentModule = "Payment"
        };

        var result = detector.EvaluatePreGate(request.Message, request);

        Assert.True(result.HasJifasSignal);
        Assert.False(result.IsHardBlocked);
    }

    [Fact]
    public void EvaluatePreGate_JifasSignal_MenangAtasOosKeyword()
    {
        var detector = Create();
        // "invoice" = sinyal JIFAS, "crypto" = keyword OOS
        // -> JIFAS menang, lolos ke LLM
        var result = detector.EvaluatePreGate("Bagaimana cara crypto invoice di JIFAS?", null);

        Assert.True(result.HasJifasSignal);
        Assert.False(result.IsHardBlocked);
        Assert.True(result.PassThrough);
    }

    // =====================================================================
    // KASUS 3: AMBIGU/NUANSA — TIDAK boleh over-block
    // =====================================================================

    [Theory]
    [InlineData("Apa kabar?")]
    [InlineData("Terima kasih")]
    [InlineData("Hai")]
    [InlineData("Bisa bantu saya?")]
    [InlineData("Halo apa kabar kamu?")]
    [InlineData("Selamat pagi")]
    [InlineData("Pertanyaan umum: apa saja modul di JIFAS?")]
    [InlineData("Seberapa penting budgeting dalam perusahaan?")]
    public void EvaluatePreGate_AmbigiNuansa_PassThrough(string ambiguousQuestion)
    {
        var detector = Create();
        var request = new ChatRequest { Message = ambiguousQuestion };

        var result = detector.EvaluatePreGate(ambiguousQuestion, request);

        Assert.False(result.IsHardBlocked,
            $"Tidak boleh di-hard-block (over-blocking): {ambiguousQuestion}");
        Assert.True(result.PassThrough);
    }

    [Fact]
    public void EvaluatePreGate_Kosong_PassThrough()
    {
        var detector = Create();
        var result = detector.EvaluatePreGate("", null);

        Assert.False(result.IsHardBlocked);
        Assert.True(result.PassThrough);
    }

    [Fact]
    public void EvaluatePreGate_ActiveModuleMenangAtasOos()
    {
        var detector = Create();
        var request = new ChatRequest
        {
            Message = "Bagaimana cuaca di sini?", // OOS keyword tapi activeModule terisi
            CurrentModule = "Budget"
        };

        var result = detector.EvaluatePreGate(request.Message, request);

        Assert.True(result.HasJifasSignal);
        Assert.False(result.IsHardBlocked);
        Assert.True(result.PassThrough);
    }

    // =====================================================================
    // KASUS 4: Istilah JIFAS spesifik (dari KB/modules)
    // =====================================================================

    [Theory]
    [InlineData("Bagaimana cara settlement PUM?")]
    [InlineData("Flow approval RV seperti apa?")]
    [InlineData("COA untuk biaya perjalanan apa?")]
    [InlineData("Report trial balance ada di menu mana?")]
    [InlineData("Vendor baru bagaimana caranya diinput?")]
    [InlineData("Perbedaan PAJE dan CAJE?")]
    [InlineData("Cara void journal entry salah?")]
    public void EvaluatePreGate_IstilahSpesifikJifas_Escapes(string jifasSpecific)
    {
        var detector = Create();
        var result = detector.EvaluatePreGate(jifasSpecific, null);

        Assert.False(result.IsHardBlocked,
            $"Seharusnya lolos ke LLM: {jifasSpecific}");
        Assert.True(result.PassThrough);
    }

    // =====================================================================
    // KASUS 5: HasJifasSignal() — unit test terpisah
    // =====================================================================

    [Fact]
    public void HasJifasSignal_ActiveModule_ReturnsTrue()
    {
        var detector = Create();
        Assert.True(detector.HasJifasSignal("apapun", new ChatRequest
        {
            Context = new RequestContext { ActiveModule = "Home" }
        }));
    }

    [Fact]
    public void HasJifasSignal_CurrentModule_ReturnsTrue()
    {
        var detector = Create();
        Assert.True(detector.HasJifasSignal("apapun", new ChatRequest
        {
            CurrentModule = "Invoice"
        }));
    }

    [Fact]
    public void HasJifasSignal_IstilahJifas_ReturnsTrue()
    {
        var detector = Create();
        Assert.True(detector.HasJifasSignal("Cara approve payment di JIFAS", null));
    }

    [Fact]
    public void HasJifasSignal_KosongDanTanpaContext_ReturnsFalse()
    {
        var detector = Create();
        Assert.False(detector.HasJifasSignal("halo", null));
    }
}
