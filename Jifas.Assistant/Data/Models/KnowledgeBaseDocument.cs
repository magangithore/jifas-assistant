using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jifas.Assistant.Data.Models
{
    /// <summary>
    /// Knowledge base document entity
    /// </summary>
    [Table("KnowledgeBaseDocuments")]
    public class KnowledgeBaseDocument
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(500)]
        public string Title { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(max)")]
        public string Content { get; set; }

        [StringLength(100)]
        public string Category { get; set; }

        [StringLength(500)]
        public string Tags { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string Embedding { get; set; }

        public int EmbeddingDimensions { get; set; }

        public double RelevanceScore { get; set; }

        public int ViewCount { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        [StringLength(255)]
        public string CreatedBy { get; set; }

        [StringLength(255)]
        public string UpdatedBy { get; set; }
    }
}
