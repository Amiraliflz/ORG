using System.ComponentModel.DataAnnotations;

namespace Application.Models
{
    public class AppSetting
    {
        [Key]
        [MaxLength(100)]
        public string Key { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Value { get; set; } = string.Empty;

        public static class Keys
        {
            public const string LoyaltyDiscountEnabled = "LoyaltyDiscountEnabled";
        }
    }
}
