namespace Jifas.Assistant.Models
{
    /// <summary>
    /// Request model for creating a JIFAS support ticket
    /// </summary>
    public class TicketRequest
    {
        /// <summary>
        /// User identifier
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// Session ID from chat conversation
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// Ticket subject (brief description of issue)
        /// </summary>
        public string Subject { get; set; }

        /// <summary>
        /// Detailed description of the issue
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Category of the issue (access, error, feature_request, training, etc.)
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Priority level (Low, Medium, High)
        /// </summary>
        public string Priority { get; set; }
    }

    /// <summary>
    /// Response model for ticket operations
    /// </summary>
    public class TicketResponse
    {
        public bool Success { get; set; }
        public string TicketNumber { get; set; }
        public string Message { get; set; }
        public string Status { get; set; }
        public string CreatedAt { get; set; }
    }
}
