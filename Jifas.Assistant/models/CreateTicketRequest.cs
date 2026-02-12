namespace Jifas.Assistant.Models
{
    /// <summary>
    /// Request model for creating a support ticket
    /// </summary>
    public class CreateTicketRequest
    {
        /// <summary>
        /// User ID (Windows AD username)
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// Ticket title/subject
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Ticket description
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Ticket category
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Ticket priority (LOW, MEDIUM, HIGH, CRITICAL)
        /// </summary>
        public string Priority { get; set; } = "MEDIUM";

        /// <summary>
        /// Session ID for tracking
        /// </summary>
        public string SessionId { get; set; }
    }
}
