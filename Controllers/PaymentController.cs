using Application.Data;
using Application.Services.Payment;
using Application.Services.MrShooferORS;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Application.Controllers
{
    [Route("[controller]/[action]")]
    public class PaymentController : Controller
    {
        private readonly ZarinpalService _zarinpalService;
        private readonly AppDbContext _context;
        private readonly ILogger<PaymentController> _logger;
        private readonly MrShooferAPIClient _mrShooferClient;
        private readonly IConfiguration _configuration;

        public PaymentController(
            ZarinpalService zarinpalService, 
            AppDbContext context,
            ILogger<PaymentController> logger,
            MrShooferAPIClient mrShooferClient,
            IConfiguration configuration)
        {
            _zarinpalService = zarinpalService;
            _context = context;
            _logger = logger;
            _mrShooferClient = mrShooferClient;
            _configuration = configuration;
        }

        /// <summary>
        /// Mock payment gateway for localhost testing
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> MockGateway(string authority)
        {
            if (!_zarinpalService.IsMockPaymentEnabled)
            {
                return NotFound("Mock payment is not enabled");
            }

            // Find the ticket by authority
            var ticket = await _context.Tickets
                .FirstOrDefaultAsync(t => t.PaymentAuthority == authority);

            if (ticket == null)
            {
                return NotFound("تراکنش یافت نشد");
            }

            ViewBag.Authority = authority;
            ViewBag.Ticket = ticket;
            ViewBag.Amount = ticket.TicketFinalPrice;
            ViewBag.AmountInRials = ticket.TicketFinalPrice * 10;

            return View();
        }

        /// <summary>
        /// Process mock payment (simulate success or failure)
        /// </summary>
        [HttpPost]
        public IActionResult ProcessMockPayment(string authority, bool success)
        {
            if (!_zarinpalService.IsMockPaymentEnabled)
            {
                return NotFound("Mock payment is not enabled");
            }

            var status = success ? "OK" : "NOK";
            return RedirectToAction("Verify", new { Authority = authority, Status = status });
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

                // Verify payment with Zarinpal (or mock)
                var (success, refId, cardPan, message) = await _zarinpalService.VerifyPaymentAsync(Authority, amountInRials);

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
                    
                    // Set API token for the agency
                    if (ticket.Agency != null && !string.IsNullOrWhiteSpace(ticket.Agency.ORSAPI_token))
                    {
                        _mrShooferClient.SetSellerApiKey(ticket.Agency.ORSAPI_token);
                    }
                    else
                    {
                        // Fallback to configuration token
                        var fallbackToken = _configuration["MrShoofer:SellerToken"];
                        if (!string.IsNullOrWhiteSpace(fallbackToken))
                        {
                            _mrShooferClient.SetSellerApiKey(fallbackToken);
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
                    
                    _logger.LogInformation("MrShoofer reservation confirmed. TicketCode: {TicketCode}", ticket.TicketCode);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating MrShoofer reservation after payment. TicketId: {TicketId}", ticket.Id);
                    
                    // ⚠️ CRITICAL: Payment succeeded but reservation failed!
                    // Mark ticket with special code for manual intervention
                    ticket.TicketCode = $"PAID-NO-RESERVE-{DateTime.Now:yyyyMMddHHmmss}-{ticket.Id}";
                    
                    _logger.LogCritical("PAYMENT SUCCEEDED BUT MRSHOOFER RESERVATION FAILED! TicketId: {TicketId}, PaymentRefId: {RefId}, TripCode: {TripCode}",
                        ticket.Id, refId, ticket.Tripcode);
                }

                // Update ticket payment information
                ticket.IsPaid = true;
                ticket.PaymentRefId = refId.ToString();
                ticket.CardPan = cardPan;
                ticket.PaidAt = DateTime.Now;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Ticket updated successfully. TicketCode: {TicketCode}, RefId: {RefId}", ticket.TicketCode, refId);

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
        public IActionResult PaymentFailed(string message = null)
        {
            ViewBag.ErrorMessage = message ?? "پرداخت ناموفق بود";
            return View();
        }
    }
}
