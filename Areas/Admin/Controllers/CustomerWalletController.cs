using Application.Data;
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
    public class CustomerWalletController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<CustomerWalletController> _logger;
        private readonly CustomerBalanceService _balanceSvc;
        private readonly AppDbContext _context;

        public CustomerWalletController(UserManager<IdentityUser> userManager, ILogger<CustomerWalletController> logger, CustomerBalanceService balanceSvc, AppDbContext context)
        {
            _userManager = userManager;
            _logger = logger;
            _balanceSvc = balanceSvc;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var profiles = await _context.CustomerProfiles.ToListAsync();
            var profileMap = profiles.ToDictionary(p => p.UserId);

            var customers = new List<(IdentityUser User, decimal Balance)>();
            foreach (var user in _userManager.Users.ToList())
            {
                var claims = await _userManager.GetClaimsAsync(user);
                if (!claims.Any(c => c.Type == "Role" && c.Value == "Customer")) continue;

                var balance = profileMap.TryGetValue(user.Id, out var profile) ? profile.Balance : 0m;
                customers.Add((user, balance));
            }

            return View(customers.OrderByDescending(c => c.Balance).ToList());
        }

        [HttpPost]
        public async Task<IActionResult> ChargeBalance(string phone, decimal amount, string? note)
        {
            if (amount <= 0) return BadRequest("مبلغ باید بیشتر از صفر باشد");

            var user = await _userManager.FindByNameAsync(phone);
            if (user == null) return NotFound("کاربر یافت نشد");

            var claims = await _userManager.GetClaimsAsync(user);
            if (!claims.Any(c => c.Type == "Role" && c.Value == "Customer"))
                return BadRequest("این کاربر مشتری نیست");

            var newBalance = await _balanceSvc.AddBalance(user.Id, amount);

            _logger.LogInformation("Admin charged wallet for {Phone}: +{Amount} (note: {Note}). New balance: {Balance}",
                phone, amount, note, newBalance);

            TempData["Success"] = $"کیف پول {phone} به مبلغ {amount:N0} تومان شارژ شد. موجودی جدید: {newBalance:N0} تومان";
            return RedirectToAction("Index");
        }
    }
}
