namespace Jifas.Assistant.Models
{
    /// <summary>
    /// Request untuk membuat tiket support JIFAS dari percakapan chatbot.
    /// </summary>
    public class TicketRequest
    {
        /// <summary>
        /// Identitas user pemohon.
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// Session id percakapan chatbot.
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// Ringkasan singkat masalah.
        /// </summary>
        public string Subject { get; set; } = string.Empty;

        /// <summary>
        /// Detail masalah yang akan dikirim ke ticketing system.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Kategori masalah, misalnya access, error, feature request, atau training.
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Prioritas tiket.
        /// </summary>
        public string Priority { get; set; } = "Medium";
    }

    /// <summary>
    /// Response operasi tiket.
    /// </summary>
    public class TicketResponse
    {
        public bool Success { get; set; }
        public string TicketNumber { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
    }
}
