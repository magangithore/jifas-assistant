using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jifas.Assistant.Data.Models
{
    /// <summary>
    /// Chat history entity for JIFAS AI Assistant
    /// </summary>
    [Table("Chats")]
    public class Chat
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(255)]
        public string UserId { get; set; }

        [Required]
        [StringLength(500)]
        public string UserMessage { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(max)")]
        public string AssistantResponse { get; set; }

        [StringLength(100)]
        public string SessionId { get; set; }

        [StringLength(100)]
        public string Source { get; set; }

        public double? ConfidenceScore { get; set; }

        public bool IsFromKnowledgeBase { get; set; }

        [StringLength(50)]
        public string Category { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        [StringLength(500)]
        public string Remarks { get; set; }
    }
}
