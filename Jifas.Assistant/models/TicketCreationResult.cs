namespace Jifas.Assistant.Models
{
    /// <summary>
    /// Hasil pembuatan tiket dari integrasi ticketing.
    /// </summary>
    public class TicketCreationResult
    {
        public bool Success { get; set; }
        public int? TicketId { get; set; }
        public string TicketNumber { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public System.DateTime? CreatedAt { get; set; }
    }
}
