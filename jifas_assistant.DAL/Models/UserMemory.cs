using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace jifas_assistant.DAL.Models
{
    /// <summary>
    /// Menyimpan profil dan memori jangka panjang per user.
    /// Diupdate setiap sesi percakapan sehingga AI makin mengenal karakteristik user.
    /// </summary>
    [Table("UserMemory")]
    public class UserMemory
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>
        /// User ID (Windows username) — unique per user
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string UserId { get; set; } = string.Empty;

        // ─── Modul & Topik ────────────────────────────────────────────────────

        /// <summary>
        /// Modul JIFAS yang paling sering ditanya (JSON array string)
        /// Contoh: ["Invoice","Payment","PUM"]
        /// </summary>
        public string? FavoriteModules { get; set; }

        /// <summary>
        /// Topik/kategori KB yang sering diakses (JSON array string)
        /// </summary>
        public string? FrequentTopics { get; set; }

        /// <summary>
        /// Pertanyaan-pertanyaan terakhir (JSON array string, maks 20 item)
        /// Dipakai untuk deteksi pola dan personalisasi saran
        /// </summary>
        public string? RecentQuestions { get; set; }

        // ─── Karakteristik User ───────────────────────────────────────────────

        /// <summary>
        /// Estimasi level expertise: Beginner / Intermediate / Advanced
        /// Dihitung dari pola pertanyaan (how-to banyak = Beginner, troubleshooting = Advanced)
        /// </summary>
        [MaxLength(20)]
        public string ExpertiseLevel { get; set; } = "Beginner";

        /// <summary>
        /// Preferensi bahasa jawaban: "id" / "en"
        /// </summary>
        [MaxLength(5)]
        public string PreferredLanguage { get; set; } = "id";

        /// <summary>
        /// Role/department user di JIFAS (dari request context atau terdeteksi dari pertanyaan)
        /// Contoh: "Finance", "Accounting", "Procurement"
        /// </summary>
        [MaxLength(100)]
        public string? DetectedDepartment { get; set; }

        /// <summary>
        /// Role JIFAS yang terdeteksi dari pola pertanyaan
        /// Contoh: "Pemohon Invoice", "Finance Checker", "Head Approval"
        /// </summary>
        [MaxLength(100)]
        public string? DetectedRole { get; set; }

        // ─── Statistik ────────────────────────────────────────────────────────

        /// <summary>
        /// Total sesi percakapan
        /// </summary>
        public int TotalSessions { get; set; } = 0;

        /// <summary>
        /// Total pertanyaan yang pernah diajukan
        /// </summary>
        public int TotalQuestions { get; set; } = 0;

        /// <summary>
        /// Jumlah pertanyaan how-to (indicator beginner)
        /// </summary>
        public int HowToCount { get; set; } = 0;

        /// <summary>
        /// Jumlah pertanyaan troubleshooting (indicator advanced)
        /// </summary>
        public int TroubleshootingCount { get; set; } = 0;

        /// <summary>
        /// Rata-rata confidence score jawaban yang diterima (indikator KB coverage)
        /// </summary>
        public double AverageConfidenceReceived { get; set; } = 0;

        // ─── Timestamps ───────────────────────────────────────────────────────

        /// <summary>
        /// Pertama kali user menggunakan JIFAS Assistant
        /// </summary>
        public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Terakhir kali user aktif
        /// </summary>
        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Terakhir kali profil diupdate
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // ─── Cross-session memory ───────────────────────────────────────────

        /// <summary>
        /// ID sesi terakhir user — untuk cross-session memory.
        /// Saat user buka sesi baru, AI inject konteks dari sesi ini.
        /// </summary>
        [MaxLength(100)]
        public string? LastSessionId { get; set; }

        /// <summary>
        /// Timestamp sesi terakhir — untuk format "2 jam yang lalu" dll.
        /// </summary>
        public DateTime? LastSessionAt { get; set; }
    }
}
