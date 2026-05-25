using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using jifas_assistant.DAL.Models;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Snapshot profil user untuk dipakai di prompt AI
    /// </summary>
    public class UserProfile
    {
        public string UserId { get; set; }
        public string ExpertiseLevel { get; set; } = "Beginner";
        public string DetectedDepartment { get; set; }
        public string DetectedRole { get; set; }
        public List<string> FavoriteModules { get; set; } = new();
        public List<string> FrequentTopics { get; set; } = new();
        public List<string> RecentQuestions { get; set; } = new();
        public int TotalSessions { get; set; }
        public int TotalQuestions { get; set; }
        public bool IsNewUser { get; set; } = true;
        public string PreferredLanguage { get; set; } = "id";
    }

    public interface IUserMemoryService
    {
        /// <summary>
        /// Ambil profil user (dari cache → DB). Return profil kosong jika belum ada.
        /// </summary>
        Task<UserProfile> GetUserProfileAsync(string userId);

        /// <summary>
        /// Update memori user berdasarkan interaksi terbaru (fire-and-forget friendly).
        /// </summary>
        Task UpdateMemoryAsync(string userId, string userMessage, string aiResponse,
            IntentType intent, double confidenceScore, string currentModule = null,
            string userRole = null, string sessionId = null);

        /// <summary>
        /// Bangun string konteks user untuk disuntikkan ke prompt AI.
        /// </summary>
        Task<string> BuildUserContextForPromptAsync(string userId);
    }

    /// <summary>
    /// Service untuk menyimpan dan menganalisis memori percakapan per user.
    /// Menggunakan SQL Server (tabel UserMemory) + MemoryCache untuk performa.
    /// AI menjadi makin personal karena mengenal karakteristik setiap user.
    /// </summary>
    public class UserMemoryService : IUserMemoryService
    {
        private readonly JIFAS_AssistantContext _db;
        private readonly ICacheService _cache;
        private readonly ILoggerService _logger;

        private const int MAX_RECENT_QUESTIONS = 20;
        private const int MAX_FAVORITE_MODULES = 5;
        private const int CACHE_DURATION_MINUTES = 60;
        private const string CACHE_PREFIX = "UserMemory_";

        // Mapping modul dari kata kunci pesan
        private static readonly Dictionary<string, List<string>> ModuleKeywords =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "Invoice",    new() { "invoice", "tagihan", "faktur", "inv-" } },
                { "Payment",    new() { "payment", "pembayaran", "bayar", "transfer", "bg ", "giro", "cek " } },
                { "PUM",        new() { "pum", "uang muka", "advance", "kasbon", "perjalanan dinas" } },
                { "Receiving",  new() { "receiving", "penerimaan", "rv ", "terima barang", "receive" } },
                { "Budget",     new() { "budget", "anggaran", "overbudget", "over budget", "sisa anggaran" } },
                { "Accounting", new() { "posting", "jurnal", "gl ", "ledger", "akuntansi", "coa", "ap ", "ar " } },
                { "Report",     new() { "laporan", "report", "dashboard", "cashflow", "inquiry" } },
                { "Master",     new() { "master", "vendor", "company", "divisi", "department", "employee" } },
                { "SPK",        new() { "spk", "surat perintah", "kontrak" } },
            };

        // Mapping department dari kata kunci
        private static readonly Dictionary<string, List<string>> DepartmentKeywords =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "Finance",    new() { "finance", "keuangan", "treasury", "tax", "pajak" } },
                { "Accounting", new() { "accounting", "akuntansi", "posting", "gl", "jurnal" } },
                { "Procurement",new() { "procurement", "purchasing", "vendor", "po ", "spk" } },
                { "HR",         new() { "hr", "hrd", "karyawan", "payroll", "employee" } },
                { "IT",         new() { "it ", "sistem", "user", "akses", "login", "error" } },
            };

        public UserMemoryService(
            JIFAS_AssistantContext db,
            ICacheService cache,
            ILoggerService logger)
        {
            _db = db;
            _cache = cache;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────────────
        // PUBLIC METHODS
        // ─────────────────────────────────────────────────────────────────────

        public async Task<UserProfile> GetUserProfileAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId) || userId == "anonymous")
                return new UserProfile { UserId = userId, IsNewUser = true };

            // 1. Cek cache dulu
            var cacheKey = CACHE_PREFIX + userId;
            var cached = _cache.Get<UserProfile>(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug($"[UserMemory] Profile cache HIT for user: {userId}");
                return cached;
            }

            // 2. Ambil dari DB
            try
            {
                var memory = await _db.UserMemories
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.UserId == userId);

                if (memory == null)
                    return new UserProfile { UserId = userId, IsNewUser = true };

                var profile = MapToProfile(memory);

                _cache.Set(cacheKey, profile, CACHE_DURATION_MINUTES);
                _logger.LogDebug($"[UserMemory] Profile loaded from DB for user: {userId} (Level: {profile.ExpertiseLevel}, Sessions: {profile.TotalSessions})");

                return profile;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[UserMemory] Error loading profile for {userId}: {ex.Message}", ex);
                return new UserProfile { UserId = userId, IsNewUser = true };
            }
        }

        public async Task UpdateMemoryAsync(
            string userId,
            string userMessage,
            string aiResponse,
            IntentType intent,
            double confidenceScore,
            string currentModule = null,
            string userRole = null,
            string sessionId = null)
        {
            if (string.IsNullOrWhiteSpace(userId) || userId == "anonymous") return;

            try
            {
                _logger.LogDebug(
                    $"[UserMemory] Update started for {userId} — Intent: {intent}, Module: {currentModule ?? "(detected)"}, Role: {userRole ?? "(none)"}");

                // Upsert ke database
                var memory = await _db.UserMemories
                    .FirstOrDefaultAsync(m => m.UserId == userId);

                var isNew = memory == null;
                if (isNew)
                {
                    memory = new UserMemory
                    {
                        UserId = userId,
                        FirstSeenAt = DateTime.UtcNow
                    };
                    _logger.LogInformation($"[UserMemory] NEW profile created for {userId}");
                }

                // Update statistik
                memory.TotalQuestions++;
                memory.LastSeenAt = DateTime.UtcNow;
                memory.UpdatedAt = DateTime.UtcNow;

                // Track unique sessions to increment TotalSessions
                if (!string.IsNullOrWhiteSpace(sessionId))
                {
                    var sessionCacheKey = $"UserSession_{userId}_{sessionId}";
                    var seenBefore = _cache.Get<bool>(sessionCacheKey);
                    if (!seenBefore)
                    {
                        memory.TotalSessions++;
                        _cache.Set(sessionCacheKey, true, 24 * 60); // 24 hours
                        _logger.LogDebug($"[UserMemory] {userId} — New session detected, TotalSessions: {memory.TotalSessions}");
                    }
                }

                _logger.LogDebug($"[UserMemory] {userId} — TotalQuestions: {memory.TotalQuestions}");

                if (intent == IntentType.HowTo)        memory.HowToCount++;
                if (intent == IntentType.Troubleshooting) memory.TroubleshootingCount++;

                // Update rolling average confidence
                memory.AverageConfidenceReceived = memory.TotalQuestions == 1
                    ? confidenceScore
                    : (memory.AverageConfidenceReceived * (memory.TotalQuestions - 1) + confidenceScore)
                      / memory.TotalQuestions;

                // Deteksi dan update modul favorit
                var detectedModule = currentModule ?? DetectModule(userMessage);
                if (!string.IsNullOrEmpty(detectedModule))
                    memory.FavoriteModules = UpdateTopList(memory.FavoriteModules, detectedModule, MAX_FAVORITE_MODULES);

                // Update topik dari response
                var detectedTopic = DetectTopic(userMessage);
                if (!string.IsNullOrEmpty(detectedTopic))
                    memory.FrequentTopics = UpdateTopList(memory.FrequentTopics, detectedTopic, 10);

                // Update recent questions (sliding window)
                memory.RecentQuestions = AddToRecentList(
                    memory.RecentQuestions,
                    TruncateForStorage(userMessage, 150),
                    MAX_RECENT_QUESTIONS);

                // Deteksi department dari pertanyaan
                if (string.IsNullOrEmpty(memory.DetectedDepartment))
                    memory.DetectedDepartment = DetectDepartment(userMessage);

                // Update role dari request jika ada
                if (!string.IsNullOrEmpty(userRole) && string.IsNullOrEmpty(memory.DetectedRole))
                    memory.DetectedRole = userRole;

                // Hitung expertise level
                memory.ExpertiseLevel = CalculateExpertiseLevel(memory);

                if (isNew)
                    _db.UserMemories.Add(memory);

                await _db.SaveChangesAsync();

                // Invalidate cache agar profil fresh
                _cache.Remove(CACHE_PREFIX + userId);

                _logger.LogInformation(
                    $"[UserMemory] Updated profile for {userId} — Level: {memory.ExpertiseLevel}, Questions: {memory.TotalQuestions}, Modules: {memory.FavoriteModules ?? "(none)"}");
            }
            catch (Exception ex)
            {
                // Jangan gagalkan chat karena memory update error
                _logger.LogError($"[UserMemory] Error updating memory for {userId}: {ex.Message}", ex);
            }
        }

        public async Task<string> BuildUserContextForPromptAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId) || userId == "anonymous")
                return string.Empty;

            var profile = await GetUserProfileAsync(userId);
            if (profile.IsNewUser)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("=== PROFIL USER ===");
            sb.AppendLine($"User ID: {profile.UserId}");

            if (!string.IsNullOrEmpty(profile.DetectedDepartment))
                sb.AppendLine($"Departemen: {profile.DetectedDepartment}");

            if (!string.IsNullOrEmpty(profile.DetectedRole))
                sb.AppendLine($"Role di JIFAS: {profile.DetectedRole}");

            sb.AppendLine($"Level Pengalaman JIFAS: {MapExpertiseToDescription(profile.ExpertiseLevel)}");
            sb.AppendLine($"Total Sesi: {profile.TotalSessions}, Total Pertanyaan: {profile.TotalQuestions}");

            if (profile.FavoriteModules.Count > 0)
                sb.AppendLine($"Modul yang sering digunakan: {string.Join(", ", profile.FavoriteModules)}");

            if (profile.FrequentTopics.Count > 0)
                sb.AppendLine($"Topik yang sering ditanya: {string.Join(", ", profile.FrequentTopics.Take(5))}");

            if (profile.RecentQuestions.Count > 0)
            {
                sb.AppendLine("Pertanyaan terakhir user ini:");
                foreach (var q in profile.RecentQuestions.Take(3))
                    sb.AppendLine($"  - {q}");
            }

            sb.AppendLine();
            sb.AppendLine("INSTRUKSI PERSONALISASI:");
            sb.AppendLine(BuildPersonalizationInstructions(profile));
            sb.AppendLine("=== END PROFIL USER ===");

            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────
        // PRIVATE HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private static UserProfile MapToProfile(UserMemory m) => new()
        {
            UserId          = m.UserId,
            ExpertiseLevel  = m.ExpertiseLevel ?? "Beginner",
            DetectedDepartment = m.DetectedDepartment,
            DetectedRole    = m.DetectedRole,
            FavoriteModules = ParseJsonList(m.FavoriteModules),
            FrequentTopics  = ParseJsonList(m.FrequentTopics),
            RecentQuestions = ParseJsonList(m.RecentQuestions),
            TotalSessions   = m.TotalSessions,
            TotalQuestions  = m.TotalQuestions,
            IsNewUser       = false,
            PreferredLanguage = m.PreferredLanguage ?? "id"
        };

        private static string CalculateExpertiseLevel(UserMemory m)
        {
            // Beginner: banyak how-to, sedikit troubleshooting
            // Intermediate: campuran
            // Advanced: lebih banyak troubleshooting, banyak sesi
            if (m.TotalQuestions < 10)
                return "Beginner";

            var troubleshootingRatio = m.TotalQuestions > 0
                ? (double)m.TroubleshootingCount / m.TotalQuestions
                : 0;

            if (m.TotalSessions >= 20 && troubleshootingRatio >= 0.3)
                return "Advanced";
            if (m.TotalSessions >= 5 || m.TotalQuestions >= 20)
                return "Intermediate";

            return "Beginner";
        }

        private static string BuildPersonalizationInstructions(UserProfile profile)
        {
            var instructions = new List<string>();

            switch (profile.ExpertiseLevel)
            {
                case "Beginner":
                    instructions.Add("Berikan penjelasan langkah demi langkah yang detail dan mudah dipahami.");
                    instructions.Add("Tambahkan definisi istilah teknis JIFAS jika digunakan.");
                    instructions.Add("Gunakan analogi sederhana jika membantu pemahaman.");
                    break;
                case "Intermediate":
                    instructions.Add("Fokus pada langkah-langkah konkret tanpa terlalu banyak penjelasan dasar.");
                    instructions.Add("Sertakan tips atau shortcut JIFAS yang relevan.");
                    break;
                case "Advanced":
                    instructions.Add("Jawab secara ringkas dan teknis — user sudah paham dasar JIFAS.");
                    instructions.Add("Fokus pada root cause dan solusi spesifik, bukan penjelasan umum.");
                    instructions.Add("Bisa menyebut kode error, tabel DB, atau konfigurasi sistem jika relevan.");
                    break;
            }

            if (profile.FavoriteModules.Count > 0)
                instructions.Add($"User bekerja paling sering dengan modul {string.Join(" dan ", profile.FavoriteModules.Take(2))} — prioritaskan konteks modul ini.");

            if (!string.IsNullOrEmpty(profile.DetectedDepartment))
                instructions.Add($"User berasal dari departemen {profile.DetectedDepartment} — sesuaikan terminologi dan proses.");

            return string.Join("\n", instructions.Select(i => $"- {i}"));
        }

        private static string MapExpertiseToDescription(string level) => level switch
        {
            "Advanced"     => "Advanced (pengguna berpengalaman JIFAS)",
            "Intermediate" => "Intermediate (sudah familiar dengan JIFAS)",
            _              => "Beginner (pengguna baru atau jarang pakai JIFAS)"
        };

        private static string DetectModule(string message)
        {
            var lower = message.ToLowerInvariant();
            foreach (var (module, keywords) in ModuleKeywords)
                if (keywords.Any(k => lower.Contains(k)))
                    return module;
            return null;
        }

        private static string DetectTopic(string message)
        {
            // Topic = first matched module keyword group
            return DetectModule(message);
        }

        private static string DetectDepartment(string message)
        {
            var lower = message.ToLowerInvariant();
            foreach (var (dept, keywords) in DepartmentKeywords)
                if (keywords.Any(k => lower.Contains(k)))
                    return dept;
            return null;
        }

        /// <summary>
        /// Update ranked list: jika item sudah ada, naikkan posisinya; jika baru, tambahkan di atas.
        /// Direpresentasikan sebagai JSON array (item paling sering = index 0).
        /// </summary>
        private static string UpdateTopList(string json, string newItem, int maxItems)
        {
            var list = ParseJsonList(json);

            // Hapus duplikat lama dan taruh di depan (most-recent-first = proxy for frequency)
            list.Remove(newItem);
            list.Insert(0, newItem);

            if (list.Count > maxItems)
                list = list.Take(maxItems).ToList();

            return JsonSerializer.Serialize(list);
        }

        private static string AddToRecentList(string json, string newItem, int maxItems)
        {
            var list = ParseJsonList(json);
            list.Insert(0, newItem);
            if (list.Count > maxItems)
                list = list.Take(maxItems).ToList();
            return JsonSerializer.Serialize(list);
        }

        private static List<string> ParseJsonList(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();
            try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
            catch { return new List<string>(); }
        }

        private static string TruncateForStorage(string text, int maxLength) =>
            string.IsNullOrEmpty(text) ? text
            : text.Length <= maxLength ? text
            : text[..maxLength].TrimEnd() + "…";
    }
}
