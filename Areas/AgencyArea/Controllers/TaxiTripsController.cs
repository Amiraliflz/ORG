using Application.Data;
using Application.Migrations;
using Application.Services;
using Application.Services.Homepage;
using Application.Services.MrShooferORS;
using Application.Services.Seo;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Globalization;
using Application.Models;
using Microsoft.AspNetCore.Authorization;
using Application.ViewModels.TaxiTrips;
using Application.Services.TravelTime;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Application.Areas.AgencyArea
{
  [Area("AgencyArea")]
  // Removed [Authorize] - Allow guest access to search trips
  public class TaxiTripsController : Controller
  {
    private readonly DirectionsRepository directionsRepository;
    private readonly MrShooferAPIClient _mrShooferAPIClient;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly AppDbContext context;
    private readonly DirectionsTravelTimeCalculator _travelTimeCalculator;
    private readonly ITravelTimeSyncService _travelTimeSync;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly IHomepageCatalogCache _homepageCatalogCache;

    private Agency agency;

    // Clean single constructor
    public TaxiTripsController(
      DirectionsRepository directionsRepository,
      MrShooferAPIClient mrShooferAPIClient,
      UserManager<IdentityUser> userManager,
      AppDbContext context,
      DirectionsTravelTimeCalculator calculator,
      ITravelTimeSyncService travelTimeSync,
      IConfiguration configuration,
      IWebHostEnvironment env,
      IHomepageCatalogCache homepageCatalogCache)
    {
      this.context = context;
      _userManager = userManager;
      _mrShooferAPIClient = mrShooferAPIClient;
      this.directionsRepository = directionsRepository;
      this._travelTimeCalculator = calculator;
      _travelTimeSync = travelTimeSync;
      _configuration = configuration;
      _env = env;
      _homepageCatalogCache = homepageCatalogCache;
    }

    private static string NormalizeCity(string? s)
    {
      if (string.IsNullOrWhiteSpace(s)) return string.Empty;
      var str = s.Trim();
      var idx = str.IndexOf('(');
      if (idx >= 0) str = str[..idx];
      str = Regex.Replace(str, "[\u200C\u200F\u200E\u0610-\u061A\u064B-\u065F\u0670\u06D6-\u06ED]", string.Empty);
      str = str.Replace('\u064A', '\u06CC').Replace('\u0643', '\u06A9');
      str = str.Replace('\u0629', '\u0647');
      str = Regex.Replace(str, "\u0020+", " ").ToLowerInvariant();
      return str;
    }

    [AllowAnonymous]
    public async Task<IActionResult> Index(string originstring, string destinationstring, string searchdate)
    {
      if (string.IsNullOrWhiteSpace(originstring) || string.IsNullOrWhiteSpace(destinationstring))
      {
        // Bare /TaxiTrips is not a landing page — avoid soft/500 empty results for crawlers.
        return RedirectPermanent("/");
      }

      var originKey = NormalizeCity(originstring);
      var destKey = NormalizeCity(destinationstring);

      int origin_id = 0, destination_id = 0;

      // Resolve and validate the ordered pair from the server-maintained snapshot.
      // A user search never refetches the full direction catalog from ORS.
      await _homepageCatalogCache.EnsureDirectionsAsync(HttpContext.RequestAborted);
      var direction = _homepageCatalogCache.GetAvailableDirections().FirstOrDefault(d =>
        NormalizeCity(d.Cityone) == originKey &&
        NormalizeCity(d.Citytwo) == destKey);

      if (direction is not null)
      {
        origin_id = direction.CityoneId ?? 0;
        destination_id = direction.CitytwoId ?? 0;
      }

      if (origin_id == 0 || destination_id == 0)
      {
        if (direction is null)
          ModelState.AddModelError(nameof(destinationstring), $"مسیر {originstring} به {destinationstring} در حال حاضر فعال نیست");
        else
          ModelState.AddModelError(string.Empty, "شناسه شهرهای این مسیر کامل نیست");
        ViewBag.origin_city_text = originstring;
        ViewBag.dest_city_text = destinationstring;
        ViewBag.searchdate = searchdate;
        try
        {
          var fallbackPd = string.IsNullOrWhiteSpace(searchdate)
            ? DateTime.Now.ToPersianDate()
            : new PersianDate(searchdate.Replace('-', '/'));
          ViewBag.searchpdate = fallbackPd;
          ViewBag.selecteddate = fallbackPd.ToDateTime();
          ViewBag.ShowNorthPriceNotice = NorthRoutePriceNotice.ShouldShow(originstring, destinationstring, fallbackPd.ToDateTime());
        }
        catch
        {
          var nowPd = DateTime.Now.ToPersianDate();
          ViewBag.searchpdate = nowPd;
          ViewBag.selecteddate = DateTime.Now.Date;
          ViewBag.ShowNorthPriceNotice = NorthRoutePriceNotice.ShouldShow(originstring, destinationstring, DateTime.Now.Date);
        }
        AttachRouteSeoIfCatalogMatch(originstring, destinationstring);
        return View(new List<SearchedTrip>());
      }

      PersianDate pd = new PersianDate(searchdate?.Replace('-', '/') ?? string.Empty);
      DateTime searchedDatetime = pd.ToDateTime();

      ViewBag.origin_city_text = originstring;
      ViewBag.dest_city_text = destinationstring;
      ViewBag.searchdate = searchdate;
      ViewBag.selecteddate = searchedDatetime;
      ViewBag.searchpdate = pd;
      ViewBag.ShowNorthPriceNotice = NorthRoutePriceNotice.ShouldShow(originstring, destinationstring, searchedDatetime);

      // Same SEO bottom + sticky bridge as /routes/{slug} when OD is in the catalog.
      // Keep IsSeoRouteLanding=false so querystring URLs stay noindex (canonical → /routes/...).
      AttachRouteSeoIfCatalogMatch(originstring, destinationstring);

      return View(new List<SearchedTrip>());
    }

    private void AttachRouteSeoIfCatalogMatch(string originstring, string destinationstring)
    {
      var route = RouteCatalog.FindByCities(originstring, destinationstring);
      if (route is null) return;

      ViewBag.RoutePage = route;
      ViewBag.RouteSeo = RouteContent.For(route);
      ViewBag.RelatedRoutes = RouteCatalog.Related(route);
      ViewBag.ReverseRoute = RouteCatalog.ReverseOf(route);
      ViewBag.IsSeoRouteLanding = false;
    }

    /// <summary>HTML fragment for #route-seo — used when the client changes OD via AJAX.</summary>
    [HttpGet]
    [AllowAnonymous]
    [Route("/TaxiTrips/RouteSeoPartial")]
    public IActionResult RouteSeoPartial(string originstring, string destinationstring)
    {
      var route = RouteCatalog.FindByCities(originstring, destinationstring);
      if (route is null) return NotFound();

      ViewBag.RoutePage = route;
      ViewBag.RouteSeo = RouteContent.For(route);
      ViewBag.RelatedRoutes = RouteCatalog.Related(route);
      ViewBag.ReverseRoute = RouteCatalog.ReverseOf(route);
      ViewBag.IsSeoRouteLanding = false;
      ViewBag.origin_city_text = route.OriginFa;
      ViewBag.dest_city_text = route.DestinationFa;
      return PartialView("~/Areas/AgencyArea/Views/TaxiTrips/_RouteSeoBottom.cshtml");
    }

    [HttpGet]
    [AllowAnonymous]
    [Route("/TaxiTrips/AvailableDirections")]
    [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, VaryByHeader = "Accept-Encoding")]
    public async Task<IActionResult> AvailableDirection(CancellationToken cancellationToken)
    {
      try
      {
        await _homepageCatalogCache.EnsureDirectionsAsync(cancellationToken);
        var cached = _homepageCatalogCache.GetAvailableDirections();
        if (cached.Count > 0)
          return Json(cached);

        // Rare race: warm failed; fall through to live ORS once.
        var dirs = await _mrShooferAPIClient.GetAvaiableOTADirectionsAsync();
        return Json(dirs);
      }
      catch (Exception ex)
      {
        try
        {
          var path = Path.Combine(_env.WebRootPath, "json", "Directions", "Directions.json");
          if (System.IO.File.Exists(path))
          {
            var json = System.IO.File.ReadAllText(path);
            var doc = JsonDocument.Parse(json);
            var list = new List<object>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
              var c1 = el.GetProperty("Cityone").GetString();
              var c2 = el.GetProperty("Citytwo").GetString();
              if (!string.IsNullOrWhiteSpace(c1) && !string.IsNullOrWhiteSpace(c2))
                list.Add(new { Cityone = c1, Citytwo = c2 });
            }
            return Json(list);
          }
        }
        catch { }
        return StatusCode(500, new { error = "Failed to load directions", detail = ex.Message });
      }
    }

    [HttpGet]
    [AllowAnonymous]
    [Route("/TaxiTrips/SupportedCities")]
    public async Task<IActionResult> SupportedCities(CancellationToken cancellationToken)
    {
      await _homepageCatalogCache.EnsureFreshAsync(cancellationToken);
      return Json(_homepageCatalogCache.GetSupportedCities());
    }

    [HttpGet]
    [AllowAnonymous]
    [Route("/TaxiTrips/SearchHints")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> SearchHints(CancellationToken cancellationToken)
    {
      await _homepageCatalogCache.EnsureFreshAsync(cancellationToken);
      return Json(new
      {
        supportedCities = _homepageCatalogCache.GetSupportedCities(),
        popularOrigins = _homepageCatalogCache.GetPopularOrigins(),
        version = _homepageCatalogCache.GetVersionToken()
      });
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
      base.OnActionExecuting(context);
      string tokenToUse = null;
      
      // Use agency token if authenticated, otherwise use guest/default token
      if (User?.Identity?.IsAuthenticated == true)
      {
        var identityUser = _userManager.GetUserAsync(User).Result;
        agency = this.context.Agencies.FirstOrDefault(a => a.IdentityUser == identityUser);
        if (agency != null && !string.IsNullOrWhiteSpace(agency.ORSAPI_token)) 
          tokenToUse = agency.ORSAPI_token;
      }
      
      // Fallback to guest token from configuration
      if (string.IsNullOrWhiteSpace(tokenToUse)) 
        tokenToUse = _configuration["MrShoofer:SellerToken"];
        
      if (!string.IsNullOrWhiteSpace(tokenToUse)) 
        _mrShooferAPIClient.SetSellerApiKey(tokenToUse);
    }

    [Route("/TaxiTrips/SearchJson")]
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> SearchTripsJson(string originstring, string destinationstring, string searchdate)
    {
      if (string.IsNullOrWhiteSpace(originstring) || string.IsNullOrWhiteSpace(destinationstring))
        return BadRequest(new { error = "originstring and destinationstring are required." });

      var originKey = NormalizeCity(originstring);
      var destKey = NormalizeCity(destinationstring);

      await _homepageCatalogCache.EnsureDirectionsAsync(HttpContext.RequestAborted);
      var direction = _homepageCatalogCache.GetAvailableDirections().FirstOrDefault(d =>
        NormalizeCity(d.Cityone) == originKey &&
        NormalizeCity(d.Citytwo) == destKey);

      if (direction is null)
      {
        var validDestinations = _homepageCatalogCache.GetAvailableDirections()
          .Where(d => NormalizeCity(d.Cityone) == originKey)
          .Select(d => d.Citytwo)
          .Where(city => !string.IsNullOrWhiteSpace(city))
          .Distinct()
          .Take(5);
        return BadRequest(new
        {
          error = $"مسیر {originstring} به {destinationstring} در حال حاضر فعال نیست",
          suggestions = validDestinations
        });
      }

      var origin_id = direction.CityoneId ?? 0;
      var destination_id = direction.CitytwoId ?? 0;
      if (origin_id == 0 || destination_id == 0)
      {
        return StatusCode(503, new { error = "شناسه شهرهای این مسیر کامل نیست" });
      }

      PersianDate pd;
      try { pd = new PersianDate(searchdate?.Replace('-', '/') ?? string.Empty); }
      catch { return BadRequest(new { error = "تاریخ نامعتبر" }); }
      DateTime searchedDatetime = pd.ToDateTime();

      List<SearchedTrip> response;
      try
      {
        response = (await _mrShooferAPIClient.SearchTrips(searchedDatetime, searchedDatetime.AddDays(1), origin_id, destination_id))?.ToList() ?? new List<SearchedTrip>();
      }
      catch (Exception ex)
      {
        // Prefer a clear error over a silent empty list (CookieContainer / network / API).
        Console.Error.WriteLine($"[SearchJson] {ex}");
        Response.StatusCode = 502;
        return Json(new {
          error = "خطا در دریافت سفرها از سرویس",
          detail = ex.Message,
          inner = ex.InnerException?.Message
        });
      }

      int traveltime_mins = _travelTimeCalculator.GetTravelMins(
        origin_id > 0 ? origin_id : null,
        destination_id > 0 ? destination_id : null,
        originstring,
        destinationstring);

      if (traveltime_mins <= 0)
      {
        try
        {
          using var etaCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
          etaCts.CancelAfter(TimeSpan.FromSeconds(8));
          traveltime_mins = await _travelTimeSync.EnsureRouteTravelTimeAsync(
            origin_id, destination_id, originstring, destinationstring, etaCts.Token);
        }
        catch (OperationCanceledException)
        {
          // Search still returns; ETA fills on next search after Neshan responds.
        }
        catch
        {
          // Keep 0 — duration/arrival stay hidden rather than blocking results.
        }
      }

      var end_result = response
        .OrderBy(t => t.startingDateTime)
        .ThenBy(t => t.afterdiscticketprice)
        .Where(t => t.startingDateTime > DateTime.Now.AddMinutes(45))
        .ToList();

      string FormatTravelDuration(int mins)
      {
        if (mins <= 0) return string.Empty;
        if (mins < 60) return $"{mins} دقیقه";
        var h = mins / 60;
        var m = mins % 60;
        return m > 0 ? $"{h} ساعت و {m} دقیقه" : $"{h} ساعت";
      }

      var travelDuration = FormatTravelDuration(traveltime_mins);

      var searchedTripViewModels = end_result.Select(t =>
      {
        var arrival = t.startingDateTime.AddMinutes(traveltime_mins);
        var image = t.Image?.Trim();
        if (!string.IsNullOrWhiteSpace(image) && image.StartsWith('/'))
          image = "https://ors.shoofer.taxi" + image;

        return new SearchedTripViewModel
        {
          startingDateTime = t.startingDateTime.ToString("HH:mm"),
          arrivalDateTime = arrival.ToString("HH:mm"),
          arrivesNextDay = arrival.Date > t.startingDateTime.Date,
          origin = $"{t.originCityName}({t.oringinLocationName})",
          destination = $"{t.destinationCityName}({t.destinationLocationName})",
          originalPrice = t.originalTicketprice.ToString("N0"),
          afterdiscount = t.afterdiscticketprice.ToString("N0"),
          taxiSupervisorName = t.taxiSupervisorName,
          taxiSupervisorID = t.taxiSupervisorID,
          tripcode = t.tripPlanCode,
          carModelName = t.carModelName,
          Image = image,
          travelDuration = travelDuration
        };
      }).ToList();

      return Json(searchedTripViewModels);
    }
  }
}
