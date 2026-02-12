namespace Jifas.Assistant.Models
{
    /// <summary>
    /// Result of ticket creation operation
    /// </summary>
    public class TicketCreationResult
    {
        public bool Success { get; set; }
        public int? TicketId { get; set; }
        public string TicketNumber { get; set; }
        public string Message { get; set; }
        public string Status { get; set; }
        public System.DateTime? CreatedAt { get; set; }
    }
}
