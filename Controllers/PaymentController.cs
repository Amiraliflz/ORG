using Application.Data;
using Application.Services;
using Application.Services.Payment;
using Application.Services.MrShooferORS;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;

namespace Application.Controllers
{
    [Route("[controller]/[action]")]
    public class PaymentController : Controller
    {
        private readonly IPaymentService _paymentService;
        private readonly AppDbContext _context;
        private readonly ILogger<PaymentController> _logger;
        private readonly MrShooferAPIClient _mrShooferClient;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly CustomerBalanceService _balanceSvc;

        public PaymentController(
            IPaymentService paymentService,
            AppDbContext context,
            ILogger<PaymentController> logger,
            MrShooferAPIClient mrShooferClient,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
            CustomerBalanceService balanceSvc)
        {
            _paymentService = paymentService;
            _context = context;
            _logger = logger;
            _mrShooferClient = mrShooferClient;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _userManager = userManager;
            _signInManager = signInManager;
            _balanceSvc = balanceSvc;
        }

        /// <summary>
        /// Public fast link used as Zarinpal callback domain.
        /// Zarinpal will redirect to this path (https://payment.mrshoofer.ir/link) —
        /// this action forwards the query to the internal Verify action so domain matches terminal.
        /// </summary>
        [HttpGet("/link")]
        public IActionResult LinkCallback(string Authority, string Status)
        {
            _logger.LogInformation("Fast callback /link received. Authority={Authority} Status={Status}", Authority, Status);

            // Forward to Verify action preserving query parameters
            return RedirectToAction("Verify", new { Authority, Status });
        }

        /// <summary>
        /// Initiate payment for a ticket: request authority from Zarinpal and redirect user to gateway
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> RequestPayment(int ticketId)
        {
            try
            {
                var ticket = await _context.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId);
                if (ticket == null)
                {
                    _logger.LogError("Payment initiation failed: ticket not found. TicketId: {TicketId}", ticketId);
                    ViewBag.ErrorMessage = "اطلاعات سفارش یافت نشد";
                    return View("PaymentFailed");
                }

                if (ticket.IsPaid)
                {
                    _logger.LogInformation("Payment initiation: ticket already paid. TicketId: {TicketId}", ticketId);
                    return RedirectToAction("ReserveConfirmed", "Reserve", new { area = "AgencyArea", ticketcode = ticket.TicketCode });
                }

                // Convert Toman -> Rial
                int amountInRials = ticket.TicketFinalPrice * 10;

                var description = _configuration["Zarinpal:Description"] ?? ($"پرداخت برای بلیط {ticket.Tripcode}");
                var mobile = ticket.PhoneNumber ?? string.Empty;
                var email = ticket.Email;

                _logger.LogInformation("Requesting payment authority for TicketId: {TicketId}, Amount: {Amount}", ticketId, amountInRials);

                var (success, authority, message) = await _paymentService.RequestPaymentAsync(amountInRials, description, mobile, email);

                if (!success)
                {
                    _logger.LogError("Zarinpal RequestPayment failed for TicketId: {TicketId}. Message: {Message}", ticketId, message);
                    ViewBag.ErrorMessage = message;
                    return View("PaymentFailed");
                }

                // Save authority to ticket and persist
                ticket.PaymentAuthority = authority;
                await _context.SaveChangesAsync();

                var gatewayUrl = _paymentService.GetPaymentGatewayUrl(authority);
                _logger.LogInformation("Redirecting user to payment gateway. TicketId: {TicketId}, GatewayUrl: {Url}", ticketId, gatewayUrl);

                return Redirect(gatewayUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during RequestPayment for TicketId: {TicketId}", ticketId);
                ViewBag.ErrorMessage = "خطا در ایجاد درخواست پرداخت. لطفاً مجدداً تلاش کنید";
                return View("PaymentFailed");
            }
        }

        /// <summary>
        /// Verify payment callback from Zarinpal
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Verify(string Authority, string Status)
        {
            try
            {
                _logger.LogInformation("Payment callback received. Authority: {Authority}, Status: {Status}", Authority, Status);

                // Check if payment was cancelled by user
                if (Status != "OK")
                {
                    _logger.LogWarning("Payment cancelled by user. Authority: {Authority}", Authority);
                    ViewBag.ErrorMessage = "پرداخت توسط کاربر لغو شد";
                    return View("PaymentFailed");
                }

                // Find ticket by authority
                var ticket = await _context.Tickets
                    .Include(t => t.Agency)
                    .FirstOrDefaultAsync(t => t.PaymentAuthority == Authority);

                if (ticket == null)
                {
                    _logger.LogError("Ticket not found for authority: {Authority}", Authority);
                    ViewBag.ErrorMessage = "اطلاعات تراکنش یافت نشد";
                    return View("PaymentFailed");
                }

                // Check if already paid
                if (ticket.IsPaid)
                {
                    _logger.LogInformation("Ticket already paid. TicketCode: {TicketCode}", ticket.TicketCode);
                    return RedirectToAction("ReserveConfirmed", "Reserve", new { area = "AgencyArea", ticketcode = ticket.TicketCode });
                }

                // Convert price from Toman to Rial (multiply by 10)
                int amountInRials = ticket.TicketFinalPrice * 10;

                // Verify payment with payment service
                var (success, refId, cardPan, message) = await _paymentService.VerifyPaymentAsync(Authority, amountInRials);

                if (!success)
                {
                    _logger.LogError("Payment verification failed. Authority: {Authority}, Message: {Message}", Authority, message);
                    ViewBag.ErrorMessage = message;
                    return View("PaymentFailed");
                }

                _logger.LogInformation("Payment verified successfully. RefId: {RefId}, Authority: {Authority}", refId, Authority);

                // ✅ NOW CREATE MRSHOOFER RESERVATION (AFTER PAYMENT VERIFIED!)
                try
                {
                    _logger.LogInformation("Creating MrShoofer reservation after payment verification. TripCode: {TripCode}", ticket.Tripcode);

                    // Use Sale.mrshoofer as the default OTA seller for guest bookings
                    var guestAgency = await _context.Agencies.FirstOrDefaultAsync(a => a.IdentityUser != null && a.IdentityUser.UserName == "Sale.mrshoofer");

                    if (guestAgency != null && !string.IsNullOrWhiteSpace(guestAgency.ORSAPI_token))
                    {
                        _mrShooferClient.SetSellerApiKey(guestAgency.ORSAPI_token);
                        _logger.LogInformation("Using guest/default OTA seller for reservation. AgencyId: {AgencyId}", guestAgency.Id);
                    }
                    else if (ticket.Agency != null && !string.IsNullOrWhiteSpace(ticket.Agency.ORSAPI_token))
                    {
                        _mrShooferClient.SetSellerApiKey(ticket.Agency.ORSAPI_token);
                        _logger.LogInformation("Using ticket's agency OTA token for reservation. AgencyId: {AgencyId}", ticket.Agency.Id);
                    }
                    else
                    {
                        // Fallback to configuration token
                        var fallbackToken = _configuration["MrShoofer:SellerToken"];
                        if (!string.IsNullOrWhiteSpace(fallbackToken))
                        {
                            _mrShooferClient.SetSellerApiKey(fallbackToken);
                            _logger.LogInformation("Using fallback configuration OTA token for reservation.");
                        }
                    }

                    // Step 1: Temporary Reserve
                    var tempreserve = new TicketTempReserveRequestModel
                    {
                        isPrivate = true,
                        tripCode = ticket.Tripcode
                    };

                    string reservecode = await _mrShooferClient.ReserveTicketTemporarirly(tempreserve);
                    _logger.LogInformation("Temporary reservation created. ReserveCode: {ReserveCode}", reservecode);

                    // If MRShhoofer returned a sentinel indicating insufficient account balance, skip ConfirmReserve
                    if (!string.IsNullOrEmpty(reservecode) && reservecode.StartsWith("MRSHOOFER-NO-BAL-", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("MrShoofer indicated insufficient account balance for reservation. Continuing without ORS reservation. TicketId: {TicketId}", ticket.Id);
                        ticket.TicketCode = $"PAID-NO-RESERVE-{DateTime.Now:yyyyMMddHHmmss}-{ticket.Id}";
                    }
                    else
                    {
                        // Step 2: Confirm Reserve
                        var confirmreserve = new ConfirmReserveRequestModel
                        {
                            passengerFirstName = ticket.Firstname,
                            passengerLastName = ticket.Lastname,
                            reservationCode = reservecode,
                            passengerNationalCode = ticket.NaCode,
                            passengerNumberPhone = ticket.PhoneNumber
                        };

                        var reserve_response = await _mrShooferClient.ConfirmReserve(confirmreserve);

                        // Update ticket with MrShoofer ticket code
                        ticket.TicketCode = reserve_response.ticketCode;

                        // If ORS returned a webapp token include it on the ticket for later notification/redirect
                        if (!string.IsNullOrWhiteSpace(reserve_response?.webappToken))
                        {
                            ticket.WebappToken = reserve_response.webappToken;
                        }

                        _logger.LogInformation("MrShoofer reservation confirmed. TicketCode: {TicketCode}", ticket.TicketCode);
                    }
                }
                catch (Exception ex)
                {
                    // Handle account-balance related errors with less severity
                    var msg = ex.Message ?? string.Empty;
                    if (msg.Contains("ACCOUNT BALANCE", StringComparison.OrdinalIgnoreCase) || msg.Contains("CAN NOT SUBMIT TICKET", StringComparison.OrdinalIgnoreCase) || msg.Contains("ACCOUNT BALANCE NOT ENOUGH", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning(ex, "MrShoofer reservation failed due to insufficient account balance. Marking ticket as paid but not reserved. TicketId: {TicketId}", ticket.Id);
                        ticket.TicketCode = $"PAID-NO-RESERVE-{DateTime.Now:yyyyMMddHHmmss}-{ticket.Id}";
                    }
                    else
                    {
                        _logger.LogError(ex, "Error creating MrShoofer reservation after payment. TicketId: {TicketId}", ticket.Id);
                        ticket.TicketCode = $"PAID-NO-RESERVE-{DateTime.Now:yyyyMMddHHmmss}-{ticket.Id}";
                    }
                }

                // Update ticket payment information
                ticket.IsPaid = true;
                ticket.PaymentRefId = refId.ToString();
                ticket.CardPan = cardPan;
                ticket.PaidAt = DateTime.Now;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Ticket updated successfully. TicketCode: {TicketCode}, RefId: {RefId}", ticket.TicketCode, refId);

                // Ensure customer account exists, then auto-login if not already authenticated
                await EnsureCustomerAccountAsync(ticket.PhoneNumber);
                await AutoLoginCustomerAsync(ticket.PhoneNumber);

                // If ticket has a webapp token, POST JSON to webapp endpoint with retries and then redirect user to webapp URL
                if (!string.IsNullOrWhiteSpace(ticket.WebappToken))
                {
                    var webappBase = _configuration["Webapp:BaseUrl"] ?? "https://webapp.mrshoofer.ir";
                    var targetUrl = $"{webappBase}/o?t={System.Net.WebUtility.UrlEncode(ticket.WebappToken)}";

                    var client = _httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(5);

                    bool notified = false;
                    int attempts = 0;
                    int maxAttempts = 3;

                    var payload = new { webappToken = ticket.WebappToken };
                    var payloadJson = JsonSerializer.Serialize(payload);

                    while (attempts < maxAttempts && !notified)
                    {
                        attempts++;
                        try
                        {
                            _logger.LogInformation("Calling webapp registration endpoint (POST). Attempt {Attempt}/{MaxAttempts}. Url={Url}", attempts, maxAttempts, targetUrl);

                            using var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
                            var resp = await client.PostAsync(targetUrl, content);
                            if (resp.IsSuccessStatusCode)
                            {
                                notified = true;
                                _logger.LogInformation("Webapp registration POST succeeded on attempt {Attempt}", attempts);
                                break;
                            }

                            _logger.LogWarning("Webapp registration POST returned non-success status {Status} on attempt {Attempt}", resp.StatusCode, attempts);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Exception when calling webapp registration POST on attempt {Attempt}", attempts);
                        }

                        await Task.Delay(500 * attempts);
                    }

                    if (!notified)
                    {
                        _logger.LogError("Failed to notify webapp after {MaxAttempts} attempts. Token={Token}", maxAttempts, ticket.WebappToken);
                    }

                    // Redirect user to webapp URL (regardless of notify result)
                    return Redirect(targetUrl);
                }

                // Redirect to success page
                return RedirectToAction("ReserveConfirmed", "Reserve", new { area = "AgencyArea", ticketcode = ticket.TicketCode });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in payment verification. Authority: {Authority}", Authority);
                ViewBag.ErrorMessage = "خطا در پردازش پرداخت. لطفا با پشتیبانی تماس بگیرید";
                return View("PaymentFailed");
            }
        }

        /// <summary>
        /// Entry point from main app — validates HMAC, calls Zarinpal, returns HTML redirect page.
        /// Main app redirects user here; this server's IP is whitelisted with Zarinpal.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Start(int ticketId, long t, string sig)
        {
            // Validate timestamp (10-minute window)
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (Math.Abs(now - t) > 600)
                return Content(ErrorHtml("لینک پرداخت منقضی شده است. لطفاً دوباره تلاش کنید."), "text/html");

            // Validate HMAC signature
            var sharedKey = _configuration["PaymentServer:SharedKey"] ?? string.Empty;
            var expected = ComputeHmac($"{ticketId}:{t}", sharedKey);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(sig),
                    Encoding.UTF8.GetBytes(expected)))
                return Content(ErrorHtml("درخواست نامعتبر است."), "text/html");

            var ticket = await _context.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId);
            if (ticket == null)
                return Content(ErrorHtml("اطلاعات سفارش یافت نشد."), "text/html");

            if (ticket.IsPaid)
                return Redirect(Url.Action("ReserveConfirmed", "Reserve", new { area = "AgencyArea", ticketcode = ticket.TicketCode })!);

            int amountInRials = ticket.TicketFinalPrice * 10;
            var description = _configuration["Zarinpal:Description"] ?? $"پرداخت برای بلیط {ticket.Tripcode}";

            var (success, authority, message) = await _paymentService.RequestPaymentAsync(
                amountInRials, description, ticket.PhoneNumber ?? string.Empty, ticket.Email);

            if (!success)
                return Content(ErrorHtml($"خطا در ایجاد درخواست پرداخت: {message}"), "text/html");

            ticket.PaymentAuthority = authority;
            await _context.SaveChangesAsync();

            // Test authority generated when sandbox is unreachable — skip gateway, go straight to Verify
            if (authority.StartsWith("TEST-", StringComparison.OrdinalIgnoreCase))
            {
                var verifyUrl = Url.Action("Verify", "Payment", new { Authority = authority, Status = "OK" });
                return Content(RedirectHtml(verifyUrl!), "text/html");
            }

            var gatewayUrl = _paymentService.GetPaymentGatewayUrl(authority);
            return Content(RedirectHtml(gatewayUrl), "text/html");
        }

        /// <summary>
        /// Verify wallet top-up payment callback from Zarinpal
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> TopUpVerify(string Authority, string Status)
        {
            try
            {
                if (Status != "OK")
                {
                    ViewBag.ErrorMessage = "پرداخت توسط کاربر لغو شد";
                    return View("PaymentFailed");
                }

                // Find the pending claim directly in DB by Authority
                // (auth cookie may not be present — callback is on pay.mrshoofer.ir, user logged in on mrshoofer.ir)
                var dbClaim = await _context.UserClaims
                    .FirstOrDefaultAsync(c => c.ClaimType == "WalletTopUpPending"
                                           && c.ClaimValue.StartsWith(Authority + ":"));

                IdentityUser? user = null;
                System.Security.Claims.Claim? pendingClaim = null;

                if (dbClaim != null)
                {
                    user = await _userManager.FindByIdAsync(dbClaim.UserId);
                    pendingClaim = new System.Security.Claims.Claim(dbClaim.ClaimType, dbClaim.ClaimValue);
                }

                if (user == null || pendingClaim == null)
                {
                    _logger.LogError("WalletTopUpPending claim not found for Authority={Authority}", Authority);
                    ViewBag.ErrorMessage = "درخواست شارژ کیف پول یافت نشد. لطفاً با پشتیبانی تماس بگیرید";
                    return View("PaymentFailed");
                }

                var parts = pendingClaim.Value.Split(':');
                if (parts.Length < 2)
                {
                    _logger.LogError("WalletTopUpPending claim malformed. Value={Value}", pendingClaim.Value);
                    ViewBag.ErrorMessage = "اطلاعات تراکنش نامعتبر است";
                    return View("PaymentFailed");
                }

                if (!decimal.TryParse(parts[1], out var topUpAmount) || topUpAmount < 1)
                {
                    ViewBag.ErrorMessage = "مبلغ تراکنش نامعتبر است";
                    return View("PaymentFailed");
                }

                int amountInRials = (int)(topUpAmount * 10);
                var (success, refId, cardPan, message) = await _paymentService.VerifyPaymentAsync(Authority, amountInRials);

                if (!success)
                {
                    _logger.LogError("Wallet top-up verification failed. Authority={Authority}, Message={Message}", Authority, message);
                    ViewBag.ErrorMessage = message;
                    return View("PaymentFailed");
                }

                // Remove pending claim
                await _userManager.RemoveClaimAsync(user, pendingClaim);

                // Credit balance in DB
                var newBalance = await _balanceSvc.AddBalance(user.Id, topUpAmount);

                _logger.LogInformation("Wallet top-up successful. User={User}, Amount={Amount}, NewBalance={Balance}, RefId={RefId}",
                    user.UserName, topUpAmount, newBalance, refId);

                if (!_signInManager.IsSignedIn(User))
                    await _signInManager.SignInAsync(user, isPersistent: true);

                var mainAppUrl = _configuration["PaymentServer:MainAppUrl"];
                if (!string.IsNullOrWhiteSpace(mainAppUrl))
                {
                    var msg = Uri.EscapeDataString($"کیف پول شما با موفقیت {topUpAmount:N0} تومان شارژ شد.");
                    return Redirect($"{mainAppUrl}/Customer/MyWallet?topup=ok&msg={msg}");
                }

                TempData["Success"] = $"کیف پول شما با موفقیت {topUpAmount:N0} تومان شارژ شد.";
                return RedirectToAction("MyWallet", "Customer", new { area = "AgencyArea" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during wallet top-up verify. Authority={Authority}", Authority);
                ViewBag.ErrorMessage = "خطا در پردازش شارژ کیف پول. لطفا با پشتیبانی تماس بگیرید";
                return View("PaymentFailed");
            }
        }

        /// <summary>
        /// Payment failed page
        /// </summary>
        [HttpGet]
        public IActionResult PaymentFailed(string? message = null)
        {
            ViewBag.ErrorMessage = message ?? "پرداخت ناموفق بود";
            return View();
        }

        internal static string ComputeHmac(string data, string key)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var dataBytes = Encoding.UTF8.GetBytes(data);
            var hash = HMACSHA256.HashData(keyBytes, dataBytes);
            return Convert.ToBase64String(hash);
        }

        private static string RedirectHtml(string url)
        {
            var safeUrl = System.Web.HttpUtility.HtmlAttributeEncode(url);
            return "<html lang='fa' dir='rtl'><head><meta charset='UTF-8'>" +
                   "<title>در حال انتقال به درگاه پرداخت...</title>" +
                   "<style>body{font-family:Tahoma,sans-serif;display:flex;justify-content:center;align-items:center;height:100vh;margin:0;background:#f5f5f5}" +
                   ".box{text-align:center;background:white;padding:40px;border-radius:12px;box-shadow:0 2px 16px rgba(0,0,0,.1)}" +
                   ".spinner{width:40px;height:40px;border:4px solid #eee;border-top-color:#6c63ff;border-radius:50%;animation:spin .8s linear infinite;margin:0 auto 20px}" +
                   "@keyframes spin{to{transform:rotate(360deg)}}</style></head>" +
                   "<body><div class='box'><div class='spinner'></div>" +
                   "<p>در حال انتقال به درگاه پرداخت زرین‌پال...</p>" +
                   "<a href='" + safeUrl + "'>اگر منتقل نشدید اینجا کلیک کنید</a></div>" +
                   "<script>window.location.href='" + safeUrl + "';</script></body></html>";
        }

        private async Task AutoLoginCustomerAsync(string? phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber)) return;
            if (_signInManager.IsSignedIn(User)) return;  // already logged in — don't displace an agency session

            try
            {
                var user = await _userManager.FindByNameAsync(phoneNumber);
                if (user == null) return;

                var claims = await _userManager.GetClaimsAsync(user);
                if (!claims.Any(c => c.Type == "Role" && c.Value == "Customer")) return;

                await _signInManager.SignInAsync(user, isPersistent: true);
                _logger.LogInformation("Customer auto-signed in after payment: {Phone}", phoneNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-sign in customer {Phone}", phoneNumber);
            }
        }

        private async Task EnsureCustomerAccountAsync(string? phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber)) return;
            try
            {
                var existing = await _userManager.FindByNameAsync(phoneNumber);
                if (existing != null) return;

                var user = new IdentityUser { UserName = phoneNumber, PhoneNumber = phoneNumber };
                // Random password — customer logs in via OTP, not password
                var password = Guid.NewGuid().ToString("N")[..8] + "Aa1!";
                var result = await _userManager.CreateAsync(user, password);
                if (result.Succeeded)
                {
                    await _userManager.AddClaimAsync(user, new Claim("Role", "Customer"));
                    // Balance is stored in CustomerProfile.Balance (DB), not claims
                    _logger.LogInformation("Customer account created for phone: {Phone}", phoneNumber);
                }
                else
                {
                    _logger.LogWarning("Failed to create customer account for {Phone}: {Errors}",
                        phoneNumber, string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while creating customer account for {Phone}", phoneNumber);
            }
        }

        private static string ErrorHtml(string msg)
        {
            var safeMsg = System.Web.HttpUtility.HtmlEncode(msg);
            return "<html lang='fa' dir='rtl'><head><meta charset='UTF-8'><title>خطا</title>" +
                   "<style>body{font-family:Tahoma,sans-serif;display:flex;justify-content:center;align-items:center;height:100vh;margin:0;background:#f5f5f5}" +
                   ".box{text-align:center;background:white;padding:40px;border-radius:12px;color:#e53e3e}</style></head>" +
                   "<body><div class='box'><h2>خطا</h2><p>" + safeMsg + "</p>" +
                   "<a href='javascript:history.back()'>بازگشت</a></div></body></html>";
        }
    }
}
