namespace Application.Models
{
    public class DiscountCode
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public int DiscountPercent { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public int? MaxUses { get; set; }
        public int UsedCount { get; set; } = 0;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string? Description { get; set; }
        public ICollection<DiscountCodeUsage> Usages { get; set; } = [];
    }
}
