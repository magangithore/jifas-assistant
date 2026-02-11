using System;
using System.Linq;
using System.Threading.Tasks;
using Jifas.Chatbot.DAL;

namespace Jifas.Chatbot.Services
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
    /// </summary>
    public class TicketService : ITicketService
    {
        private readonly JIFAS_AssistantEntities _db;

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

        public TicketService()
        {
            _db = new JIFAS_AssistantEntities();
        }

        public TicketService(JIFAS_AssistantEntities db)
        {
            _db = db;
        }

        public async Task<TicketResult> CreateTicketAsync(CreateTicketRequest request)
        {
            try
            {
                // Validate request
                if (string.IsNullOrWhiteSpace(request.Subject))
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

                // Create ticket entity
                var ticket = new Tickets
                {
                    TicketNumber = ticketNumber,
                    UserId = request.UserId ?? "anonymous",
                    Subject = $"[JIFAS] {request.Subject}",
                    Description = request.Description,
                    Category = category,
                    Priority = ValidatePriority(request.Priority),
                    Status = "Open",
                    ConversationSessionId = request.SessionId,
                    CreatedAt = DateTime.Now
                };

                _db.Tickets.Add(ticket);
                await _db.SaveChangesAsync();

                return new TicketResult
                {
                    Success = true,
                    TicketNumber = ticketNumber,
                    Message = $"Ticket berhasil dibuat dengan nomor: {ticketNumber}. Tim IT Help Desk akan segera menghubungi Anda.",
                    Status = "Open",
                    CreatedAt = ticket.CreatedAt
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TicketService] Error: {ex.Message}");
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
                var ticket = await Task.Run(() =>
                    _db.Tickets.FirstOrDefault(t => t.TicketNumber == ticketNumber));

                if (ticket == null)
                {
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
                System.Diagnostics.Debug.WriteLine($"[TicketService] GetTicket Error: {ex.Message}");
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
    }
}
