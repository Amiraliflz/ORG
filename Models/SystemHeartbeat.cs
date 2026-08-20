using System.ComponentModel.DataAnnotations;

namespace Application.Models
{
    public class SystemHeartbeat
    {
        public long Id { get; set; }

        public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

        public bool IsHealthy { get; set; }

        [MaxLength(50)]
        public string Component { get; set; } = string.Empty;

        public int? ResponseMs { get; set; }

        [MaxLength(500)]
        public string? Details { get; set; }
    }
}
