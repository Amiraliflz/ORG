using Application.Data;
using Application.Models;
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

        public CustomerController(AppDbContext context, UserManager<IdentityUser> userManager,
            IConfiguration configuration, IPaymentService paymentService)
        {
            _context = context;
            _userManager = userManager;
            _configuration = configuration;
            _paymentService = paymentService;
        }

        public async Task<IActionResult> MyTickets()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var tickets = await _context.Tickets
                .Where(t => t.PhoneNumber == user.UserName && t.IsPaid)
                .OrderByDescending(t => t.RegisteredAt)
                .ToListAsync();

            ViewBag.CustomerPhone = user.UserName;
            return View(tickets);
        }

        public async Task<IActionResult> MyWallet()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var claims = await _userManager.GetClaimsAsync(user);
            var balanceClaim = claims.FirstOrDefault(c => c.Type == "CustomerBalance");
            decimal.TryParse(balanceClaim?.Value, out var balance);

            ViewBag.Balance = balance;
            ViewBag.CustomerPhone = user.UserName;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InitiateTopUp(int amount)
        {
            if (amount < 1000)
            {
                TempData["Error"] = "حداقل مبلغ شارژ ۱۰۰۰ تومان است";
                return RedirectToAction("MyWallet");
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var baseUrl = _configuration["PaymentServer:BaseUrl"] ?? string.Empty;
            var walletCallbackUrl = $"{baseUrl}/Payment/TopUpVerify";
            var description = $"شارژ کیف پول مسترشوفر - {user.UserName}";

            var (success, authority, message) = await _paymentService.RequestPaymentAsync(
                amount * 10,
                description,
                user.UserName!,
                null,
                walletCallbackUrl);

            if (!success)
            {
                TempData["Error"] = message;
                return RedirectToAction("MyWallet");
            }

            // Store pending top-up claim (remove old one first)
            var existingClaims = await _userManager.GetClaimsAsync(user);
            var existingPending = existingClaims.FirstOrDefault(c => c.Type == "WalletTopUpPending");
            if (existingPending != null)
                await _userManager.RemoveClaimAsync(user, existingPending);
            await _userManager.AddClaimAsync(user, new Claim("WalletTopUpPending", $"{authority}:{amount}"));

            // Sandbox bypass
            if (authority.StartsWith("TEST-", StringComparison.OrdinalIgnoreCase))
                return Redirect($"/Payment/TopUpVerify?Authority={authority}&Status=OK");

            var gatewayUrl = _paymentService.GetPaymentGatewayUrl(authority);
            return Redirect(gatewayUrl);
        }

        [HttpGet]
        public async Task<IActionResult> MyProfile()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var claims = await _userManager.GetClaimsAsync(user);
            var balanceClaim = claims.FirstOrDefault(c => c.Type == "CustomerBalance");
            decimal.TryParse(balanceClaim?.Value, out var balance);

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
