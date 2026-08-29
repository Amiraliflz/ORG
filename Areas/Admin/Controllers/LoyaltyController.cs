using Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Application.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Policy = "Admin")]
    public class LoyaltyController : Controller
    {
        private readonly LoyaltyService _loyalty;

        public LoyaltyController(LoyaltyService loyalty)
        {
            _loyalty = loyalty;
        }

        public async Task<IActionResult> Index()
        {
            if (!LoyaltyService.FeatureEnabled)
                return NotFound();

            var enabled = await _loyalty.IsDiscountEnabledAsync();
            var (start, end) = LoyaltyService.CurrentShamsiMonthRange();
            ViewBag.DiscountEnabled = enabled;
            ViewBag.MonthStart = start;
            ViewBag.MonthEnd = end;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetDiscountEnabled(bool enabled)
        {
            if (!LoyaltyService.FeatureEnabled)
                return NotFound();

            await _loyalty.SetDiscountEnabledAsync(enabled);
            TempData["Success"] = enabled
                ? "تخفیف وفاداری فعال شد."
                : "تخفیف وفاداری غیرفعال شد. سطح مشتریان همچنان محاسبه و نمایش داده می‌شود.";
            return RedirectToAction(nameof(Index));
        }
    }
}
