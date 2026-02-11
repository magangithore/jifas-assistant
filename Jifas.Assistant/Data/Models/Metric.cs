using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jifas.Assistant.Data.Models
{
    /// <summary>
    /// Metrics entity for tracking analytics
    /// </summary>
    [Table("Metrics")]
    public class Metric
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string MetricType { get; set; }

        [StringLength(255)]
        public string MetricName { get; set; }

        public int Count { get; set; }

        public double Value { get; set; }

        [StringLength(255)]
        public string Category { get; set; }

        [StringLength(500)]
        public string Tags { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }
    }
}
