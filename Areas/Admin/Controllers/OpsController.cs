using Application.Data;
using Application.Models;
using Application.Services.Ops;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Application.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Policy = "Admin")]
    public class OpsController : Controller
    {
        private readonly PlatformAnalyticsService _analytics;
        private readonly OpsStatusService _status;
        private readonly AppDbContext _db;
        private readonly IServiceRestarter _restarter;
        private readonly IBusinessEventLogger _businessLog;
        private readonly ILogger<OpsController> _logger;

        public OpsController(
            PlatformAnalyticsService analytics,
            OpsStatusService status,
            AppDbContext db,
            IServiceRestarter restarter,
            IBusinessEventLogger businessLog,
            ILogger<OpsController> logger)
        {
            _analytics = analytics;
            _status = status;
            _db = db;
            _restarter = restarter;
            _businessLog = businessLog;
            _logger = logger;
        }

        public IActionResult Analytics() => View();

        [HttpGet]
        public async Task<IActionResult> AnalyticsJson(CancellationToken ct)
        {
            var summary = await _analytics.GetSummaryAsync(ct);
            return Json(new
            {
                summary.TodaySold,
                summary.WeekSold,
                summary.MonthSold,
                summary.TodayRevenue,
                summary.WeekRevenue,
                summary.CancellationsWeek,
                summary.NewCustomersWeek,
                summary.WalletChargesWeek,
                summary.Errors24h,
                summary.Requests24h,
                dailySales = summary.DailySales.Select(d => new
                {
                    date = d.Date.ToString("MM/dd"),
                    count = d.Count
                }),
                topRoutes = summary.TopRoutes.Select(r => new
                {
                    label = $"{r.Origin} → {r.Destination}",
                    count = r.Count
                })
            });
        }

        public async Task<IActionResult> Logs(string? level, string? q, int page = 1, CancellationToken ct = default)
        {
            const int pageSize = 50;
            var query = _db.AppLogEntries.AsNoTracking().OrderByDescending(l => l.Timestamp);

            if (!string.IsNullOrWhiteSpace(level))
                query = (IOrderedQueryable<AppLogEntry>)query.Where(l => l.Level == level);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = (IOrderedQueryable<AppLogEntry>)query.Where(l =>
                    l.Message.Contains(term)
                    || (l.RequestPath != null && l.RequestPath.Contains(term))
                    || (l.Category != null && l.Category.Contains(term)));
            }

            var total = await query.CountAsync(ct);
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

            ViewBag.Level = level;
            ViewBag.Query = q;
            ViewBag.Page = page;
            ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
            ViewBag.Total = total;

            return View(items);
        }

        public IActionResult Monitor()
        {
            ViewData["Layout"] = "_OpsMobileLayout";
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> StatusJson(CancellationToken ct)
        {
            var status = await _status.GetStatusAsync(ct);
            return Json(status);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restart([FromForm] string confirm, CancellationToken ct)
        {
            if (!string.Equals(confirm, "RESTART", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "برای تأیید، عبارت RESTART را وارد کنید.";
                return RedirectToAction(nameof(Monitor));
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _businessLog.LogEvent("Ops", "Restart requested", new { userId });
            _logger.LogWarning("Service restart requested by {UserId}", userId);

            var (success, message) = await _restarter.RestartAsync(ct);

            _db.OperationAudits.Add(new OperationAudit
            {
                Action = "Restart",
                UserId = userId,
                Success = success,
                Details = message
            });
            await _db.SaveChangesAsync(ct);

            if (success)
                TempData["Success"] = message;
            else
                TempData["Error"] = message;

            return RedirectToAction(nameof(Monitor));
        }
    }
}
