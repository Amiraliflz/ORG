using Application.Data;
using Application.Models;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Ops
{
    public class PlatformAnalyticsService
    {
        private readonly AppDbContext _db;

        public PlatformAnalyticsService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<PlatformAnalyticsSummary> GetSummaryAsync(CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var today = DateTime.Today;
            var weekAgo = today.AddDays(-7);
            var monthAgo = today.AddDays(-30);

            var tickets = _db.Tickets.AsNoTracking();

            var todaySold = await tickets.CountAsync(
                t => t.RegisteredAt >= today && !t.IsCancelled, ct);
            var weekSold = await tickets.CountAsync(
                t => t.RegisteredAt >= weekAgo && !t.IsCancelled, ct);
            var monthSold = await tickets.CountAsync(
                t => t.RegisteredAt >= monthAgo && !t.IsCancelled, ct);

            var todayRevenue = await tickets
                .Where(t => t.RegisteredAt >= today && t.IsPaid && !t.IsCancelled)
                .SumAsync(t => (long)t.TicketFinalPrice, ct);
            var weekRevenue = await tickets
                .Where(t => t.RegisteredAt >= weekAgo && t.IsPaid && !t.IsCancelled)
                .SumAsync(t => (long)t.TicketFinalPrice, ct);

            var cancellations = await tickets.CountAsync(
                t => t.IsCancelled && t.RegisteredAt >= weekAgo, ct);

            var newCustomers = await _db.CustomerProfiles.AsNoTracking()
                .CountAsync(c => c.CreatedAt >= weekAgo, ct);

            var walletCharges = await _db.AgencyBalanceCharges.AsNoTracking()
                .Where(c => c.ChargedAt >= weekAgo)
                .SumAsync(c => (long)c.Amount, ct);

            var errors24h = await _db.AppLogEntries.AsNoTracking()
                .CountAsync(l => l.Level == "Error" && l.Timestamp >= now.AddHours(-24), ct);

            var requests24h = await _db.AppLogEntries.AsNoTracking()
                .CountAsync(l => l.RequestPath != null && l.Timestamp >= now.AddHours(-24), ct);

            var dailySales = await tickets
                .Where(t => t.RegisteredAt >= weekAgo && !t.IsCancelled)
                .GroupBy(t => t.RegisteredAt.Date)
                .Select(g => new DailyCount { Date = g.Key, Count = g.Count() })
                .OrderBy(d => d.Date)
                .ToListAsync(ct);

            var topRoutes = await tickets
                .Where(t => t.RegisteredAt >= monthAgo && !t.IsCancelled)
                .GroupBy(t => new { t.TripOrigin, t.TripDestination })
                .Select(g => new RouteCount
                {
                    Origin = g.Key.TripOrigin,
                    Destination = g.Key.TripDestination,
                    Count = g.Count()
                })
                .OrderByDescending(r => r.Count)
                .Take(8)
                .ToListAsync(ct);

            return new PlatformAnalyticsSummary
            {
                TodaySold = todaySold,
                WeekSold = weekSold,
                MonthSold = monthSold,
                TodayRevenue = todayRevenue,
                WeekRevenue = weekRevenue,
                CancellationsWeek = cancellations,
                NewCustomersWeek = newCustomers,
                WalletChargesWeek = walletCharges,
                Errors24h = errors24h,
                Requests24h = requests24h,
                DailySales = dailySales,
                TopRoutes = topRoutes
            };
        }

        public async Task<double> GetUptimePercent24hAsync(CancellationToken ct = default)
        {
            var since = DateTime.UtcNow.AddHours(-24);
            var beats = await _db.SystemHeartbeats.AsNoTracking()
                .Where(h => h.CheckedAt >= since && h.Component == "app")
                .ToListAsync(ct);

            if (beats.Count == 0) return 100;
            var healthy = beats.Count(h => h.IsHealthy);
            return Math.Round(100.0 * healthy / beats.Count, 1);
        }
    }

    public class PlatformAnalyticsSummary
    {
        public int TodaySold { get; set; }
        public int WeekSold { get; set; }
        public int MonthSold { get; set; }
        public long TodayRevenue { get; set; }
        public long WeekRevenue { get; set; }
        public int CancellationsWeek { get; set; }
        public int NewCustomersWeek { get; set; }
        public long WalletChargesWeek { get; set; }
        public int Errors24h { get; set; }
        public int Requests24h { get; set; }
        public List<DailyCount> DailySales { get; set; } = new();
        public List<RouteCount> TopRoutes { get; set; } = new();
    }

    public class DailyCount
    {
        public DateTime Date { get; set; }
        public int Count { get; set; }
    }

    public class RouteCount
    {
        public string Origin { get; set; } = "";
        public string Destination { get; set; } = "";
        public int Count { get; set; }
    }
}
