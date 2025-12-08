using Application.Data;
using Application.Services;
using Application.Services.MrShooferORS;
using Application.ViewModels.Reserve;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using System.Data.Entity;
using System.Diagnostics;
using static System.Runtime.CompilerServices.RuntimeHelpers;
using Application.Models;
using Newtonsoft.Json;

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
    private Agency agency;


    public ReserveController(MrShooferAPIClient apiclient, UserManager<IdentityUser> usermanager, AppDbContext context, CustomerServiceSmsSender smssender, IConfiguration configuration)
    {
      this.configuration = configuration;
      customerSmsSender = smssender;
      this.context = context;
      _userManager = usermanager;
      this.apiclient = apiclient;

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

      var trip = await apiclient.GetTripInfo(tripcode);

      // Getting agancy account balance from ORS - only if authenticated
      if (User.Identity.IsAuthenticated)
      {
        var agancy_balance = (int)Convert.ToDouble(await apiclient.GetAccountBalance());

        ViewBag.agancy_balance = agancy_balance;


        if (agancy_balance >= trip.afterdiscticketprice)
        {
          ViewBag.canbuy = true;
        }
        // Cannot submit the ticket
        else
        {
          ViewBag.canbuy = false;
        }
      }
      else
      {
        // Guest user - show they need to login
        ViewBag.agancy_balance = 0;
        ViewBag.canbuy = false;
        ViewBag.isGuest = true;
      }

      ViewBag.trip = trip;

      // Check if there's saved form data from TempData (after login redirect)
      if (TempData.ContainsKey("SavedReserveData"))
      {
        var savedDataJson = TempData["SavedReserveData"]?.ToString();
        if (!string.IsNullOrEmpty(savedDataJson))
        {
          try
          {
            var savedData = JsonConvert.DeserializeObject<ReserveInfoViewModel>(savedDataJson);
            // Pass the saved data to the view
            return View(savedData);
          }
          catch
          {
            // If deserialization fails, just show empty form
          }
        }
      }

      return View();
    }


    [HttpPost]
    public async Task<IActionResult> Reservetrip(ReserveInfoViewModel viewmodel)
    {
      // Check if user is authenticated before allowing reservation
      if (!User.Identity.IsAuthenticated)
      {
        // Save the form data in TempData before redirecting to login
        TempData["SavedReserveData"] = JsonConvert.SerializeObject(viewmodel);
        TempData.Keep("SavedReserveData"); // Ensure it persists across redirects
        
        // Return JSON to trigger modal on client side
        var returnUrl = Url.Action("Reservetrip", "Reserve", new { tripcode = viewmodel.TripCode, area = "AgencyArea" });
        return Json(new { requiresAuth = true, returnUrl = returnUrl });
      }

      if (!ModelState.IsValid)
      {
        return RedirectToAction("Reservetrip", new { tripcode = viewmodel.TripCode });
      }


      var trip = await apiclient.GetTripInfo(viewmodel.TripCode);

      var agancy_balance = (int)Convert.ToDouble(await apiclient.GetAccountBalance());

      ViewBag.agancy_balance = agancy_balance;

      ViewBag.agancy = agency;
      ViewBag.trip = trip;
      ViewBag.reserveviewmodel = viewmodel;

      return View("ConfirmInfo");
    }

    [HttpPost]
    public async Task<IActionResult> ConfirmInfo(ConfirmInfoViewModel viewModel)
    {
      // Check if user is authenticated before confirming reservation
      if (!User.Identity.IsAuthenticated)
      {
        return Json(new { requiresAuth = true, returnUrl = Url.Action("Reservetrip", "Reserve", new { tripcode = viewModel.TripCode }) });
      }

      // Registering the ticket


      // Issuing ticket in ORS
      // TempReserve



        TicketTempReserveRequestModel tempreserve_viewodel = new TicketTempReserveRequestModel()
        {
          isPrivate = true,
          tripCode = viewModel.TripCode
        };

        var reservecode = await apiclient.ReserveTicketTemporarirly(tempreserve_viewodel);


        // final reserve

        ConfirmReserveRequestModel confirmreserve_viewmodel = new ConfirmReserveRequestModel()
        {
          passengerFirstName = viewModel.Firstname,
          passengerLastName = viewModel.Lastname,
          reservationCode = reservecode,
          passengerNationalCode = viewModel.Nacode,
          passengerNumberPhone = viewModel.Numberphone
        };


      TicketConfirmationResponse reserve_response = null;

        try
        {
           reserve_response = await apiclient.ConfirmReserve(confirmreserve_viewmodel);
        }
        catch (Exception e)
        {
          return RedirectToAction("Index", "Home");
        }


        // Getting trip_info

        var trip = await apiclient.GetTripInfo(viewModel.TripCode);

        //Creating ticket object
        Ticket newticket = new Ticket()
        {
          Firstname = viewModel.Firstname,
          Lastname = viewModel.Lastname,
          PhoneNumber = viewModel.Numberphone,
          NaCode = viewModel.Nacode,
          TicketFinalPrice = reserve_response.paid_total_fee_tomans,
          Gender = viewModel.Gender,
          TicketOriginalPrice = trip.originalTicketprice,
          TripOrigin = trip.originCityName,
          TripDestination = trip.destinationCityName,
          RegisteredAt = DateTime.Now,
          TicketCode = reserve_response.ticketCode,
          Tripcode = trip.tripPlanCode,
          ServiceName = trip.taxiSupervisorName,
          CarName = trip.carModelName
        };

        // Registering to database


        var identity_user = await _userManager.GetUserAsync(User);

        var agancy = context.Agencies.Where(a => a.IdentityUser == identity_user).FirstOrDefault();
        newticket.Agency = agancy;


        context.Tickets.Add(newticket);

        await context.SaveChangesAsync();



        //Sending SMS for customer


        var service_url = configuration["serivce_url"];
        var trip_link = newticket.TicketCode;


      try
      {

        await customerSmsSender.SendCustomerTicket_issued(newticket.Firstname, newticket.Lastname, newticket.TicketCode, trip_link, newticket.PhoneNumber);
      }
      catch
      {

      }



        return RedirectToAction("ReserveConfirmed", new { ticketcode = newticket.TicketCode });
      }





    private async Task DoConfirmResreve()
    {

    }



    [Authorize] // This one requires authentication to view confirmation
    public async Task<IActionResult> ReserveConfirmed(string ticketcode)
    {
      var ticket = context.Tickets.Where(t => t.TicketCode == ticketcode).FirstOrDefault();
      ViewBag.trip = await apiclient.GetTripInfo(ticket.Tripcode);
      ViewBag.ticket = ticket;


      return View();
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
      base.OnActionExecuting(context);

      string tokenToUse = null;

      // Use agency token if authenticated, otherwise use guest/default token
      if (User.Identity.IsAuthenticated)
      {
        var identityUser = _userManager.GetUserAsync(User).Result;
        agency = this.context.Agencies.FirstOrDefault(a => a.IdentityUser == identityUser);

        if (agency != null && !string.IsNullOrWhiteSpace(agency.ORSAPI_token))
        {
          tokenToUse = agency.ORSAPI_token;
        }
      }

      // Fallback to guest token from configuration
      if (string.IsNullOrWhiteSpace(tokenToUse))
      {
        tokenToUse = configuration["MrShoofer:SellerToken"];
      }

      if (!string.IsNullOrWhiteSpace(tokenToUse))
      {
        apiclient.SetSellerApiKey(tokenToUse);
      }
    }
  }
}
