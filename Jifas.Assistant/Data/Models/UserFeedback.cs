using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jifas.Assistant.Data.Models
{
    /// <summary>
    /// User feedback entity for JIFAS AI Assistant responses
    /// </summary>
    [Table("UserFeedbacks")]
    public class UserFeedback
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ChatId { get; set; }

        [Required]
        [StringLength(255)]
        public string UserId { get; set; }

        [StringLength(100)]
        public string Rating { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string Comment { get; set; }

        public bool IsHelpful { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(ChatId))]
        public virtual Chat Chat { get; set; }
    }
}
