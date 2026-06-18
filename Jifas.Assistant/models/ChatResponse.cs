using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Jifas.Assistant.Services;

namespace Jifas.Assistant.Models
{
    /// <summary>
    /// Model response utama dari chatbot JIFAS.
    /// Berisi pesan, error, source KB, tiket, suggestion kompatibilitas, dan performance metrics.
    /// </summary>
    public class ChatResponse
    {
        /// <summary>
        /// Nama pengirim response.
        /// </summary>
        public string Sender { get; set; } = "JIFAS AI Assistant";

        /// <summary>
        /// Isi jawaban chatbot.
        /// </summary>
        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Daftar error jika request gagal diproses.
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// Correlation id untuk tracing request di log.
        /// </summary>
        public string CorrelationId { get; set; } = string.Empty;

        /// <summary>
        /// Sumber jawaban, misalnya Knowledge Base, Ticket Flow, atau Input Validation.
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// Waktu response dibuat.
        /// </summary>
        public string Timestamp { get; set; } = string.Empty;

        /// <summary>
        /// Menandakan request berhasil diproses.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Session id untuk menghubungkan percakapan.
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// True jika jawaban berasal dari Knowledge Base.
        /// </summary>
        public bool IsFromKnowledgeBase { get; set; }

        /// <summary>
        /// Confidence score jawaban dalam rentang 0 sampai 1.
        /// </summary>
        public double ConfidenceScore { get; set; }

        /// <summary>
        /// Suggestion pertanyaan lanjutan untuk kompatibilitas frontend lama.
        /// Pipeline chat normal mengisi ini kosong agar tidak ada AI call kedua.
        /// </summary>
        public List<string> Suggestions { get; set; } = new List<string>();

        /// <summary>
        /// Informasi tiket jika percakapan membuat tiket.
        /// </summary>
        public TicketInfo? Ticket { get; set; }

        /// <summary>
        /// Hasil Knowledge Base yang dipakai untuk membuat jawaban.
        /// </summary>
        public List<KnowledgeBaseResult> KnowledgeBaseResults { get; set; } = new List<KnowledgeBaseResult>();

        /// <summary>
        /// Metrics performa response dalam milidetik.
        /// </summary>
        [JsonProperty("performanceMetrics")]
        public PerformanceMetrics PerformanceMetrics { get; set; } = new PerformanceMetrics();
    }

    /// <summary>
    /// Metrics performa untuk analisis latency chatbot.
    /// Semua nilai durasi memakai milidetik.
    /// </summary>
    public class PerformanceMetrics
    {
        /// <summary>
        /// Durasi validasi input.
        /// </summary>
        public long InputValidationMs { get; set; }

        /// <summary>
        /// Durasi lookup cache.
        /// </summary>
        public long CacheLookupMs { get; set; }

        /// <summary>
        /// Durasi deteksi scope.
        /// </summary>
        public long ScopeDetectionMs { get; set; }

        /// <summary>
        /// Durasi pencarian KB.
        /// </summary>
        public long KbSearchMs { get; set; }

        /// <summary>
        /// Durasi validasi hasil KB.
        /// </summary>
        public long ResultValidationMs { get; set; }

        /// <summary>
        /// Durasi perhitungan confidence.
        /// </summary>
        public long ConfidenceCalculationMs { get; set; }

        /// <summary>
        /// Durasi generate jawaban LLM.
        /// </summary>
        public long LlmResponseMs { get; set; }

        /// <summary>
        /// Durasi pipeline suggestion lama. Normalnya 0 ms karena suggestion LLM terpisah sudah dimatikan.
        /// </summary>
        public long SuggestionsMs { get; set; }

        /// <summary>
        /// Durasi menyimpan response ke cache.
        /// </summary>
        public long CachingMs { get; set; }

        /// <summary>
        /// Durasi total end-to-end.
        /// </summary>
        public long TotalMs { get; set; }

        /// <summary>
        /// True jika response berasal dari cache.
        /// </summary>
        public bool WasCacheLit { get; set; }

        /// <summary>
        /// Scope cache yang dipakai: shared untuk pertanyaan umum, contextual untuk pertanyaan berbasis user/halaman.
        /// </summary>
        public string CacheScope { get; set; } = string.Empty;

        /// <summary>
        /// True jika suggestion lama berasal dari cache.
        /// </summary>
        public bool SuggestionsCached { get; set; }

        /// <summary>
        /// Rata-rata score hasil KB.
        /// </summary>
        public double AverageKbScore { get; set; }

        /// <summary>
        /// Jumlah hasil KB sebelum validasi.
        /// </summary>
        public int KbResultsBeforeValidation { get; set; }

        /// <summary>
        /// Jumlah hasil KB setelah validasi.
        /// </summary>
        public int KbResultsAfterValidation { get; set; }

        /// <summary>
        /// Jalur keputusan utama yang dipakai chatbot: learning-exact, learning-similar, kb-rag, cache, ticket, fallback.
        /// </summary>
        public string Route { get; set; } = string.Empty;

        /// <summary>
        /// Versi knowledge yang ikut masuk ke cache key agar publish/republish tidak tertutup cache lama.
        /// </summary>
        public string KnowledgeVersion { get; set; } = string.Empty;

        /// <summary>
        /// Jenis match AI Learning: exact, similar, atau kosong jika bukan AI Learning.
        /// </summary>
        public string LearningMatchType { get; set; } = string.Empty;

        /// <summary>
        /// Durasi formatter LLM khusus AI Learning. Normalnya 0 jika jawaban admin langsung dipakai.
        /// </summary>
        public long LearningFormatterMs { get; set; }

        /// <summary>
        /// Ringkasan performa untuk log.
        /// </summary>
        public string GetSummary()
        {
            var cacheLabel = WasCacheLit ? "HIT" : "MISS";
            var scopeLabel = string.IsNullOrWhiteSpace(CacheScope) ? "n/a" : CacheScope;
            var routeLabel = string.IsNullOrWhiteSpace(Route) ? "n/a" : Route;
            return $"[PERFORMANCE] Total: {TotalMs}ms | Route: {routeLabel} | Validation: {InputValidationMs}ms | KB Search: {KbSearchMs}ms | LLM: {LlmResponseMs}ms | LearningFormatter: {LearningFormatterMs}ms | Suggestions: {SuggestionsMs}ms | Cache: {cacheLabel}/{scopeLabel}";
        }
    }

    /// <summary>
    /// Informasi tiket yang ditempelkan pada response chat.
    /// </summary>
    public class TicketInfo
    {
        /// <summary>
        /// Nomor tiket yang dibuat.
        /// </summary>
        public string TicketNumber { get; set; } = string.Empty;

        /// <summary>
        /// Status tiket saat ini.
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Pesan terkait tiket.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Link tiket Jira jika tiket dibuat di Jira.
        /// </summary>
        public string Url { get; set; } = string.Empty;
    }
}
