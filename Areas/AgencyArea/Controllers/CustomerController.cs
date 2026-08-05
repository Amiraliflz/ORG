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

        public CustomerController(AppDbContext context, UserManager<IdentityUser> userManager,
            IConfiguration configuration, IPaymentService paymentService, CustomerBalanceService balanceSvc,
            MrShooferAPIClient apiClient, ILogger<CustomerController> logger)
        {
            _context = context;
            _userManager = userManager;
            _configuration = configuration;
            _paymentService = paymentService;
            _balanceSvc = balanceSvc;
            _apiClient = apiClient;
            _logger = logger;
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
            var paymentServerBase = _configuration["PaymentServer:BaseUrl"] ?? "https://mrshoofer.ir";
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
    }
}
