using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Jifas.Assistant.Models;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Command layer ringan untuk perintah slash seperti /help dan /status.
    /// Konsepnya mirip command registry: command ditangani cepat tanpa memanggil KB atau LLM.
    /// </summary>
    public interface IAssistantCommandService
    {
        ChatResponse? TryHandleCommand(string message, ChatRequest request, string sessionId, string correlationId);
        IReadOnlyList<AssistantCapability> GetCapabilities();
        IReadOnlyList<string> GetSupportedCommands();
    }

    public class AssistantCommandService : IAssistantCommandService
    {
        private static readonly IReadOnlyDictionary<string, Func<AssistantCommandService, ChatRequest, string, string, ChatResponse>> CommandHandlers =
            new Dictionary<string, Func<AssistantCommandService, ChatRequest, string, string, ChatResponse>>(StringComparer.OrdinalIgnoreCase)
            {
                ["/help"] = (svc, req, sid, cid) => svc.BuildHelpResponse(req, sid, cid),
                ["/commands"] = (svc, req, sid, cid) => svc.BuildHelpResponse(req, sid, cid),
                ["/status"] = (svc, req, sid, cid) => svc.BuildStatusResponse(req, sid, cid),
                ["/monitoring"] = (svc, req, sid, cid) => svc.BuildMonitoringResponse(req, sid, cid),
                ["/ticket"] = (svc, req, sid, cid) => svc.BuildTicketResponse(req, sid, cid),
                ["/kb"] = (svc, req, sid, cid) => svc.BuildKnowledgeBaseResponse(req, sid, cid),
                ["/context"] = (svc, req, sid, cid) => svc.BuildContextResponse(req, sid, cid),
                ["/scope"] = (svc, req, sid, cid) => svc.BuildScopeResponse(req, sid, cid),
            };

        private static readonly IReadOnlyList<AssistantCapability> Capabilities = new List<AssistantCapability>
        {
            new AssistantCapability
            {
                Id = "kb-rag",
                Name = "Knowledge Base RAG",
                Description = "Menjawab pertanyaan JIFAS berdasarkan Knowledge Base dan pencarian semantic pgvector.",
                Examples = new List<string> { "Apa itu JIFAS?", "Tombol approve invoice ada di page mana?" }
            },
            new AssistantCapability
            {
                Id = "ticket-flow",
                Name = "Jira Ticket Flow",
                Description = "Membantu user membuat tiket Jira melalui dialog konfirmasi bertahap.",
                Examples = new List<string> { "Buat tiket", "Tombol approve invoice tidak bisa diklik" }
            },
            new AssistantCapability
            {
                Id = "page-context",
                Name = "Page Context",
                Description = "Memakai module, page, document type, dan status halaman dari frontend agar jawaban lebih relevan.",
                Examples = new List<string> { "/context", "Saya sedang di Invoice Approval, ini page apa?" }
            },
            new AssistantCapability
            {
                Id = "monitoring",
                Name = "Monitoring Dashboard",
                Description = "Menyediakan dashboard request AI, latency, token, KB hit, cache, dan dependency health.",
                Examples = new List<string> { "/monitoring", "Cek dashboard AI" }
            },
            new AssistantCapability
            {
                Id = "safe-scope",
                Name = "JIFAS Scope Guard",
                Description = "Menahan pertanyaan di luar konteks JIFAS agar jawaban tetap aman dan relevan.",
                Examples = new List<string> { "/scope", "Apa saja batasan AI ini?" }
            }
        };

        public ChatResponse? TryHandleCommand(string message, ChatRequest request, string sessionId, string correlationId)
        {
            if (string.IsNullOrWhiteSpace(message))
                return null;

            var trimmed = message.Trim();
            if (!trimmed.StartsWith("/", StringComparison.Ordinal))
                return null;

            var command = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            if (CommandHandlers.TryGetValue(command, out var handler))
                return handler(this, request, sessionId, correlationId);

            return BuildResponse(
                sessionId,
                correlationId,
                "Command",
                $"Perintah `{command}` belum tersedia di JIFAS Assistant.\n\nKetik `/help` untuk melihat perintah yang bisa dipakai.");
        }

        public IReadOnlyList<AssistantCapability> GetCapabilities() => Capabilities;

        public IReadOnlyList<string> GetSupportedCommands() => CommandHandlers.Keys.OrderBy(x => x).ToList();

        private ChatResponse BuildHelpResponse(ChatRequest request, string sessionId, string correlationId)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Perintah cepat JIFAS Assistant:");
            sb.AppendLine();
            sb.AppendLine("- `/help` atau `/commands`: lihat daftar perintah.");
            sb.AppendLine("- `/status`: lihat endpoint health dan status konfigurasi utama.");
            sb.AppendLine("- `/monitoring`: lihat alamat dashboard monitoring AI.");
            sb.AppendLine("- `/ticket`: panduan membuat tiket Jira dari chat.");
            sb.AppendLine("- `/kb`: panduan bertanya ke Knowledge Base JIFAS.");
            sb.AppendLine("- `/context`: cek context halaman yang diterima AI.");
            sb.AppendLine("- `/scope`: lihat batasan topik yang bisa dijawab.");
            sb.AppendLine();
            sb.Append("Untuk pertanyaan biasa, langsung ketik masalah atau topik JIFAS yang ingin ditanyakan.");

            return BuildResponse(sessionId, correlationId, "Command Help", sb.ToString());
        }

        private ChatResponse BuildStatusResponse(ChatRequest request, string sessionId, string correlationId)
        {
            var message =
                "Status operasional JIFAS Assistant bisa dicek dari endpoint berikut:\n\n" +
                "- `/health`: health check aplikasi.\n" +
                "- `/api/chat/health`: health check modul chat.\n" +
                "- `/api/KnowledgeBaseSearch/health`: health check pencarian Knowledge Base.\n" +
                "- `/api/monitoring/all?minutes=60`: data monitoring 60 menit terakhir.\n\n" +
                "Konfigurasi saat ini: app-level rate limit tidak aktif, Redis dipakai sebagai cache jika tersedia, dan suggestion LLM terpisah tidak dipakai.";

            return BuildResponse(sessionId, correlationId, "Command Status", message);
        }

        private ChatResponse BuildMonitoringResponse(ChatRequest request, string sessionId, string correlationId)
        {
            var message =
                "Dashboard monitoring JIFAS AI tersedia di `/monitoring/index.html`.\n\n" +
                "Dashboard ini dipakai untuk membaca total request, error rate, avg/p95 latency, token usage, cache hit/miss, KB hit, dependency failure, dan request terbaru.";

            return BuildResponse(sessionId, correlationId, "Command Monitoring", message);
        }

        private ChatResponse BuildTicketResponse(ChatRequest request, string sessionId, string correlationId)
        {
            var message =
                "Untuk membuat tiket Jira dari chat, ketik masalahnya dengan jelas, misalnya:\n\n" +
                "`Buat tiket: tombol approve invoice tidak bisa diklik di Invoice Approval.`\n\n" +
                "AI akan meminta detail, menampilkan ringkasan, lalu membuat tiket hanya setelah kamu konfirmasi. Jika berubah pikiran, ketik `batal` sebelum konfirmasi final.";

            return BuildResponse(sessionId, correlationId, "Command Ticket", message);
        }

        private ChatResponse BuildKnowledgeBaseResponse(ChatRequest request, string sessionId, string correlationId)
        {
            var message =
                "Untuk bertanya ke Knowledge Base, tulis pertanyaan natural tentang JIFAS, modul, alur approval, menu, atau troubleshooting.\n\n" +
                "Contoh:\n" +
                "- `Apa itu JIFAS?`\n" +
                "- `Bagaimana cara approve invoice?`\n" +
                "- `Tombol approve invoice ada di page mana?`\n\n" +
                "Jika pertanyaan bergantung pada halaman yang sedang dibuka, pastikan frontend mengirim context page/module.";

            return BuildResponse(sessionId, correlationId, "Command Knowledge Base", message);
        }

        private ChatResponse BuildContextResponse(ChatRequest request, string sessionId, string correlationId)
        {
            var context = request.Context;
            var sb = new StringBuilder();
            sb.AppendLine("Context yang diterima JIFAS Assistant:");
            sb.AppendLine();
            sb.AppendLine($"- Company: {SafeValue(request.UserCompCode ?? request.CompanyId)}");
            sb.AppendLine($"- Module: {SafeValue(request.CurrentModule ?? context?.ActiveModule)}");
            sb.AppendLine($"- Page: {SafeValue(context?.CurrentPage)}");
            sb.AppendLine($"- Title: {SafeValue(context?.PageTitle)}");
            sb.AppendLine($"- Document Type: {SafeValue(context?.DocumentType)}");
            sb.AppendLine($"- Document Status: {SafeValue(context?.DocumentStatus)}");
            sb.AppendLine();
            sb.Append("Context ini membantu AI menjawab pertanyaan navigasi dan masalah yang spesifik halaman.");

            return BuildResponse(sessionId, correlationId, "Command Context", sb.ToString());
        }

        private ChatResponse BuildScopeResponse(ChatRequest request, string sessionId, string correlationId)
        {
            var message =
                "Scope JIFAS Assistant dibatasi ke topik JIFAS: modul finance/accounting, menu, approval, report, troubleshooting penggunaan sistem, Knowledge Base, dan ticket support.\n\n" +
                "Pertanyaan umum di luar JIFAS seperti cuaca, politik, film, crypto, atau topik pribadi akan ditolak dengan aman.";

            return BuildResponse(sessionId, correlationId, "Command Scope", message);
        }

        private static ChatResponse BuildResponse(string sessionId, string correlationId, string source, string message)
        {
            return new ChatResponse
            {
                Sender = "JIFAS AI Assistant",
                Message = message,
                Source = source,
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                Success = true,
                SessionId = sessionId,
                CorrelationId = correlationId,
                IsFromKnowledgeBase = false,
                ConfidenceScore = 1.0,
                Suggestions = new List<string>(),
                KnowledgeBaseResults = new List<KnowledgeBaseResult>()
            };
        }

        private static string SafeValue(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
    }
}
