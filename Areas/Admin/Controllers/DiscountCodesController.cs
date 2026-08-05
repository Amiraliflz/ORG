using Application.Data;
using Application.Models;
using Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Application.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Policy = "Admin")]
    public class DiscountCodesController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly CustomerServiceSmsSender _sms;

        public DiscountCodesController(AppDbContext context, UserManager<IdentityUser> userManager, CustomerServiceSmsSender sms)
        {
            _context = context;
            _userManager = userManager;
            _sms = sms;
        }

        public async Task<IActionResult> Index()
        {
            var codes = await _context.DiscountCodes.OrderByDescending(d => d.CreatedAt).ToListAsync();
            return View(codes);
        }

        [HttpGet]
        public IActionResult Create() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            string code, string discountPercent, string? description,
            DateTime? expiryDate, string? maxUses,
            bool allowMultipleUsePerUser,
            string sendMode,
            string? targetPhone,
            string? smsMessage)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                TempData["Error"] = "کد تخفیف نمی‌تواند خالی باشد.";
                return View();
            }

            var percent = ParsePersianInt(discountPercent);
            if (percent is null or < 1 or > 100)
            {
                TempData["Error"] = "درصد تخفیف باید عددی بین ۱ تا ۱۰۰ باشد.";
                return View();
            }

            var maxUsesVal = string.IsNullOrWhiteSpace(maxUses) ? (int?)null : ParsePersianInt(maxUses);

            var exists = await _context.DiscountCodes.AnyAsync(d => d.Code == code.Trim().ToUpper());
            if (exists)
            {
                TempData["Error"] = "این کد تخفیف قبلاً ثبت شده است.";
                return View();
            }

            var entity = new DiscountCode
            {
                Code            = code.Trim().ToUpper(),
                DiscountPercent = percent.Value,
                Description     = description,
                ExpiryDate      = expiryDate,
                MaxUses                  = maxUsesVal,
                IsActive                 = true,
                AllowMultipleUsePerUser  = allowMultipleUsePerUser,
                CreatedAt                = DateTime.Now
            };

            _context.DiscountCodes.Add(entity);
            await _context.SaveChangesAsync();

            var finalMsg = string.IsNullOrWhiteSpace(smsMessage)
                ? _sms.BuildDiscountMessage(entity.Code, entity.DiscountPercent)
                : smsMessage.Trim();

            // Send SMS and auto-set MaxUses = recipient count (skipped when AllowMultipleUsePerUser is on)
            if (sendMode == "single" && !string.IsNullOrWhiteSpace(targetPhone))
            {
                var recipients = new List<string> { targetPhone.Trim() };
                await _sms.SendBulk(finalMsg, recipients);

                if (!allowMultipleUsePerUser)
                {
                    entity.MaxUses = recipients.Count;
                    await _context.SaveChangesAsync();
                }

                var usageNote = allowMultipleUsePerUser ? "استفاده نامحدود" : "یک بار قابل استفاده";
                TempData["Success"] = $"کد تخفیف «{entity.Code}» ایجاد شد و به {targetPhone} ارسال گردید ({usageNote}).";
            }
            else if (sendMode == "all")
            {
                var phones = await GetAllCustomerPhones();
                if (phones.Count > 0)
                {
                    await _sms.SendBulk(finalMsg, phones);
                    if (!allowMultipleUsePerUser)
                    {
                        entity.MaxUses = phones.Count;
                        await _context.SaveChangesAsync();
                    }
                }
                var usageNote = allowMultipleUsePerUser ? "استفاده نامحدود" : "هر مشتری یک بار قابل استفاده";
                TempData["Success"] = $"کد تخفیف «{entity.Code}» ایجاد شد و برای {phones.Count} مشتری ارسال گردید ({usageNote}).";
            }
            else
            {
                TempData["Success"] = $"کد تخفیف «{entity.Code}» با موفقیت ایجاد شد.";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle(int id)
        {
            var code = await _context.DiscountCodes.FindAsync(id);
            if (code == null) return NotFound();

            code.IsActive = !code.IsActive;
            await _context.SaveChangesAsync();
            TempData["Success"] = $"کد «{code.Code}» {(code.IsActive ? "فعال" : "غیرفعال")} شد.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleMultiUse(int id)
        {
            var code = await _context.DiscountCodes.FindAsync(id);
            if (code == null) return NotFound();

            code.AllowMultipleUsePerUser = !code.AllowMultipleUsePerUser;
            await _context.SaveChangesAsync();
            TempData["Success"] = $"کد «{code.Code}» — استفاده نامحدود {(code.AllowMultipleUsePerUser ? "فعال" : "غیرفعال")} شد.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var code = await _context.DiscountCodes.FindAsync(id);
            if (code == null) return NotFound();

            _context.DiscountCodes.Remove(code);
            await _context.SaveChangesAsync();
            TempData["Success"] = $"کد تخفیف «{code.Code}» حذف شد.";
            return RedirectToAction(nameof(Index));
        }

        // Send existing code to customers without creating a new one
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendSms(int id, string sendMode, string? targetPhone, string? smsMessage)
        {
            var code = await _context.DiscountCodes.FindAsync(id);
            if (code == null) return NotFound();

            var msg = string.IsNullOrWhiteSpace(smsMessage)
                ? _sms.BuildDiscountMessage(code.Code, code.DiscountPercent)
                : smsMessage.Trim();

            if (sendMode == "single" && !string.IsNullOrWhiteSpace(targetPhone))
            {
                var recipients = new List<string> { targetPhone.Trim() };
                await _sms.SendBulk(msg, recipients);
                code.MaxUses = (code.MaxUses ?? 0) + recipients.Count;
                await _context.SaveChangesAsync();
                TempData["Success"] = $"کد «{code.Code}» به {targetPhone} ارسال شد (یک بار قابل استفاده).";
            }
            else if (sendMode == "all")
            {
                var phones = await GetAllCustomerPhones();
                if (phones.Count > 0)
                {
                    await _sms.SendBulk(msg, phones);
                    code.MaxUses = (code.MaxUses ?? 0) + phones.Count;
                    await _context.SaveChangesAsync();
                }
                TempData["Success"] = $"کد «{code.Code}» برای {phones.Count} مشتری ارسال شد.";
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<List<string>> GetAllCustomerPhones()
        {
            var phones = new List<string>();
            foreach (var user in _userManager.Users.ToList())
            {
                var claims = await _userManager.GetClaimsAsync(user);
                if (!claims.Any(c => c.Type == "Role" && c.Value == "Customer")) continue;
                if (!string.IsNullOrWhiteSpace(user.UserName))
                    phones.Add(user.UserName);
            }
            return phones;
        }

        private static int? ParsePersianInt(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var normalized = System.Text.RegularExpressions.Regex.Replace(value.Trim(), "[۰-۹]",
                m => ((char)(m.Value[0] - '۰' + '0')).ToString());
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, "[٠-٩]",
                m => ((char)(m.Value[0] - '٠' + '0')).ToString());
            return int.TryParse(normalized, out var result) ? result : null;
        }
    }
}
