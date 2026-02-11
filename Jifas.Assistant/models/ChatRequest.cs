namespace Jifas.Chatbot.Models
{
    /// <summary>
    /// Request model for JIFAS AI Assistant
    /// </summary>
    public class ChatRequest
    {
        /// <summary>
        /// The user's message/question
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// User identifier (Windows AD username)
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// Session ID for conversation tracking
        /// </summary>
        public string SessionId { get; set; }
    }
}