using System.ComponentModel.DataAnnotations;

namespace Application.Models
{
    public class CustomerProfile
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = null!;

        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? NationalId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }
    }
}
