using System;

namespace Jifas.Chatbot.Models
{
    /// <summary>
    /// Model for tracking suggestion quality metrics
    /// </summary>
    public class SuggestionMetric
    {
        /// <summary>
        /// Unique metric ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Session ID for tracking conversations
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// User ID (if available)
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// The original query/message
        /// </summary>
        public string Query { get; set; }

        /// <summary>
        /// The suggestion provided to user
        /// </summary>
        public string Suggestion { get; set; }

        /// <summary>
        /// Whether user clicked on this suggestion (true = helpful, false = not helpful)
        /// </summary>
        public bool? IsHelpful { get; set; }

        /// <summary>
        /// Number of times this suggestion was shown
        /// </summary>
        public int DisplayCount { get; set; } = 1;

        /// <summary>
        /// Number of times user clicked on this suggestion
        /// </summary>
        public int ClickCount { get; set; } = 0;

        /// <summary>
        /// Click-through rate (ClickCount / DisplayCount)
        /// </summary>
        public decimal ClickThroughRate { get; set; } = 0m;

        /// <summary>
        /// Timestamp when suggestion was logged
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Timestamp when metric was last updated
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Additional notes/context
        /// </summary>
        public string Notes { get; set; }
    }
}
