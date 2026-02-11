using System.Collections.Generic;
using Newtonsoft.Json;
using Jifas.Assistant.Services;

namespace Jifas.Assistant.Models
{
    /// <summary>
    /// Response model for JIFAS AI Assistant
    /// </summary>
    public class ChatResponse
    {
        /// <summary>
        /// Name of the AI assistant
        /// </summary>
        public string Sender { get; set; } = "JIFAS AI Assistant";

        /// <summary>
        /// The AI response message
        /// </summary>
        [JsonProperty("message")]
        public string Message { get; set; }

        /// <summary>
        /// Source of the response (Knowledge Base, AI Generated, Out of Scope, etc.)
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// Timestamp of the response
        /// </summary>
        public string Timestamp { get; set; }

        /// <summary>
        /// Indicates if the request was processed successfully
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Session ID for conversation tracking
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// Indicates if the response was derived from Knowledge Base
        /// </summary>
        public bool IsFromKnowledgeBase { get; set; }

        /// <summary>
        /// Confidence score of the response (0-1)
        /// </summary>
        public double ConfidenceScore { get; set; }

        /// <summary>
        /// Follow-up question suggestions for user
        /// </summary>
        public List<string> Suggestions { get; set; } = new List<string>();

        /// <summary>
        /// Ticket information if a ticket was created from this response
        /// </summary>
        public TicketInfo Ticket { get; set; }

        /// <summary>
        /// Knowledge Base results used to generate this response
        /// </summary>
        public List<KnowledgeBaseResult> KnowledgeBaseResults { get; set; } = new List<KnowledgeBaseResult>();
    }

    /// <summary>
    /// Ticket information embedded in chat response
    /// </summary>
    public class TicketInfo
    {
        /// <summary>
        /// Generated ticket number
        /// </summary>
        public string TicketNumber { get; set; }

        /// <summary>
        /// Current status of the ticket
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Response message about the ticket
        /// </summary>
        public string Message { get; set; }
    }
}