using Application.Data;
using Application.Models;
using Application.Services;
using Application.Services.MrShooferORS;
using Application.Services.Payment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Application.Areas.AgencyArea
{
    [Area("AgencyArea")]
    [Authorize(Policy = "Customer")]
    public class CustomerController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IConfiguration _configuration;
        private readonly IPaymentService _paymentService;
        private readonly CustomerBalanceService _balanceSvc;
        private readonly MrShooferAPIClient _apiClient;
        private readonly ILogger<CustomerController> _logger;
        private readonly LoyaltyService _loyaltySvc;

        public CustomerController(AppDbContext context, UserManager<IdentityUser> userManager,
            IConfiguration configuration, IPaymentService paymentService, CustomerBalanceService balanceSvc,
            MrShooferAPIClient apiClient, ILogger<CustomerController> logger, LoyaltyService loyaltySvc)
        {
            _context = context;
            _userManager = userManager;
            _configuration = configuration;
            _paymentService = paymentService;
            _balanceSvc = balanceSvc;
            _apiClient = apiClient;
            _logger = logger;
            _loyaltySvc = loyaltySvc;
        }

        public async Task<IActionResult> MyTickets()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var tickets = await _context.Tickets
                .Where(t => t.PhoneNumber == user.UserName && t.IsPaid)
                .OrderByDescending(t => t.RegisteredAt)
                .ToListAsync();

            // Sync cancellation status from ORS for active tickets
            var sellerToken = _configuration["MrShoofer:SellerToken"];
            if (!string.IsNullOrWhiteSpace(sellerToken))
            {
                _apiClient.SetSellerApiKey(sellerToken);
                var syncTasks = tickets
                    .Where(t => !t.IsCancelled)
                    .Select(async t =>
                    {
                        var isCancelled = await _apiClient.GetTicketIsCancelledAsync(t.TicketCode);
                        if (isCancelled == true) t.IsCancelled = true;
                    });
                await Task.WhenAll(syncTasks);
                await _context.SaveChangesAsync();
            }

            ViewBag.CustomerPhone = user.UserName;
            ViewBag.Balance = await _balanceSvc.GetBalance(user.Id);
            return View(tickets);
        }

        public async Task<IActionResult> MyWallet()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            ViewBag.Balance = await _balanceSvc.GetBalance(user.Id);
            ViewBag.CustomerPhone = user.UserName;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InitiateTopUp(string? amount)
        {
            // Normalize Persian/Arabic digits and parse
            var normalized = (amount ?? "")
                .Replace('۰','0').Replace('۱','1').Replace('۲','2').Replace('۳','3').Replace('۴','4')
                .Replace('۵','5').Replace('۶','6').Replace('۷','7').Replace('۸','8').Replace('۹','9')
                .Replace('٠','0').Replace('١','1').Replace('٢','2').Replace('٣','3').Replace('٤','4')
                .Replace('٥','5').Replace('٦','6').Replace('٧','7').Replace('٨','8').Replace('٩','9')
                .Replace(",", "").Trim();

            _logger.LogInformation("InitiateTopUp: raw={Raw} normalized={Normalized}", amount, normalized);

            if (!int.TryParse(normalized, out var amountInt) || amountInt < 1000)
            {
                TempData["Error"] = "حداقل مبلغ شارژ ۱۰۰۰ تومان است";
                return RedirectToAction("MyWallet");
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            // Payment start/callback stay on the user-facing sale domain
            var paymentServerBase = _configuration["PaymentServer:BaseUrl"]
                ?? Application.Services.Seo.SeoDefaults.PreferredOrigin;
            var sharedKey = _configuration["PaymentServer:SharedKey"] ?? string.Empty;
            var partnerBrand = Request.Cookies["partner_brand"] ?? string.Empty;
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var payload = $"{user.Id}:{amountInt}:{timestamp}:{partnerBrand}";
            var sig = Application.Controllers.PaymentController.ComputeHmac(payload, sharedKey);
            var startUrl =
                $"{paymentServerBase}/Payment/StartTopUp?userId={Uri.EscapeDataString(user.Id)}" +
                $"&amount={amountInt}&t={timestamp}&partner={Uri.EscapeDataString(partnerBrand)}" +
                $"&sig={Uri.EscapeDataString(sig)}";

            _logger.LogInformation("Redirecting wallet top-up to payment server. User={User}, Amount={Amount}", user.UserName, amountInt);
            return Redirect(startUrl);
        }

        [HttpGet]
        public async Task<IActionResult> MyProfile()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var balance = await _balanceSvc.GetBalance(user.Id);

            var ticketCount = await _context.Tickets
                .CountAsync(t => t.PhoneNumber == user.UserName && t.IsPaid);

            var profile = await _context.CustomerProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);

            ViewBag.CustomerPhone = user.UserName;
            ViewBag.Balance = balance;
            ViewBag.TicketCount = ticketCount;
            ViewBag.Profile = profile;
            if (LoyaltyService.FeatureEnabled)
                ViewBag.Loyalty = await _loyaltySvc.GetInfoAsync(user.UserName);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MyProfile(string? firstName, string? lastName, string? nationalId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var profile = await _context.CustomerProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            if (profile == null)
            {
                profile = new CustomerProfile { UserId = user.Id, CreatedAt = DateTime.Now };
                _context.CustomerProfiles.Add(profile);
            }

            profile.FirstName = firstName?.Trim();
            profile.LastName = lastName?.Trim();
            profile.NationalId = nationalId?.Trim();
            profile.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            TempData["Success"] = "اطلاعات پروفایل با موفقیت ذخیره شد";
            return RedirectToAction("MyProfile");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelTicket(int ticketId, string? reason, bool acceptedPolicy = false)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            if (!acceptedPolicy)
            {
                TempData["Error"] = "برای لغو بلیط باید شرایط کنسلی را بپذیرید";
                return RedirectToAction(nameof(MyTickets));
            }

            var trimmedReason = (reason ?? string.Empty).Trim();
            if (trimmedReason.Length < 3)
            {
                TempData["Error"] = "لطفاً دلیل لغو بلیط را وارد کنید";
                return RedirectToAction(nameof(MyTickets));
            }
            if (trimmedReason.Length > 500)
                trimmedReason = trimmedReason[..500];

            var ticket = await _context.Tickets.FirstOrDefaultAsync(t =>
                t.Id == ticketId &&
                t.PhoneNumber == user.UserName &&
                t.IsPaid);

            if (ticket == null)
            {
                TempData["Error"] = "بلیط یافت نشد";
                return RedirectToAction(nameof(MyTickets));
            }

            if (ticket.IsCancelled)
            {
                TempData["Error"] = "این بلیط قبلاً لغو شده است";
                return RedirectToAction(nameof(MyTickets));
            }

            var code = ticket.TicketCode ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code) ||
                code.StartsWith("PENDING-", StringComparison.OrdinalIgnoreCase) ||
                code.StartsWith("PAID-NO-RESERVE-", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "این بلیط در سامانه ORS ثبت نشده و قابل لغو نیست. لطفاً با پشتیبانی تماس بگیرید.";
                return RedirectToAction(nameof(MyTickets));
            }

            var sellerToken = _configuration["MrShoofer:SellerToken"];
            if (string.IsNullOrWhiteSpace(sellerToken))
            {
                TempData["Error"] = "سرویس لغو بلیط در دسترس نیست. لطفاً بعداً تلاش کنید.";
                return RedirectToAction(nameof(MyTickets));
            }

            _apiClient.SetSellerApiKey(sellerToken);
            var result = await _apiClient.CancelTicketAsync(code, trimmedReason);
            if (!result.Success)
            {
                _logger.LogWarning("Passenger cancel failed. TicketId={TicketId} Code={Code} Error={Error}",
                    ticket.Id, code, result.ErrorMessage);
                TempData["Error"] = result.ErrorMessage ?? "لغو بلیط ناموفق بود";
                return RedirectToAction(nameof(MyTickets));
            }

            ticket.IsCancelled = true;
            ticket.CancelReason = trimmedReason;
            await _context.SaveChangesAsync();

            if (result.RefundAmount > 0)
            {
                await _balanceSvc.AddBalance(user.Id, result.RefundAmount);
                TempData["Success"] =
                    $"بلیط با موفقیت لغو شد. مبلغ {result.RefundAmount:N0} تومان به کیف پول شما بازگردانده شد.";
            }
            else
            {
                TempData["Success"] = "بلیط با موفقیت لغو شد.";
            }

            return RedirectToAction(nameof(MyTickets));
        }
    }
}
