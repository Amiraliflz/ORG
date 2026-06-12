using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Application.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Policy = "Admin")]
    public class CustomerWalletController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<CustomerWalletController> _logger;

        public CustomerWalletController(UserManager<IdentityUser> userManager, ILogger<CustomerWalletController> logger)
        {
            _userManager = userManager;
            _logger = logger;
        }

        // List all customer users with their balances
        public async Task<IActionResult> Index()
        {
            var allUsers = _userManager.Users.ToList();
            var customers = new List<(IdentityUser User, decimal Balance)>();

            foreach (var user in allUsers)
            {
                var claims = await _userManager.GetClaimsAsync(user);
                if (claims.Any(c => c.Type == "Role" && c.Value == "Customer"))
                {
                    var balanceClaim = claims.FirstOrDefault(c => c.Type == "CustomerBalance");
                    decimal.TryParse(balanceClaim?.Value, out var balance);
                    customers.Add((user, balance));
                }
            }

            return View(customers.OrderByDescending(c => c.Balance).ToList());
        }

        // Charge (add to) a customer's wallet balance
        [HttpPost]
        public async Task<IActionResult> ChargeBalance(string phone, decimal amount, string? note)
        {
            if (amount <= 0)
                return BadRequest("مبلغ باید بیشتر از صفر باشد");

            var user = await _userManager.FindByNameAsync(phone);
            if (user == null)
                return NotFound("کاربر یافت نشد");

            var claims = await _userManager.GetClaimsAsync(user);
            var roleClaim = claims.FirstOrDefault(c => c.Type == "Role" && c.Value == "Customer");
            if (roleClaim == null)
                return BadRequest("این کاربر مشتری نیست");

            var balanceClaim = claims.FirstOrDefault(c => c.Type == "CustomerBalance");
            decimal.TryParse(balanceClaim?.Value, out var currentBalance);

            var newBalance = currentBalance + amount;

            if (balanceClaim != null)
                await _userManager.RemoveClaimAsync(user, balanceClaim);
            await _userManager.AddClaimAsync(user, new Claim("CustomerBalance", newBalance.ToString()));

            _logger.LogInformation("Admin charged wallet for {Phone}: +{Amount} (note: {Note}). New balance: {Balance}",
                phone, amount, note, newBalance);

            TempData["Success"] = $"کیف پول {phone} به مبلغ {amount:N0} تومان شارژ شد. موجودی جدید: {newBalance:N0} تومان";
            return RedirectToAction("Index");
        }
    }
}
