using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace jifas_assistant.DAL.Models
{
    /// <summary>
    /// Chat conversation history model untuk tracking semua pertanyaan dan jawaban
    /// </summary>
    [Table("ChatHistory")]
    public class ChatHistory
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>
        /// Session ID untuk grouping conversations
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string SessionId { get; set; }

        /// <summary>
        /// User ID atau identifier
        /// </summary>
        [MaxLength(100)]
        public string UserId { get; set; }

        /// <summary>
        /// User message / pertanyaan
        /// </summary>
        [Required]
        public string UserMessage { get; set; }

        /// <summary>
        /// AI response / jawaban
        /// </summary>
        [Required]
        public string AiResponse { get; set; }

        /// <summary>
        /// Source dari response (Knowledge Base, Out of Scope, Error, dll)
        /// </summary>
        [MaxLength(100)]
        public string ResponseSource { get; set; }

        /// <summary>
        /// Confidence score dari response (0-1)
        /// </summary>
        public double? ConfidenceScore { get; set; }

        /// <summary>
        /// Apakah response dari Knowledge Base
        /// </summary>
        public bool IsFromKnowledgeBase { get; set; }

        /// <summary>
        /// Total response time dalam milliseconds
        /// </summary>
        public long ResponseTimeMs { get; set; }

        /// <summary>
        /// Timestamp ketika chat terjadi
        /// </summary>
        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Flag untuk tracking success/failure
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Knowledge Base documents yang digunakan (JSON serialized document IDs)
        /// </summary>
        public string UsedDocumentIds { get; set; }
    }
}
