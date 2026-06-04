namespace Jifas.Assistant.Models
{
    /// <summary>
    /// Request internal untuk membuat tiket support.
    /// </summary>
    public class CreateTicketRequest
    {
        /// <summary>
        /// User ID, biasanya username Windows/AD.
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// Judul tiket.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Deskripsi masalah.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Kategori tiket.
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Prioritas tiket.
        /// </summary>
        public string Priority { get; set; } = "MEDIUM";

        /// <summary>
        /// Session id untuk audit percakapan.
        /// </summary>
        public string SessionId { get; set; } = string.Empty;
    }
}
