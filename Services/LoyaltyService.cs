using Application.Data;
using Application.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Application.Services
{
    public sealed class LoyaltyInfo
    {
        public LoyaltyTier Tier { get; init; }
        public int TripCount { get; init; }
        public int DiscountPercent { get; init; }
        public bool DiscountEnabled { get; init; }
        public int TripsToNextTier { get; init; }
        public string MonthLabel { get; init; } = string.Empty;
    }

    public class LoyaltyService
    {
        /// <summary>
        /// Kill switch: hide loyalty UI from customers and never apply tier discounts
        /// until this is flipped back to true.
        /// </summary>
        public const bool FeatureEnabled = false;

        private readonly AppDbContext _context;

        public LoyaltyService(AppDbContext context)
        {
            _context = context;
        }

        public static (DateTime Start, DateTime End) CurrentShamsiMonthRange(DateTime? now = null)
        {
            var dt = now ?? DateTime.Now;
            var pc = new PersianCalendar();
            var year = pc.GetYear(dt);
            var month = pc.GetMonth(dt);
            var start = pc.ToDateTime(year, month, 1, 0, 0, 0, 0);
            var days = pc.GetDaysInMonth(year, month);
            var end = pc.ToDateTime(year, month, days, 23, 59, 59, 999);
            return (start, end);
        }

        public async Task<bool> IsDiscountEnabledAsync(CancellationToken ct = default)
        {
            if (!FeatureEnabled) return false;

            var row = await _context.AppSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == AppSetting.Keys.LoyaltyDiscountEnabled, ct);
            return row != null && string.Equals(row.Value, "true", StringComparison.OrdinalIgnoreCase);
        }

        public async Task SetDiscountEnabledAsync(bool enabled, CancellationToken ct = default)
        {
            var row = await _context.AppSettings
                .FirstOrDefaultAsync(s => s.Key == AppSetting.Keys.LoyaltyDiscountEnabled, ct);
            if (row == null)
            {
                row = new AppSetting { Key = AppSetting.Keys.LoyaltyDiscountEnabled, Value = enabled ? "true" : "false" };
                _context.AppSettings.Add(row);
            }
            else
            {
                row.Value = enabled ? "true" : "false";
            }

            await _context.SaveChangesAsync(ct);
        }

        public async Task<int> CountTripsInCurrentShamsiMonthAsync(string? phone, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(phone)) return 0;

            var normalized = phone.Trim();
            var (start, end) = CurrentShamsiMonthRange();

            return await _context.Tickets.AsNoTracking().CountAsync(t =>
                t.PhoneNumber == normalized &&
                t.IsPaid &&
                !t.IsCancelled &&
                t.RegisteredAt >= start &&
                t.RegisteredAt <= end, ct);
        }

        public async Task<LoyaltyInfo> GetInfoAsync(string? phone, CancellationToken ct = default)
        {
            var tripCount = await CountTripsInCurrentShamsiMonthAsync(phone, ct);
            var tier = LoyaltyTierExtensions.FromTripCount(tripCount);
            var discountEnabled = FeatureEnabled && await IsDiscountEnabledAsync(ct);
            var now = DateTime.Now.ToPersianDate();

            return new LoyaltyInfo
            {
                Tier = tier,
                TripCount = tripCount,
                DiscountPercent = discountEnabled ? tier.DiscountPercent() : 0,
                DiscountEnabled = discountEnabled,
                TripsToNextTier = tier.TripsToNextTier(tripCount),
                MonthLabel = $"{now.MonthName} {now.Year}"
            };
        }

        public static int ApplyTierDiscount(int priceAfterPromo, int tierDiscountPercent)
        {
            if (!FeatureEnabled || tierDiscountPercent <= 0 || priceAfterPromo <= 0) return priceAfterPromo;
            return (int)Math.Round(priceAfterPromo * (1 - tierDiscountPercent / 100m));
        }

        public static int ApplyStackedDiscounts(int basePrice, int promoPercent, int tierPercent)
        {
            var afterPromo = promoPercent > 0
                ? (int)Math.Round(basePrice * (1 - promoPercent / 100m))
                : basePrice;
            return ApplyTierDiscount(afterPromo, FeatureEnabled ? tierPercent : 0);
        }
    }
}
