namespace Application.Models
{
    public enum LoyaltyTier
    {
        Bronze = 0,
        Silver = 1,
        Gold = 2,
        Platinum = 3
    }

    public static class LoyaltyTierExtensions
    {
        public static int DiscountPercent(this LoyaltyTier tier) => tier switch
        {
            LoyaltyTier.Silver => 3,
            LoyaltyTier.Gold => 7,
            LoyaltyTier.Platinum => 12,
            _ => 0
        };

        public static string PersianName(this LoyaltyTier tier) => tier switch
        {
            LoyaltyTier.Silver => "نقره‌ای",
            LoyaltyTier.Gold => "طلایی",
            LoyaltyTier.Platinum => "الماسی",
            _ => "برنزی"
        };

        public static int MinTrips(this LoyaltyTier tier) => tier switch
        {
            LoyaltyTier.Silver => 5,
            LoyaltyTier.Gold => 15,
            LoyaltyTier.Platinum => 30,
            _ => 0
        };

        public static LoyaltyTier FromTripCount(int completedTrips) =>
            completedTrips >= 30 ? LoyaltyTier.Platinum :
            completedTrips >= 15 ? LoyaltyTier.Gold :
            completedTrips >= 5 ? LoyaltyTier.Silver :
            LoyaltyTier.Bronze;

        public static int TripsToNextTier(this LoyaltyTier tier, int currentTrips) => tier switch
        {
            LoyaltyTier.Bronze => 5 - currentTrips,
            LoyaltyTier.Silver => 15 - currentTrips,
            LoyaltyTier.Gold => 30 - currentTrips,
            _ => 0
        };
    }
}
