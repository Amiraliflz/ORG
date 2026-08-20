using Application.Data;
using Application.Models;
using Application.Services.Ops;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly IOpsMobileTokenService _mobileTokens;

        public OpsController(
            PlatformAnalyticsService analytics,
            OpsStatusService status,
            AppDbContext db,
            IServiceRestarter restarter,
            IBusinessEventLogger businessLog,
            ILogger<OpsController> logger,
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
            IOpsMobileTokenService mobileTokens)
        {
            _analytics = analytics;
            _status = status;
            _db = db;
            _restarter = restarter;
            _businessLog = businessLog;
            _logger = logger;
            _userManager = userManager;
            _signInManager = signInManager;
            _mobileTokens = mobileTokens;
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

        /// <summary>JSON login for the Ops Android APK — returns a bearer token (no cookie required).</summary>
        [HttpPost]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ApiLogin([FromBody] OpsApiLoginRequest body)
        {
            if (body is null || string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrWhiteSpace(body.Password))
                return BadRequest(new { success = false, message = "نام کاربری و رمز لازم است" });

            var user = await _userManager.FindByNameAsync(body.Username.Trim());
            if (user is null)
                return Unauthorized(new { success = false, message = "نام کاربری یا رمز عبور اشتباه است" });

            var claims = await _userManager.GetClaimsAsync(user);
            if (!claims.Any(c => c.Type == "Role" && c.Value == "Admin"))
                return Unauthorized(new { success = false, message = "دسترسی ادمین ندارید" });

            if (!await _userManager.CheckPasswordAsync(user, body.Password))
                return Unauthorized(new { success = false, message = "نام کاربری یا رمز عبور اشتباه است" });

            // Cookie optional (web); mobile uses token
            await _signInManager.SignInAsync(user, isPersistent: true);

            var token = _mobileTokens.Issue(user.Id, user.UserName ?? body.Username);
            _businessLog.LogEvent("Ops", "Mobile API login", new { user.Id, user.UserName });
            return Json(new { success = true, username = user.UserName, token });
        }

        /// <summary>Status for APK — cookie OR Bearer token.</summary>
        [HttpGet]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ApiStatus(CancellationToken ct)
        {
            if (!TryAuthorizeMobile(out var userId))
                return Unauthorized(new { success = false, message = "نشست منقضی شده — دوباره وارد شوید" });

            var status = await _status.GetStatusAsync(ct);
            return Json(status);
        }

        [HttpPost]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ApiRestart([FromBody] OpsApiRestartRequest body, CancellationToken ct)
        {
            if (!TryAuthorizeMobile(out var userId))
                return Unauthorized(new { success = false, message = "نشست منقضی شده — دوباره وارد شوید" });

            if (body is null || !string.Equals(body.Confirm, "RESTART", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, message = "confirm باید RESTART باشد" });

            _businessLog.LogEvent("Ops", "API Restart requested", new { userId });
            _logger.LogWarning("API service restart requested by {UserId}", userId);

            var (success, message) = await _restarter.RestartAsync(ct);
            _db.OperationAudits.Add(new OperationAudit
            {
                Action = "RestartApi",
                UserId = userId,
                Success = success,
                Details = message
            });
            await _db.SaveChangesAsync(ct);

            return Json(new { success, message });
        }

        private bool TryAuthorizeMobile(out string userId)
        {
            userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            if (User.Identity?.IsAuthenticated == true
                && User.HasClaim("Role", "Admin")
                && !string.IsNullOrEmpty(userId))
                return true;

            var header = Request.Headers.Authorization.ToString();
            if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = header["Bearer ".Length..].Trim();
                if (_mobileTokens.TryValidate(token, out userId, out _))
                    return true;
            }

            // Also accept X-Ops-Token (some clients)
            var alt = Request.Headers["X-Ops-Token"].ToString();
            if (!string.IsNullOrEmpty(alt) && _mobileTokens.TryValidate(alt, out userId, out _))
                return true;

            userId = "";
            return false;
        }
    }

    public class OpsApiLoginRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class OpsApiRestartRequest
    {
        public string Confirm { get; set; } = "";
    }
}
