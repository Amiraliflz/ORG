using Application.Data;
using Application.Services;
using Application.Services.MrShooferORS;
using Application.Services.Payment;
using Application.ViewModels.Reserve;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using static System.Runtime.CompilerServices.RuntimeHelpers;
using Application.Models;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;

namespace Application.Areas.AgencyArea
{
  [Area("AgencyArea")]
  // Guests can access most actions - authorization checked per-action
  public class ReserveController : Controller
  {

    private readonly UserManager<IdentityUser> _userManager;
    private readonly MrShooferAPIClient apiclient;
    private readonly AppDbContext context;
    private readonly CustomerServiceSmsSender customerSmsSender;
    private readonly IConfiguration configuration;
    private readonly IPaymentService _paymentService;
    private readonly ILogger<ReserveController> _logger;
    private Agency agency;


    public ReserveController(
      MrShooferAPIClient apiclient, 
      UserManager<IdentityUser> usermanager, 
      AppDbContext context, 
      CustomerServiceSmsSender smssender, 
      IConfiguration configuration,
      IPaymentService paymentService,
      ILogger<ReserveController> logger)
    {
      this.configuration = configuration;
      customerSmsSender = smssender;
      this.context = context;
      _userManager = usermanager;
      this.apiclient = apiclient;
      _paymentService = paymentService;
      _logger = logger;
      
      // Ensure guest agency exists when controller is initialized
      EnsureGuestAgencyExistsAsync().Wait();
    }

    public IActionResult Index()
    {
      return View();
    }


    public async Task<IActionResult> Reservetrip(string tripcode)
    {
      if (string.IsNullOrEmpty(tripcode))
        return BadRequest();

      ViewData["ReservationId"] = tripcode;

      // Retry logic for MrShoofer API
      int retryCount = 0;
      int maxRetries = 3;
      SearchedTrip trip = null;

      while (retryCount < maxRetries)
      {
        try
        {
          trip = await apiclient.GetTripInfo(tripcode);
          ViewBag.trip = trip;
          break; // Success - exit loop
        }
        catch (HttpRequestException ex) when (retryCount < maxRetries - 1)
        {
          retryCount++;
          _logger.LogWarning(ex, "MrShoofer API connection error (attempt {Attempt}/{MaxRetries}) for trip: {TripCode}. Retrying...", 
            retryCount, maxRetries, tripcode);
          await Task.Delay(1000 * retryCount); // Exponential backoff: 1s, 2s, 3s
        }
        catch (HttpRequestException ex)
        {
          _logger.LogError(ex, "Failed to connect to MrShoofer API after {Attempts} attempts for trip: {TripCode}", 
            retryCount + 1, tripcode);
          TempData["ErrorMessage"] = "در حال حاضر امکان اتصال به سرویس رزرو وجود ندارد. لطفا بعدا تلاش کنید";
          return RedirectToAction("Index", "Home", new { area = "AgencyArea" });
        }
        catch (TaskCanceledException ex)
        {
          _logger.LogError(ex, "Request timeout while fetching trip: {TripCode}", tripcode);
          TempData["ErrorMessage"] = "زمان اتصال به سرویس به پایان رسید. لطفا دوباره تلاش کنید";
          return RedirectToAction("Index", "Home", new { area = "AgencyArea" });
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Unexpected error while fetching trip: {TripCode}", tripcode);
          TempData["ErrorMessage"] = "خطایی رخ داده است. لطفا بعدا تلاش کنید";
          return RedirectToAction("Index", "Home", new { area = "AgencyArea" });
        }
      }

      if (trip == null)
      {
        _logger.LogError("Trip info is null after all retry attempts for TripCode: {TripCode}", tripcode);
        TempData["ErrorMessage"] = "اطلاعات سفر یافت نشد. لطفاً بعداً مجدداً تلاش کنید.";
        return RedirectToAction("Index", "Home", new { area = "AgencyArea" });
      }

      // Check if there's saved form data from TempData (after login redirect)
      if (TempData.ContainsKey("SavedReserveData"))
      {
        var savedDataJson = TempData["SavedReserveData"]?.ToString();
        if (!string.IsNullOrEmpty(savedDataJson))
        {
          try
          {
            var savedData = JsonConvert.DeserializeObject<ReserveInfoViewModel>(savedDataJson);
            
            // IMPORTANT: Remove the data from TempData after reading it
            // This ensures it's only used once and won't persist on page refresh
            TempData.Remove("SavedReserveData");
            
            // Pass the saved data to the view
            return View(savedData);
          }
          catch
          {
            // If deserialization fails, remove the corrupted data
            TempData.Remove("SavedReserveData");
          }
        }
      }

      return View();
    }


    [HttpPost]
    public async Task<IActionResult> Reservetrip(ReserveInfoViewModel viewmodel)
    {
      // NO AUTHENTICATION CHECK - Allow all users (guests and authenticated)
      
      if (!ModelState.IsValid)
      {
        return RedirectToAction("Reservetrip", new { tripcode = viewmodel.TripCode });
      }

      // Get trip info with retry logic
      SearchedTrip trip = null;
      int retryCount = 0;
      int maxRetries = 3;

      while (retryCount < maxRetries)
      {
        try
        {
          trip = await apiclient.GetTripInfo(viewmodel.TripCode);
          break; // Success - exit loop
        }
        catch (HttpRequestException ex) when (retryCount < maxRetries - 1)
        {
          retryCount++;
          _logger.LogWarning(ex, "MrShoofer API error (attempt {Attempt}/{MaxRetries}). Retrying...", 
            retryCount, maxRetries);
          await Task.Delay(1000 * retryCount); // Exponential backoff
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Failed to get trip info after {Attempts} attempts for TripCode: {TripCode}", 
            retryCount + 1, viewmodel.TripCode);
          
          TempData["ErrorMessage"] = "خطا در دریافت اطلاعات سفر. لطفاً دوباره تلاش کنید.";
          return RedirectToAction("Index", "Home", new { area = "AgencyArea" });
        }
      }

      if (trip == null)
      {
        TempData["ErrorMessage"] = "اطلاعات سفر یافت نشد.";
        return RedirectToAction("Index", "Home", new { area = "AgencyArea" });
      }

      // For Zarinpal payment, we don't need agency balance, but we'll still fetch it for display
      long agencyBalance = 0;
      if (agency != null)
      {
        try
        {
          apiclient.SetSellerApiKey(agency.ORSAPI_token);
          agencyBalance = (long)Convert.ToDecimal(await apiclient.GetAccountBalance());
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "Failed to get agency balance. Continuing with 0 balance.");
          agencyBalance = 0;
        }
      }

      // Set ViewBag data once
      ViewBag.agancy = agency;
      ViewBag.agancy_balance = agencyBalance;
      ViewBag.trip = trip;
      ViewBag.reserveviewmodel = viewmodel;

      return View("ConfirmInfo");
    }

    [HttpPost]
    public async Task<IActionResult> ConfirmInfo(ConfirmInfoViewModel viewModel)
    {
      // NO AUTHENTICATION CHECK - Allow all users to confirm reservation
      
      _logger.LogInformation("ConfirmInfo POST started. TripCode: {TripCode}, Firstname: {Firstname}, Lastname: {Lastname}, Numberphone: {Numberphone}, Nacode: {Nacode}, Gender: {Gender}", 
        viewModel?.TripCode ?? "NULL", 
        viewModel?.Firstname ?? "NULL", 
        viewModel?.Lastname ?? "NULL",
        viewModel?.Numberphone ?? "NULL",
        viewModel?.Nacode ?? "NULL",
        viewModel?.Gender ?? "NULL");
      
      // Validate model
      if (!ModelState.IsValid)
      {
        var errors = ModelState
          .Where(x => x.Value.Errors.Count > 0)
          .Select(x => new { Field = x.Key, Errors = x.Value.Errors.Select(e => e.ErrorMessage).ToList() })
          .ToList();
        
        _logger.LogWarning("ModelState invalid. Errors: {Errors}", 
          JsonConvert.SerializeObject(errors));
        
        // Return user back to reservation page with errors
        TempData["ErrorMessage"] = "اطلاعات فرم ناقص است: " + string.Join(", ", errors.SelectMany(e => e.Errors));
        return RedirectToAction("Reservetrip", new { tripcode = viewModel?.TripCode });
      }
      
      _logger.LogInformation("ModelState is valid. Proceeding with payment request...");
      
      // Get trip info for pricing with retry logic
      SearchedTrip trip = null;
      int retryCount = 0;
      int maxRetries = 3;
      
      while (retryCount < maxRetries)
      {
        try
        {
          trip = await apiclient.GetTripInfo(viewModel.TripCode);
          break; // Success - exit loop
        }
        catch (HttpRequestException ex) when (retryCount < maxRetries - 1)
        {
          retryCount++;
          _logger.LogWarning(ex, "MrShoofer API error (attempt {Attempt}/{MaxRetries}). Retrying...", 
            retryCount, maxRetries);
          await Task.Delay(1000 * retryCount); // Exponential backoff: 1s, 2s, 3s
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Failed to get trip info after {Attempts} attempts for TripCode: {TripCode}", 
            retryCount + 1, viewModel.TripCode);
          
          TempData["ErrorMessage"] = "در حال حاضر امکان اتصال به سرویس رزرو وجود ندارد. لطفاً چند دقیقه دیگر مجدداً تلاش کنید.";
          return RedirectToAction("Reservetrip", new { tripcode = viewModel.TripCode });
        }
      }
      
      if (trip == null)
      {
        _logger.LogError("Trip info is null after all retry attempts for TripCode: {TripCode}", viewModel.TripCode);
        TempData["ErrorMessage"] = "اطلاعات سفر یافت نشد. لطفاً دوباره تلاش کنید.";
        return RedirectToAction("Index", "Home", new { area = "AgencyArea" });
      }

      // ✅ STEP 1: CREATE PRELIMINARY TICKET (BEFORE PAYMENT, WITHOUT MRSHOOFER TICKETCODE)
      // Store trip code temporarily - we'll create MrShoofer reservation after payment
      Ticket newticket = new Ticket()
      {
        Firstname = viewModel.Firstname,
        Lastname = viewModel.Lastname,
        PhoneNumber = viewModel.Numberphone,
        NaCode = viewModel.Nacode,
        TicketFinalPrice = trip.afterdiscticketprice,
        Gender = viewModel.Gender,
        TicketOriginalPrice = trip.originalTicketprice,
        TripOrigin = trip.originCityName,
        TripDestination = trip.destinationCityName,
        RegisteredAt = DateTime.Now,
        Tripcode = trip.tripPlanCode,
        ServiceName = trip.taxiSupervisorName,
        CarName = trip.carModelName,
        // ⚠️ Temporary ticket code - will be replaced with MrShoofer ticket code after payment
        TicketCode = $"PENDING-{DateTime.Now:yyyyMMddHHmmss}",
        IsPaid = false
      };

      // Associate with agency
      if (agency != null)
      {
        newticket.Agency = agency;
      }
      else
      {
        var guestAgency = context.Agencies
          .FirstOrDefault(a => a.IdentityUser == null && a.Name.Contains("مهمان"));
        if (guestAgency != null)
        {
          newticket.Agency = guestAgency;
        }
      }

      context.Tickets.Add(newticket);
      await context.SaveChangesAsync();

      _logger.LogInformation("Preliminary ticket saved to database. TripCode: {TripCode}, TicketId: {TicketId}, Price: {Price}", 
        viewModel.TripCode, newticket.Id, newticket.TicketFinalPrice);

      // ✅ STEP 2: REQUEST PAYMENT FROM ZARINPAL (BEFORE MRSHOOFER RESERVATION)
      int amountInRials = newticket.TicketFinalPrice * 10; // Convert Toman to Rial
      string description = $"خرید بلیط {newticket.TripOrigin} به {newticket.TripDestination}";
      
      _logger.LogInformation("Requesting payment. Amount in Rials: {Amount}, TripCode: {TripCode}", 
        amountInRials, viewModel.TripCode);
      
      var (success, authority, message) = await _paymentService.RequestPaymentAsync(
        amountInRials,
        description,
        newticket.PhoneNumber,
        null // Email is optional
      );

      if (success)
      {
        // Save payment authority and ticket ID for callback
        newticket.PaymentAuthority = authority;
        await context.SaveChangesAsync();

        _logger.LogInformation("Payment request successful. Authority: {Authority}, TicketId: {TicketId}", 
          authority, newticket.Id);

        // ✅ STEP 3: REDIRECT TO PAYMENT GATEWAY
        var paymentUrl = _paymentService.GetPaymentGatewayUrl(authority);
        
        _logger.LogInformation("Redirecting to payment gateway. URL: {PaymentUrl}", paymentUrl);
        
        return Redirect(paymentUrl);
      }
      else
      {
        // Payment request failed
        _logger.LogError("Zarinpal payment request failed. Message: {Message}, TripCode: {TripCode}", 
          message, viewModel.TripCode);
        
        // Delete the preliminary ticket since payment failed
        context.Tickets.Remove(newticket);
        await context.SaveChangesAsync();
        
        TempData["ErrorMessage"] = message;
        return RedirectToAction("PaymentFailed", "Payment");
      }
    }

    public async Task<IActionResult> ReserveConfirmed(string ticketcode)
    {
      var ticket = context.Tickets.Where(t => t.TicketCode == ticketcode).FirstOrDefault();
      
      if (ticket == null)
      {
        return NotFound();
      }

      // Check if ticket is paid
      if (!ticket.IsPaid)
      {
        return RedirectToAction("PaymentFailed", "Payment", new { message = "پرداخت هنوز تایید نشده است" });
      }
      
      ViewBag.trip = await apiclient.GetTripInfo(ticket.Tripcode);
      ViewBag.ticket = ticket;

      // Send SMS to customer after successful payment
      try
      {
        var service_url = configuration["serivce_url"];
        var trip_link = ticket.TicketCode;
        await customerSmsSender.SendCustomerTicket_issued(
          ticket.Firstname, 
          ticket.Lastname, 
          ticket.TicketCode, 
          trip_link, 
          ticket.PhoneNumber
        );
      }
      catch
      {
        // Log error but don't fail the request
      }

      return View();
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
      base.OnActionExecuting(context);

      string tokenToUse = null;
      Agency agencyToUse = null;

      // Use agency token if authenticated, otherwise use guest agency
      if (User.Identity.IsAuthenticated)
      {
        var identityUser = _userManager.GetUserAsync(User).Result;
        agencyToUse = this.context.Agencies
          .FirstOrDefault(a => a.IdentityUser == identityUser);

        if (agencyToUse != null && !string.IsNullOrWhiteSpace(agencyToUse.ORSAPI_token))
        {
          tokenToUse = agencyToUse.ORSAPI_token;
        }
      }

      // If no agency found (guest user or authenticated user without agency)
      if (agencyToUse == null)
      {
        // Get the guest agency (agency with no IdentityUser)
        agencyToUse = this.context.Agencies
          .FirstOrDefault(a => a.IdentityUser == null && a.Name.Contains("مهمان"));
        
        if (agencyToUse != null && !string.IsNullOrWhiteSpace(agencyToUse.ORSAPI_token))
        {
          tokenToUse = agencyToUse.ORSAPI_token;
        }
      }

      // Fallback to configuration token if no agency token found
      if (string.IsNullOrWhiteSpace(tokenToUse))
      {
        tokenToUse = configuration["MrShoofer:SellerToken"];
      }

      if (!string.IsNullOrWhiteSpace(tokenToUse))
      {
        apiclient.SetSellerApiKey(tokenToUse);
      }

      // Store the agency for use in actions
      agency = agencyToUse;
    }

    private async Task EnsureGuestAgencyExistsAsync()
    {
      // Check if guest agency already exists
      var guestAgency = await context.Agencies
        .FirstOrDefaultAsync(a => a.IdentityUser == null && a.Name.Contains("مهمان"));

      if (guestAgency == null)
      {
        // Create guest agency
        var newGuestAgency = new Agency
        {
          Name = "مستر شوفر - مهمان",
          PhoneNumber = "02100000000",
          Address = "تهران",
          AdminMobile = "09900000000",
          DateJoined = DateTime.Now,
          ORSAPI_token = configuration["MrShoofer:SellerToken"] ?? "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1laWRlbnRpZmllciI6IjU1NCIsImp0aSI6Ijk1ZTI1NjYzLTkzM2EtNGY1ZS04ZTdiLTMwNGQ0Yjg3M2Q3NiIsImV4cCI6MTkyMjc3ODkwOCwiaXNzIjoibXJzaG9vZmVyLmlyIiwiYXVkIjoibiJzaG9vZmVyLmlyIn0.2r5WoGmqb5Ra_6epV5jR3Y0RlHs5bcwE0li0wo1ricE",
          Commission = 0,
          IdentityUser = null
        };

        context.Agencies.Add(newGuestAgency);
        await context.SaveChangesAsync();
      }
    }
  }
}
