namespace Application.Models
{
    public class DiscountCodeUsage
    {
        public int Id { get; set; }
        public int DiscountCodeId { get; set; }
        public DiscountCode DiscountCode { get; set; } = null!;
        public string UserPhone { get; set; } = string.Empty;
        public DateTime UsedAt { get; set; } = DateTime.Now;
    }
}
