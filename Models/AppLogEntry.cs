using System.ComponentModel.DataAnnotations;

namespace Application.Models
{
    public class AppLogEntry
    {
        public long Id { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [MaxLength(20)]
        public string Level { get; set; } = "Information";

        [MaxLength(100)]
        public string? Category { get; set; }

        [MaxLength(4000)]
        public string Message { get; set; } = string.Empty;

        public string? Exception { get; set; }

        [MaxLength(500)]
        public string? RequestPath { get; set; }

        [MaxLength(10)]
        public string? RequestMethod { get; set; }

        public int? StatusCode { get; set; }

        public int? DurationMs { get; set; }

        [MaxLength(128)]
        public string? UserId { get; set; }

        public string? PropertiesJson { get; set; }
    }
}
