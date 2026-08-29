using Application.Controllers;
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
using System.Collections.Concurrent;
using System.Diagnostics;
using static System.Runtime.CompilerServices.RuntimeHelpers;
using Application.Models;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

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
    private readonly TicketIssuer _ticketIssuer;
    private readonly CustomerBalanceService _balanceSvc;
    private readonly LoyaltyService _loyaltySvc;
    private readonly IWebHostEnvironment _env;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Application.Services.Neshan.NeshanApiClient _neshan;
    private Agency agency;


    public ReserveController(
      MrShooferAPIClient apiclient,
      UserManager<IdentityUser> usermanager,
      AppDbContext context,
      CustomerServiceSmsSender smssender,
      IConfiguration configuration,
      IPaymentService paymentService,
      ILogger<ReserveController> logger,
      TicketIssuer ticketIssuer,
      CustomerBalanceService balanceSvc,
      LoyaltyService loyaltySvc,
      IWebHostEnvironment env,
      IHttpClientFactory httpClientFactory,
      Application.Services.Neshan.NeshanApiClient neshan)
    {
      this.configuration = configuration;
      customerSmsSender = smssender;
      this.context = context;
      _userManager = usermanager;
      this.apiclient = apiclient;
      _paymentService = paymentService;
      _logger = logger;
      _ticketIssuer = ticketIssuer;
      _balanceSvc = balanceSvc;
      _loyaltySvc = loyaltySvc;
      _env = env;
      _httpClientFactory = httpClientFactory;
      _neshan = neshan;

      // Ensure guest agency exists when controller is initialized
      EnsureGuestAgencyExistsAsync().Wait();
    }

    public IActionResult Index()
    {
      return View();
    }

    /// <summary>
    /// Dev-only Snapp-style map booking UX. Not served outside Development.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [Route("/Reserve/MapBook")]
    public IActionResult MapBook()
    {
      ViewData["Title"] = "رزرو روی نقشه";
      ViewData["HideFrontFooter"] = true;
      ViewData["IncludeSiteNav"] = false;
      // Soft-launch: keep out of sitemap/index until funnel is proven
      ViewData["robots"] = "noindex, nofollow";
      ViewData["CartoApiKey"] = configuration["Carto:ApiKey"] ?? "";
      ViewData["NeshanWebApiKey"] = configuration["Neshan:WebApiKey"] ?? "";
      ViewData["MapBookCitiesJson"] = ReadWwwrootJson("data/iran/cities.json");
      ViewData["MapBookZonesJson"] = ReadWwwrootJson("data/iran/tehran-restriction-zones.json");
      ViewData["MapBookCityBordersJson"] = ReadWwwrootJson("data/iran/city-borders.json");
      ViewData["MapBookOrigin"] = (Request.Query["origin"].FirstOrDefault()
        ?? Request.Query["originstring"].FirstOrDefault() ?? "").Trim();
      ViewData["MapBookDest"] = (Request.Query["dest"].FirstOrDefault()
        ?? Request.Query["destination"].FirstOrDefault()
        ?? Request.Query["destinationstring"].FirstOrDefault() ?? "").Trim();
      return View();
    }

    private static readonly ConcurrentDictionary<string, string> WwwrootJsonCache = new(StringComparer.Ordinal);

    private string ReadWwwrootJson(string relativePath)
    {
      return WwwrootJsonCache.GetOrAdd(relativePath, rel =>
      {
        var full = Path.Combine(_env.WebRootPath, rel.Replace('/', Path.DirectorySeparatorChar));
        return System.IO.File.Exists(full)
          ? System.IO.File.ReadAllText(full)
          : "{}";
      });
    }

    /// <summary>
    /// MapBook routing: Neshan road geometry first, then local/public OSRM, then curve fallback.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [Route("/Reserve/OsrmRoute")]
    public async Task<IActionResult> OsrmRoute(
      double oLat, double oLng, double dLat, double dLng,
      CancellationToken cancellationToken)
    {
      if (!IsValidLatLng(oLat, oLng) || !IsValidLatLng(dLat, dLng))
        return BadRequest(new { error = "invalid coordinates" });

      try
      {
        var neshan = await _neshan.GetDrivingRouteAsync(oLat, oLng, dLat, dLng, cancellationToken);
        if (neshan != null && neshan.Coordinates.Count >= 2)
        {
          return Json(new
          {
            code = "Ok",
            routes = new[]
            {
              new
              {
                distance = neshan.DistanceMeters,
                duration = neshan.DurationSeconds,
                geometry = new
                {
                  type = "LineString",
                  coordinates = neshan.Coordinates
                    .Select(c => new[] { c.Lng, c.Lat })
                    .ToArray()
                }
              }
            },
            source = "neshan"
          });
        }
      }
      catch (Exception ex)
      {
        _logger.LogDebug(ex, "Neshan direction failed for MapBook route");
      }

      var localBase = configuration["Osrm:BaseUrl"]?.TrimEnd('/');
      var candidates = new List<string>();
      if (!string.IsNullOrWhiteSpace(localBase))
        candidates.Add($"{localBase}/route/v1/driving/{oLng},{oLat};{dLng},{dLat}?overview=full&geometries=geojson");
      candidates.Add($"https://router.project-osrm.org/route/v1/driving/{oLng},{oLat};{dLng},{dLat}?overview=full&geometries=geojson");

      var client = _httpClientFactory.CreateClient();
      client.Timeout = TimeSpan.FromSeconds(8);

      foreach (var url in candidates)
      {
        try
        {
          using var res = await client.GetAsync(url, cancellationToken);
          if (!res.IsSuccessStatusCode) continue;
          var json = await res.Content.ReadAsStringAsync(cancellationToken);
          // Tag source without breaking OSRM shape for the client
          if (json.Length > 2 && json[0] == '{')
            return Content(json.Insert(1, "\"source\":\"osrm\","), "application/json");
          return Content(json, "application/json");
        }
        catch (Exception ex)
        {
          _logger.LogDebug(ex, "OSRM candidate failed: {Url}", url);
        }
      }

      return Json(new
      {
        code = "Ok",
        routes = new[]
        {
          new
          {
            distance = HaversineMeters(oLat, oLng, dLat, dLng),
            duration = HaversineMeters(oLat, oLng, dLat, dLng) / 22.0,
            geometry = new
            {
              type = "LineString",
              coordinates = BuildFallbackLine(oLng, oLat, dLng, dLat)
            }
          }
        },
        source = "fallback"
      });
    }

    /// <summary>
    /// Place search for MapBook (محله / خیابان / کوچه). Neshan geocode + reverse labels, OSM Nominatim fallback.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [Route("/Reserve/PlaceSearch")]
    public async Task<IActionResult> PlaceSearch(
      string q,
      string? city = null,
      double? lat = null,
      double? lng = null,
      CancellationToken cancellationToken = default)
    {
      q = (q ?? string.Empty).Trim();
      if (q.Length < 2)
        return Json(Array.Empty<object>());

      city = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
      var query = city == null ? q : $"{city} {q}";
      var results = new List<object>();
      var seen = new HashSet<string>();

      void Add(string title, string? subtitle, double rLat, double rLng, string source)
      {
        var key = $"{Math.Round(rLat, 4)}:{Math.Round(rLng, 4)}";
        if (!seen.Add(key)) return;
        results.Add(new
        {
          title,
          subtitle = subtitle ?? "",
          lat = rLat,
          lng = rLng,
          source
        });
      }

      try
      {
        var primary = await _neshan.GeocodeAsync(query, cancellationToken);
        if (primary is { } p)
        {
          var rev = await _neshan.ReverseAsync(p.Lat, p.Lng, cancellationToken);
          Add(
            rev?.Title ?? q,
            rev?.Subtitle ?? city,
            p.Lat, p.Lng,
            "neshan");
        }

        var candidates = await _neshan.GeocodeCandidatesAsync(query, limit: 4, cancellationToken);
        foreach (var c in candidates.Take(3))
        {
          if (results.Count >= 8) break;
          var rev = await _neshan.ReverseAsync(c.Lat, c.Lng, cancellationToken);
          Add(
            rev?.Title ?? q,
            rev?.Subtitle ?? city,
            c.Lat, c.Lng,
            "neshan");
        }
      }
      catch (Exception ex)
      {
        _logger.LogDebug(ex, "Neshan place search failed");
      }

      // OSM Nominatim for named districts / alleys (labels)
      try
      {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(6);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MrShooferORG-MapBook/1.0");

        var qs = new List<string>
        {
          $"q={Uri.EscapeDataString(query)}",
          "format=json",
          "addressdetails=1",
          "limit=8",
          "countrycodes=ir",
          "accept-language=fa"
        };
        if (lat is double clat && lng is double clng && IsValidLatLng(clat, clng))
        {
          // ~0.25° box ≈ 25km
          var d = 0.22;
          qs.Add($"viewbox={clng - d},{clat + d},{clng + d},{clat - d}");
          qs.Add("bounded=1");
        }

        var url = "https://nominatim.openstreetmap.org/search?" + string.Join("&", qs);
        using var res = await client.GetAsync(url, cancellationToken);
        if (res.IsSuccessStatusCode)
        {
          var json = await res.Content.ReadAsStringAsync(cancellationToken);
          using var doc = System.Text.Json.JsonDocument.Parse(json);
          foreach (var el in doc.RootElement.EnumerateArray())
          {
            if (results.Count >= 10) break;
            if (!el.TryGetProperty("lat", out var latEl) || !el.TryGetProperty("lon", out var lonEl))
              continue;
            if (!double.TryParse(latEl.GetString(), System.Globalization.NumberStyles.Float,
                  System.Globalization.CultureInfo.InvariantCulture, out var rLat))
              continue;
            if (!double.TryParse(lonEl.GetString(), System.Globalization.NumberStyles.Float,
                  System.Globalization.CultureInfo.InvariantCulture, out var rLng))
              continue;
            if (!IsValidLatLng(rLat, rLng)) continue;

            var name = el.TryGetProperty("name", out var n) ? n.GetString() : null;
            var display = el.TryGetProperty("display_name", out var dn) ? dn.GetString() : null;
            var type = el.TryGetProperty("type", out var t) ? t.GetString() : null;
            var title = string.IsNullOrWhiteSpace(name) ? (display?.Split(',')[0] ?? q) : name!;
            var subtitle = display ?? type ?? city;
            Add(title, subtitle, rLat, rLng, "osm");
          }
        }
      }
      catch (Exception ex)
      {
        _logger.LogDebug(ex, "Nominatim place search failed");
      }

      return Json(results);
    }

    [HttpGet]
    [AllowAnonymous]
    [Route("/Reserve/ReverseGeocode")]
    public async Task<IActionResult> ReverseGeocode(
      double lat, double lng, CancellationToken cancellationToken = default)
    {
      if (!IsValidLatLng(lat, lng))
        return BadRequest(new { error = "invalid coordinates" });

      var rev = await _neshan.ReverseAsync(lat, lng, cancellationToken);
      if (rev == null)
        return Json(new { title = "موقعیت روی نقشه", subtitle = "" });

      return Json(new
      {
        title = rev.Title,
        subtitle = rev.Subtitle,
        neighbourhood = rev.Neighbourhood,
        route = rev.Route,
        city = rev.City,
        inTrafficZone = rev.InTrafficZone,
        inOddEvenZone = rev.InOddEvenZone
      });
    }

    private static bool IsValidLatLng(double lat, double lng) =>
      lat is >= 24 and <= 41 && lng is >= 43 and <= 64;

    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
      const double R = 6371000;
      var dLat = (lat2 - lat1) * Math.PI / 180;
      var dLon = (lon2 - lon1) * Math.PI / 180;
      var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
        + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
        * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
      return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double[][] BuildFallbackLine(double oLng, double oLat, double dLng, double dLat)
    {
      var midLng = (oLng + dLng) / 2;
      var midLat = (oLat + dLat) / 2;
      var dx = dLng - oLng;
      var dy = dLat - oLat;
      var bend = 0.12;
      var cLng = midLng - dy * bend;
      var cLat = midLat + dx * bend;
      var pts = new List<double[]>();
      for (var i = 0; i <= 48; i++)
      {
        var t = i / 48.0;
        var u = 1 - t;
        pts.Add(new[]
        {
          u * u * oLng + 2 * u * t * cLng + t * t * dLng,
          u * u * oLat + 2 * u * t * cLat + t * t * dLat
        });
      }
      return pts.ToArray();
    }


    public async Task<IActionResult> Reservetrip(string tripcode)
    {
      if (string.IsNullOrEmpty(tripcode))
        return BadRequest();

      // Capture optional webapp token from query string (supports both "webapptoken" and short "t")
      var webappToken = Request.Query["webapptoken"].FirstOrDefault()
        ?? Request.Query["t"].FirstOrDefault();

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
        TempData["ErrorMessage"] = "اطلاعات سفر یافت نشد. لطفا بعدا تلاش کنید";
        return RedirectToAction("Index", "Home", new { area = "AgencyArea" });
      }

      ViewBag.ShowNorthPriceNotice = NorthRoutePriceNotice.ShouldShow(
        trip.originCityName,
        trip.destinationCityName,
        trip.startingDateTime);

      // MapBook pin handoff (query or will be filled from client session in the view)
      ViewBag.MapOriginLabel = (Request.Query["olabel"].FirstOrDefault() ?? "").Trim();
      ViewBag.MapDestLabel = (Request.Query["dlabel"].FirstOrDefault() ?? "").Trim();
      ViewBag.MapOriginLat = Request.Query["olat"].FirstOrDefault();
      ViewBag.MapOriginLng = Request.Query["olng"].FirstOrDefault();
      ViewBag.MapDestLat = Request.Query["dlat"].FirstOrDefault();
      ViewBag.MapDestLng = Request.Query["dlng"].FirstOrDefault();

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
            if (string.IsNullOrWhiteSpace(savedData.WebappToken))
            {
              savedData.WebappToken = webappToken;
            }

            ViewBag.WebappToken = savedData.WebappToken;

            return View(savedData);
          }
          catch
          {
            // If deserialization fails, remove the corrupted data
            TempData.Remove("SavedReserveData");
          }
        }
      }

      ViewBag.WebappToken = webappToken;

      return View();
    }


    [HttpPost]
    public async Task<IActionResult> Reservetrip(ReserveInfoViewModel viewmodel)
    {
      // NO AUTHENTICATION CHECK - Allow all users (guests and authenticated)

      // Normalize Persian/Arabic-Indic digits to ASCII before validation
      if (viewmodel != null)
      {
        viewmodel.NaCode      = NormalizePersianDigits(viewmodel.NaCode);
        viewmodel.NumebrPhone = NormalizePersianDigits(viewmodel.NumebrPhone);
        ModelState.Clear();
        TryValidateModel(viewmodel);
      }

      if (!ModelState.IsValid)
      {
        return RedirectToAction("Reservetrip", new { tripcode = viewmodel?.TripCode });
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

      // Pass customer balance if logged in as customer
      decimal customerBalance = 0;
      bool isCustomer = User.HasClaim("Role", "Customer");
      if (isCustomer)
      {
        var cu = await _userManager.GetUserAsync(User);
        if (cu != null) customerBalance = await _balanceSvc.GetBalance(cu.Id);
      }

      // Set ViewBag data once
      ViewBag.agancy = agency;
      ViewBag.agancy_balance = agencyBalance;
      ViewBag.trip = trip;
      ViewBag.reserveviewmodel = viewmodel;
      ViewBag.IsCustomer = isCustomer;
      ViewBag.CustomerBalance = customerBalance;
      ViewBag.ShowNorthPriceNotice = NorthRoutePriceNotice.ShouldShow(
        trip.originCityName,
        trip.destinationCityName,
        trip.startingDateTime);

      var loyaltyPhone = viewmodel?.NumebrPhone?.Trim();
      if (isCustomer)
      {
        var cu = await _userManager.GetUserAsync(User);
        if (cu != null && !string.IsNullOrWhiteSpace(cu.UserName))
          loyaltyPhone = cu.UserName;
      }

      if (LoyaltyService.FeatureEnabled)
        ViewBag.Loyalty = await _loyaltySvc.GetInfoAsync(loyaltyPhone);

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
        return RedirectToAction("Reservetrip", new { tripcode = viewModel?.TripCode, webapptoken = viewModel?.WebappToken });
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
      // Normalize digits so ORS API never receives Persian/Arabic-Indic numerals
      var safeNacode = NormalizePersianDigits(viewModel.Nacode);
      var safePhone  = NormalizePersianDigits(viewModel.Numberphone);

      Ticket newticket = new Ticket()
      {
        Firstname = viewModel.Firstname,
        Lastname = viewModel.Lastname,
        PhoneNumber = safePhone,
        NaCode = safeNacode,
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
        IsPaid = false,
        // Store WebappToken if provided by client
        WebappToken = viewModel.WebappToken
      };

      // Associate with agency
      if (agency != null)
      {
        newticket.Agency = agency;
      }
      else
      {
        var guestAgency = context.Agencies
          .FirstOrDefault(a => a.IdentityUser != null && a.IdentityUser.UserName == "Sale.mrshoofer");
        if (guestAgency != null)
        {
          newticket.Agency = guestAgency;
        }
      }

      // Apply discount code if provided
      if (!string.IsNullOrWhiteSpace(viewModel.DiscountCode))
      {
        var discountEntity = await context.DiscountCodes
            .FirstOrDefaultAsync(d => d.Code == viewModel.DiscountCode.Trim().ToUpper() && d.IsActive);

        if (discountEntity != null
            && (!discountEntity.ExpiryDate.HasValue || discountEntity.ExpiryDate >= DateTime.Now)
            && (!discountEntity.MaxUses.HasValue || discountEntity.UsedCount < discountEntity.MaxUses))
        {
          var userPhone = viewModel.Numberphone?.Trim();
          var alreadyUsed = !discountEntity.AllowMultipleUsePerUser
              && !string.IsNullOrWhiteSpace(userPhone)
              && await context.DiscountCodeUsages.AnyAsync(u => u.DiscountCodeId == discountEntity.Id && u.UserPhone == userPhone);

          if (!alreadyUsed)
          {
            var discounted = (int)Math.Round(newticket.TicketFinalPrice * (1 - discountEntity.DiscountPercent / 100m));
            newticket.TicketFinalPrice = discounted;
            discountEntity.UsedCount++;

            if (!string.IsNullOrWhiteSpace(userPhone))
            {
              context.DiscountCodeUsages.Add(new Application.Models.DiscountCodeUsage
              {
                DiscountCodeId = discountEntity.Id,
                UserPhone      = userPhone,
                UsedAt         = DateTime.Now
              });
            }
          }
        }
      }

      var loyaltyPhone = viewModel.Numberphone?.Trim();
      if (User.HasClaim("Role", "Customer"))
      {
        var cu = await _userManager.GetUserAsync(User);
        if (cu != null && !string.IsNullOrWhiteSpace(cu.UserName))
          loyaltyPhone = cu.UserName;
      }

      if (LoyaltyService.FeatureEnabled)
      {
        var loyalty = await _loyaltySvc.GetInfoAsync(loyaltyPhone);
        if (loyalty.DiscountPercent > 0)
        {
          newticket.TicketFinalPrice = LoyaltyService.ApplyTierDiscount(newticket.TicketFinalPrice, loyalty.DiscountPercent);
        }
      }

      context.Tickets.Add(newticket);
      await context.SaveChangesAsync();

      _logger.LogInformation("Preliminary ticket saved to database. TripCode: {TripCode}, TicketId: {TicketId}, Price: {Price}",
        viewModel.TripCode, newticket.Id, newticket.TicketFinalPrice);

      // ✅ STEP 2: PROCESS PAYMENT
      if (viewModel.PaymentMethod == "balance")
      {
        // --- Balance payment path ---
        if (!User.HasClaim("Role", "Customer"))
        {
          TempData["ErrorMessage"] = "برای پرداخت از کیف پول ابتدا وارد حساب کاربری خود شوید";
          return RedirectToAction("Reservetrip", new { tripcode = viewModel.TripCode });
        }

        var identityUser = await _userManager.GetUserAsync(User);

        var (deducted, _) = await _balanceSvc.DeductBalance(identityUser!.Id, newticket.TicketFinalPrice);
        if (!deducted)
        {
          TempData["ErrorMessage"] = "موجودی کیف پول کافی نیست";
          return RedirectToAction("Reservetrip", new { tripcode = viewModel.TripCode });
        }

        // ORS reservation
        string orsTicketCode;
        string? orsWebappToken = null;
        try
        {
          (orsTicketCode, orsWebappToken) = await _ticketIssuer.ReserveWithOrsAsync(newticket);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "ORS reservation failed after balance deduction. TicketId: {Id}", newticket.Id);

          // Refund balance
          await _balanceSvc.AddBalance(identityUser.Id, newticket.TicketFinalPrice);

          TempData["ErrorMessage"] = "خطا در رزرو سفر. مبلغ به کیف پول شما بازگشت داده شد.";
          return RedirectToAction("Reservetrip", new { tripcode = viewModel.TripCode });
        }

        newticket.TicketCode = orsTicketCode;
        newticket.IsPaid = true;
        newticket.PaidAt = DateTime.Now;
        newticket.PaymentRefId = "WALLET";
        if (!string.IsNullOrWhiteSpace(orsWebappToken))
          newticket.WebappToken = orsWebappToken;

        await context.SaveChangesAsync();
        _logger.LogInformation("Balance payment completed. TicketId: {Id}, TicketCode: {Code}", newticket.Id, newticket.TicketCode);

        return RedirectToAction("ReserveConfirmed", new { ticketcode = newticket.TicketCode });
      }

      // --- Zarinpal (online) payment path ---
      // The payment server's IP is whitelisted with Zarinpal — it calls Zarinpal and redirects user.
      var paymentServerBase = configuration["PaymentServer:BaseUrl"] ?? "https://pay.mrshoofer.ir";
      var sharedKey = configuration["PaymentServer:SharedKey"] ?? string.Empty;
      var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
      var sig = PaymentController.ComputeHmac($"{newticket.Id}:{timestamp}", sharedKey);
      var paymentStartUrl = $"{paymentServerBase}/Payment/Start?ticketId={newticket.Id}&t={timestamp}&sig={Uri.EscapeDataString(sig)}";

      _logger.LogInformation("Redirecting user to payment server. TicketId: {TicketId}, Url: {Url}", newticket.Id, paymentStartUrl);

      return Redirect(paymentStartUrl);
    }

    [HttpPost]
    public async Task<IActionResult> ValidateDiscount([FromBody] ValidateDiscountRequest req)
    {
      if (string.IsNullOrWhiteSpace(req?.Code))
        return Json(new { valid = false, message = "کد تخفیف وارد نشده است." });

      var code = await context.DiscountCodes
          .FirstOrDefaultAsync(d => d.Code == req.Code.Trim().ToUpper() && d.IsActive);

      if (code == null)
        return Json(new { valid = false, message = "کد تخفیف معتبر نیست." });

      if (code.ExpiryDate.HasValue && code.ExpiryDate < DateTime.Now)
        return Json(new { valid = false, message = "کد تخفیف منقضی شده است." });

      if (code.MaxUses.HasValue && code.UsedCount >= code.MaxUses)
        return Json(new { valid = false, message = "کد تخفیف به حداکثر استفاده رسیده است." });

      // Per-user one-time check (skipped if AllowMultipleUsePerUser is enabled)
      if (!code.AllowMultipleUsePerUser && !string.IsNullOrWhiteSpace(req.UserPhone))
      {
        var alreadyUsed = await context.DiscountCodeUsages
            .AnyAsync(u => u.DiscountCodeId == code.Id && u.UserPhone == req.UserPhone);
        if (alreadyUsed)
          return Json(new { valid = false, message = "این کد تخفیف قبلاً توسط شما استفاده شده است." });
      }

      var discountedPrice = LoyaltyService.ApplyStackedDiscounts(
        (int)req.OriginalPrice,
        code.DiscountPercent,
        (await _loyaltySvc.GetInfoAsync(req.UserPhone)).DiscountPercent);

      return Json(new
      {
        valid = true,
        discountPercent = code.DiscountPercent,
        discountedPrice,
        message = "کد تخفیف اعمال شد."
      });
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
      ViewBag.WebappToken = ticket.WebappToken;
      ViewBag.WebappBase = configuration["Webapp:BaseUrl"] ?? "https://webapp.mrshoofer.ir";

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

      // 1) If a specific default agency username is configured, prefer it (e.g. Test1)
      var defaultAgencyUsername = configuration["MrShoofer:DefaultAgencyUsername"];
      if (!string.IsNullOrWhiteSpace(defaultAgencyUsername))
      {
        try
        {
          var defaultIdentity = _userManager.FindByNameAsync(defaultAgencyUsername).Result;
          if (defaultIdentity != null)
          {
            var agencyByUser = this.context.Agencies.FirstOrDefault(a => a.IdentityUser != null && a.IdentityUser.Id == defaultIdentity.Id);
            if (agencyByUser != null && !string.IsNullOrWhiteSpace(agencyByUser.ORSAPI_token))
            {
              agencyToUse = agencyByUser;
              tokenToUse = agencyByUser.ORSAPI_token;
            }
          }
        }
        catch
        {
          // ignore lookup errors and continue to fallbacks
        }
      }

      // Use agency token if authenticated, otherwise use guest agency
      if (agencyToUse == null && User.Identity.IsAuthenticated)
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
        // Prefer an explicit default seller if configured
        var defaultSeller = this.context.Agencies.FirstOrDefault(a => a.IsDefaultSeller && !string.IsNullOrWhiteSpace(a.ORSAPI_token));
        if (defaultSeller != null)
        {
          agencyToUse = defaultSeller;
          tokenToUse = defaultSeller.ORSAPI_token;
        }
        else
        {
          // Get the default OTA seller agency
          agencyToUse = this.context.Agencies
            .FirstOrDefault(a => a.IdentityUser != null && a.IdentityUser.UserName == "Sale.mrshoofer");

          if (agencyToUse != null && !string.IsNullOrWhiteSpace(agencyToUse.ORSAPI_token))
          {
            tokenToUse = agencyToUse.ORSAPI_token;
          }
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
      var sellerToken = configuration["MrShoofer:SellerToken"];
      if (string.IsNullOrWhiteSpace(sellerToken)) return;

      var saleAgency = await context.Agencies
        .FirstOrDefaultAsync(a => a.IdentityUser != null && a.IdentityUser.UserName == "Sale.mrshoofer");

      if (saleAgency == null)
      {
        _logger.LogWarning("Sale.mrshoofer agency not found. Guest bookings will have no default seller agency.");
        return;
      }

      if (saleAgency.ORSAPI_token != sellerToken)
      {
        saleAgency.ORSAPI_token = sellerToken;
        await context.SaveChangesAsync();
      }
    }

    private static string NormalizePersianDigits(string? value)
    {
      if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
      var chars = value.ToCharArray();
      for (int i = 0; i < chars.Length; i++)
      {
        if (chars[i] >= '۰' && chars[i] <= '۹') chars[i] = (char)(chars[i] - '۰' + '0');
        else if (chars[i] >= '٠' && chars[i] <= '٩') chars[i] = (char)(chars[i] - '٠' + '0');
      }
      return new string(chars);
    }
  }

  public class ValidateDiscountRequest
  {
    public string? Code { get; set; }
    public long OriginalPrice { get; set; }
    public string? UserPhone { get; set; }
  }
}
