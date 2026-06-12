using Application.Data;
using Application.Models;
using Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Application.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Policy = "Admin")]
    public class CustomersController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly AppDbContext _context;
        private readonly ILogger<CustomersController> _logger;
        private readonly CustomerBalanceService _balanceSvc;

        public CustomersController(UserManager<IdentityUser> userManager, AppDbContext context, ILogger<CustomersController> logger, CustomerBalanceService balanceSvc)
        {
            _userManager = userManager;
            _context = context;
            _logger = logger;
            _balanceSvc = balanceSvc;
        }

        public async Task<IActionResult> Index(string? search)
        {
            var profiles = await _context.CustomerProfiles.ToListAsync();
            var profileMap = profiles.ToDictionary(p => p.UserId);

            var result = new List<CustomerListItem>();
            foreach (var user in _userManager.Users.ToList())
            {
                var claims = await _userManager.GetClaimsAsync(user);
                if (!claims.Any(c => c.Type == "Role" && c.Value == "Customer")) continue;

                profileMap.TryGetValue(user.Id, out var profile);
                var balance = profile?.Balance ?? 0m;

                result.Add(new CustomerListItem { User = user, Balance = balance, Profile = profile });
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim().ToLower();
                result = result.Where(r =>
                    (r.User.UserName?.Contains(q) == true) ||
                    (r.Profile?.FirstName?.ToLower().Contains(q) == true) ||
                    (r.Profile?.LastName?.ToLower().Contains(q) == true) ||
                    (r.Profile?.NationalId?.Contains(q) == true) ||
                    ($"{r.Profile?.FirstName} {r.Profile?.LastName}".ToLower().Contains(q))
                ).ToList();
            }

            ViewBag.Search = search;
            return View(result.OrderBy(r => r.User.UserName).ToList());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string phone, string? firstName, string? lastName, string? nationalId)
        {
            if (string.IsNullOrWhiteSpace(phone))
            {
                TempData["Error"] = "شماره موبایل الزامی است";
                return RedirectToAction("Index");
            }

            var existing = await _userManager.FindByNameAsync(phone);
            if (existing != null)
            {
                TempData["Error"] = $"کاربری با شماره {phone} از قبل وجود دارد";
                return RedirectToAction("Index");
            }

            var user = new IdentityUser { UserName = phone, PhoneNumber = phone };
            var password = Guid.NewGuid().ToString("N")[..8] + "Aa1!";
            var result = await _userManager.CreateAsync(user, password);

            if (!result.Succeeded)
            {
                TempData["Error"] = "خطا در ایجاد حساب: " + string.Join("، ", result.Errors.Select(e => e.Description));
                return RedirectToAction("Index");
            }

            await _userManager.AddClaimAsync(user, new Claim("Role", "Customer"));
            // Balance starts at 0 in CustomerProfile.Balance (DB)

            if (!string.IsNullOrWhiteSpace(firstName) || !string.IsNullOrWhiteSpace(nationalId))
            {
                _context.CustomerProfiles.Add(new CustomerProfile
                {
                    UserId = user.Id,
                    FirstName = firstName?.Trim(),
                    LastName = lastName?.Trim(),
                    NationalId = nationalId?.Trim(),
                    CreatedAt = DateTime.Now
                });
                await _context.SaveChangesAsync();
            }

            _logger.LogInformation("Admin created customer account for {Phone}", phone);
            TempData["Success"] = $"حساب مشتری {phone} با موفقیت ایجاد شد";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChargeBalance(string phone, decimal amount)
        {
            if (amount <= 0)
            {
                TempData["Error"] = "مبلغ باید بیشتر از صفر باشد";
                return RedirectToAction("Index");
            }

            var user = await _userManager.FindByNameAsync(phone);
            if (user == null)
            {
                TempData["Error"] = "کاربر یافت نشد";
                return RedirectToAction("Index");
            }

            var claims = await _userManager.GetClaimsAsync(user);
            if (!claims.Any(c => c.Type == "Role" && c.Value == "Customer"))
            {
                TempData["Error"] = "این کاربر مشتری نیست";
                return RedirectToAction("Index");
            }

            await _balanceSvc.AddBalance(user.Id, amount);

            _logger.LogInformation("Admin charged wallet {Phone} +{Amount}", phone, amount);
            TempData["Success"] = $"کیف پول {phone} به مبلغ {amount:N0} تومان شارژ شد";
            return RedirectToAction("Index");
        }
    }

    public class CustomerListItem
    {
        public IdentityUser User { get; set; } = null!;
        public decimal Balance { get; set; }
        public CustomerProfile? Profile { get; set; }
    }
}
