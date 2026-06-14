using Microsoft.AspNetCore.Mvc;

namespace Application.Controllers
{
    public class PartnerController : Controller
    {
        private static readonly HashSet<string> _validPartners = new(StringComparer.OrdinalIgnoreCase)
        {
            "isic"
        };

        // Entry point: /isic  or  /partner/enter/isic
        [HttpGet("/isic")]
        public IActionResult IsicEntry() => Entry("isic");

        [HttpGet("/partner/enter/{name}")]
        public IActionResult Entry(string name)
        {
            if (!_validPartners.Contains(name))
                return RedirectToAction("Index", "Home", new { area = "AgencyArea" });

            Response.Cookies.Append("partner_brand", name.ToLower(), new CookieOptions
            {
                HttpOnly = false,          // JS can read it if needed
                Secure   = false,          // set true in production with HTTPS
                SameSite = SameSiteMode.Lax,
                Expires  = DateTimeOffset.UtcNow.AddHours(24)
            });

            return RedirectToAction("Index", "Home", new { area = "AgencyArea" });
        }

        // Allows user to clear partner branding
        [HttpGet("/partner/clear")]
        public IActionResult Clear()
        {
            Response.Cookies.Delete("partner_brand");
            return RedirectToAction("Index", "Home", new { area = "AgencyArea" });
        }
    }
}
