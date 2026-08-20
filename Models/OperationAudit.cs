using System.ComponentModel.DataAnnotations;

namespace Application.Models
{
    public class OperationAudit
    {
        public long Id { get; set; }

        [MaxLength(50)]
        public string Action { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? UserId { get; set; }

        public DateTime At { get; set; } = DateTime.UtcNow;

        public bool Success { get; set; }

        [MaxLength(1000)]
        public string? Details { get; set; }
    }
}
