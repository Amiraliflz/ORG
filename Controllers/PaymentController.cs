using Application.Data;
using Application.Services.Payment;
using Application.Services.MrShooferORS;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text;

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

        public PaymentController(
            IPaymentService paymentService, 
            AppDbContext context,
            ILogger<PaymentController> logger,
            MrShooferAPIClient mrShooferClient,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _paymentService = paymentService;
            _context = context;
            _logger = logger;
            _mrShooferClient = mrShooferClient;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
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

                    // Use default guest OTA seller when available (IdentityUser == null and Name contains "مهمان")
                    var guestAgency = await _context.Agencies.FirstOrDefaultAsync(a => a.IdentityUser == null && a.Name.Contains("مهمان"));

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
        /// Payment failed page
        /// </summary>
        [HttpGet]
        public IActionResult PaymentFailed(string? message = null)
        {
            ViewBag.ErrorMessage = message ?? "پرداخت ناموفق بود";
            return View();
        }
    }
}
