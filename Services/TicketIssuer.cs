using Application.Data;
using Application.Models;
using Application.Services.MrShooferORS;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class TicketIssuer
    {
        private readonly MrShooferAPIClient _apiClient;
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TicketIssuer> _logger;

        public TicketIssuer(
            MrShooferAPIClient apiClient,
            AppDbContext context,
            IConfiguration configuration,
            ILogger<TicketIssuer> logger)
        {
            _apiClient = apiClient;
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Performs the two-step ORS reservation (TempReserve → ConfirmReserve) for a ticket.
        /// Selects the best available API token automatically.
        /// Returns (ticketCode, webappToken) on success or throws on hard failure.
        /// </summary>
        public async Task<(string ticketCode, string? webappToken)> ReserveWithOrsAsync(Ticket ticket)
        {
            var guestAgency = await _context.Agencies
                .FirstOrDefaultAsync(a => a.IdentityUser == null && a.Name.Contains("مهمان"));

            if (guestAgency != null && !string.IsNullOrWhiteSpace(guestAgency.ORSAPI_token))
                _apiClient.SetSellerApiKey(guestAgency.ORSAPI_token);
            else if (ticket.Agency != null && !string.IsNullOrWhiteSpace(ticket.Agency.ORSAPI_token))
                _apiClient.SetSellerApiKey(ticket.Agency.ORSAPI_token);
            else
            {
                var fallback = _configuration["MrShoofer:SellerToken"];
                if (!string.IsNullOrWhiteSpace(fallback))
                    _apiClient.SetSellerApiKey(fallback);
            }

            // Step 1: temp reserve
            var tempReserve = new TicketTempReserveRequestModel
            {
                isPrivate = true,
                tripCode = ticket.Tripcode
            };

            string reserveCode = await _apiClient.ReserveTicketTemporarirly(tempReserve);

            if (!string.IsNullOrEmpty(reserveCode) && reserveCode.StartsWith("MRSHOOFER-NO-BAL-", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("ORS insufficient balance for TicketId {Id}", ticket.Id);
                return ($"PAID-NO-RESERVE-{DateTime.Now:yyyyMMddHHmmss}-{ticket.Id}", null);
            }

            // Step 2: confirm reserve
            var confirmReserve = new ConfirmReserveRequestModel
            {
                reservationCode = reserveCode,
                passengerFirstName = ticket.Firstname,
                passengerLastName = ticket.Lastname,
                passengerNationalCode = ticket.NaCode,
                passengerNumberPhone = ticket.PhoneNumber
            };

            var response = await _apiClient.ConfirmReserve(confirmReserve);
            return (response.ticketCode, response.webappToken);
        }
    }
}
