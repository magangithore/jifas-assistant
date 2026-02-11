using System;
using System.Linq;
using System.Threading.Tasks;
using Jifas.Assistant.Data;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Service for creating and managing JIFAS-related support tickets
    /// </summary>
    public interface ITicketService
    {
        /// <summary>
        /// Create a new support ticket
        /// </summary>
        Task<TicketResult> CreateTicketAsync(CreateTicketRequest request);

        /// <summary>
        /// Get ticket by ticket number
        /// </summary>
        Task<TicketResult> GetTicketAsync(string ticketNumber);
    }

    public class CreateTicketRequest
    {
        public string UserId { get; set; }
        public string Subject { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string Priority { get; set; }
        public string SessionId { get; set; }
    }

    public class TicketResult
    {
        public bool Success { get; set; }
        public string TicketNumber { get; set; }
        public string Message { get; set; }
        public string Status { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    /// <summary>
    /// Ticket service implementation
    /// Only creates tickets for JIFAS-related issues
    /// Tickets are stored in the Metrics table with ticket metadata
    /// </summary>
    public class TicketService : ITicketService
    {
        private readonly JifasAssistantDbContext _db;
        private readonly ILoggerService _logger;

        // Valid JIFAS ticket categories
        private static readonly string[] ValidCategories = new[]
        {
            "jifas_access",
            "jifas_login",
            "jifas_error",
            "jifas_feature_request",
            "jifas_training",
            "jifas_report",
            "jifas_module",
            "jifas_other"
        };

        // In-memory ticket storage for demo (replace with DB table for production)
        private static readonly System.Collections.Generic.Dictionary<string, TicketData> _ticketStore = new();

        public TicketService(JifasAssistantDbContext db, ILoggerService logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<TicketResult> CreateTicketAsync(CreateTicketRequest request)
        {
            try
            {
                // Validate request
                if (request == null || string.IsNullOrWhiteSpace(request.Subject))
                {
                    return new TicketResult
                    {
                        Success = false,
                        Message = "Subject is required"
                    };
                }

                if (string.IsNullOrWhiteSpace(request.Description))
                {
                    return new TicketResult
                    {
                        Success = false,
                        Message = "Description is required"
                    };
                }

                // Validate category is JIFAS-related
                var category = ValidateCategory(request.Category);

                // Generate ticket number
                var ticketNumber = GenerateTicketNumber();

                // Create ticket data
                var ticketData = new TicketData
                {
                    TicketNumber = ticketNumber,
                    UserId = request.UserId ?? "anonymous",
                    Subject = $"[JIFAS] {request.Subject}",
                    Description = request.Description,
                    Category = category,
                    Priority = ValidatePriority(request.Priority),
                    Status = "Open",
                    ConversationSessionId = request.SessionId,
                    CreatedAt = DateTime.UtcNow
                };

                // Store ticket (in production, this would be persisted to a Tickets table)
                _ticketStore[ticketNumber] = ticketData;

                // Log ticket creation to metrics
                var metric = new Data.Models.Metric
                {
                    MetricType = "TicketCreated",
                    MetricName = ticketNumber,
                    Count = 1,
                    Value = 1.0,
                    Category = category,
                    Tags = $"UserId:{request.UserId};SessionId:{request.SessionId}",
                    CreatedAt = DateTime.UtcNow
                };

                _db.Metrics.Add(metric);
                await _db.SaveChangesAsync();

                _logger.LogInformation("[TicketService] Ticket created successfully: {0}", ticketNumber);

                return new TicketResult
                {
                    Success = true,
                    TicketNumber = ticketNumber,
                    Message = $"Ticket berhasil dibuat dengan nomor: {ticketNumber}. Tim IT Help Desk akan segera menghubungi Anda.",
                    Status = "Open",
                    CreatedAt = ticketData.CreatedAt
                };
            }
            catch (Exception ex)
            {
                _logger.LogError("[TicketService] Error creating ticket: {0}", ex, ex.Message);
                return new TicketResult
                {
                    Success = false,
                    Message = "Gagal membuat ticket. Silakan coba lagi atau hubungi IT Help Desk langsung."
                };
            }
        }

        public async Task<TicketResult> GetTicketAsync(string ticketNumber)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ticketNumber))
                {
                    return new TicketResult
                    {
                        Success = false,
                        Message = "Ticket number is required"
                    };
                }

                var found = _ticketStore.TryGetValue(ticketNumber, out var ticket);

                if (!found || ticket == null)
                {
                    _logger.LogWarning("[TicketService] Ticket not found: {0}", ticketNumber);
                    return new TicketResult
                    {
                        Success = false,
                        Message = $"Ticket dengan nomor {ticketNumber} tidak ditemukan."
                    };
                }

                return new TicketResult
                {
                    Success = true,
                    TicketNumber = ticket.TicketNumber,
                    Message = ticket.Description,
                    Status = ticket.Status,
                    CreatedAt = ticket.CreatedAt
                };
            }
            catch (Exception ex)
            {
                _logger.LogError("[TicketService] Error retrieving ticket: {0}", ex, ex.Message);
                return new TicketResult
                {
                    Success = false,
                    Message = "Gagal mengambil informasi ticket."
                };
            }
        }

        private string GenerateTicketNumber()
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd");
            var random = new Random().Next(1000, 9999);
            return $"JIFAS-{timestamp}-{random}";
        }

        private string ValidateCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return "jifas_other";

            var normalized = category.ToLower().Replace(" ", "_");
            
            foreach (var valid in ValidCategories)
            {
                if (normalized.Contains(valid.Replace("jifas_", "")))
                    return valid;
            }

            return "jifas_other";
        }

        private string ValidatePriority(string priority)
        {
            if (string.IsNullOrWhiteSpace(priority))
                return "Medium";

            var normalized = priority.ToLower();
            
            if (normalized.Contains("high") || normalized.Contains("tinggi") || normalized.Contains("urgent"))
                return "High";
            
            if (normalized.Contains("low") || normalized.Contains("rendah"))
                return "Low";
            
            return "Medium";
        }

        /// <summary>
        /// Internal ticket data structure
        /// </summary>
        private class TicketData
        {
            public string TicketNumber { get; set; }
            public string UserId { get; set; }
            public string Subject { get; set; }
            public string Description { get; set; }
            public string Category { get; set; }
            public string Priority { get; set; }
            public string Status { get; set; }
            public string ConversationSessionId { get; set; }
            public DateTime CreatedAt { get; set; }
        }
    }
}
