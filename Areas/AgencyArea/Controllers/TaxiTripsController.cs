using Application.Data;
using Application.Migrations;
using Application.Services;
using Application.Services.MrShooferORS;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Globalization;
using Application.Models;
using Microsoft.AspNetCore.Authorization;
using Application.ViewModels.TaxiTrips;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Application.Areas.AgencyArea
{
  [Area("AgencyArea")]
  [Authorize]
  public class TaxiTripsController : Controller
  {
    private readonly DirectionsRepository directionsRepository;
    private readonly MrShooferAPIClient _mrShooferAPIClient;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly AppDbContext context;
    private readonly DirectionsTravelTimeCalculator _travelTimeCalculator;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;

    private Agency agency;

    // Clean single constructor
    public TaxiTripsController(
      DirectionsRepository directionsRepository,
      MrShooferAPIClient mrShooferAPIClient,
      UserManager<IdentityUser> userManager,
      AppDbContext context,
      DirectionsTravelTimeCalculator calculator,
      IConfiguration configuration,
      IWebHostEnvironment env)
    {
      this.context = context;
      _userManager = userManager;
      _mrShooferAPIClient = mrShooferAPIClient;
      this.directionsRepository = directionsRepository;
      this._travelTimeCalculator = calculator;
      _configuration = configuration;
      _env = env;
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

    public async Task<IActionResult> Index(string originstring, string destinationstring, string searchdate)
    {
      if (string.IsNullOrWhiteSpace(originstring) || string.IsNullOrWhiteSpace(destinationstring))
      {
        ModelState.AddModelError(string.Empty, "لطفا شهر مبدا و مقصد را وارد کنید.");
        ViewBag.origin_city_text = originstring;
        ViewBag.dest_city_text = destinationstring;
        ViewBag.searchdate = searchdate;
        return View();
      }

      var allDirections = directionsRepository.GetDirections();
      var normMap = allDirections.ToDictionary(k => NormalizeCity(k.Key), v => v.Value);

      var originKey = NormalizeCity(originstring);
      var destKey = NormalizeCity(destinationstring);

      if (!normMap.TryGetValue(originKey, out var origin_id) || !normMap.TryGetValue(destKey, out var destination_id))
      {
        var dynamicMap = await _mrShooferAPIClient.GetCityNameIdMapAsync();
        if (!normMap.TryGetValue(originKey, out origin_id)) dynamicMap.TryGetValue(originKey, out origin_id);
        if (!normMap.TryGetValue(destKey, out destination_id)) dynamicMap.TryGetValue(destKey, out destination_id);

        if (origin_id == 0 || destination_id == 0)
        {
          if (origin_id == 0) ModelState.AddModelError(nameof(originstring), $"شهر مبدا نامعتبر است: {originstring}");
          if (destination_id == 0) ModelState.AddModelError(nameof(destinationstring), $"شهر مقصد نامعتبر است: {destinationstring}");
          ViewBag.origin_city_text = originstring;
          ViewBag.dest_city_text = destinationstring;
          ViewBag.searchdate = searchdate;
          return View();
        }
        normMap[originKey] = origin_id;
        normMap[destKey] = destination_id;
      }

      PersianDate pd = new PersianDate(searchdate?.Replace('-', '/') ?? string.Empty);
      DateTime searchedDatetime = pd.ToDateTime();

      ViewBag.origin_city_text = originstring;
      ViewBag.dest_city_text = destinationstring;
      ViewBag.searchdate = searchdate;
      ViewBag.selecteddate = searchedDatetime;
      ViewBag.searchpdate = pd;

      return View();
    }

    [HttpGet]
    [AllowAnonymous]
    [Route("/TaxiTrips/AvailableDirections")]
    public async Task<IActionResult> AvailableDirection()
    {
      try
      {
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
    public IActionResult SupportedCities() => Json(directionsRepository.GetDirections().Keys.ToList());

    public override void OnActionExecuting(ActionExecutingContext context)
    {
      base.OnActionExecuting(context);
      string tokenToUse = null;
      if (User?.Identity?.IsAuthenticated == true)
      {
        var identityUser = _userManager.GetUserAsync(User).Result;
        agency = this.context.Agencies.FirstOrDefault(a => a.IdentityUser == identityUser);
        if (agency != null && !string.IsNullOrWhiteSpace(agency.ORSAPI_token)) tokenToUse = agency.ORSAPI_token;
      }
      if (string.IsNullOrWhiteSpace(tokenToUse)) tokenToUse = _configuration["MrShoofer:SellerToken"];
      if (!string.IsNullOrWhiteSpace(tokenToUse)) _mrShooferAPIClient.SetSellerApiKey(tokenToUse);
    }

    [Route("/TaxiTrips/SearchJson")]
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> SearchTripsJson(string originstring, string destinationstring, string searchdate)
    {
      if (string.IsNullOrWhiteSpace(originstring) || string.IsNullOrWhiteSpace(destinationstring))
        return BadRequest(new { error = "originstring and destinationstring are required." });

      var allDirections = directionsRepository.GetDirections();
      var normMap = allDirections.ToDictionary(k => NormalizeCity(k.Key), v => v.Value);
      var originKey = NormalizeCity(originstring);
      var destKey = NormalizeCity(destinationstring);

      int origin_id = 0, destination_id = 0;
      if (!normMap.TryGetValue(originKey, out origin_id) || !normMap.TryGetValue(destKey, out destination_id))
      {
        try
        {
          var dynamicMap = await _mrShooferAPIClient.GetCityNameIdMapAsync();
          if (!normMap.TryGetValue(originKey, out origin_id)) dynamicMap.TryGetValue(originKey, out origin_id);
          if (!normMap.TryGetValue(destKey, out destination_id)) dynamicMap.TryGetValue(destKey, out destination_id);
        }
        catch { /* ignore SSL/map issues */ }
      }

      if (origin_id == 0)
      {
        var suggestions = allDirections.Keys.Where(k => NormalizeCity(k).Contains(originKey)).Take(5);
        return BadRequest(new { error = $"شهر مبدا نامعتبر است: {originstring}", suggestions });
      }
      if (destination_id == 0)
      {
        var suggestions = allDirections.Keys.Where(k => NormalizeCity(k).Contains(destKey)).Take(5);
        return BadRequest(new { error = $"شهر مقصد نامعتبر است: {destinationstring}", suggestions });
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
        // Graceful degradation: return empty array instead of 500
        return Json(new List<SearchedTripViewModel>()); // frontend shows "سفری پیدا نشد"
      }

      int traveltime_mins = _travelTimeCalculator.GetTravelMins(originstring, destinationstring);

      var end_result = response
        .OrderBy(t => t.startingDateTime)
        .ThenBy(t => t.afterdiscticketprice)
        .Where(t => t.startingDateTime > DateTime.Now.AddMinutes(45))
        .ToList();

      var searchedTripViewModels = end_result.Select(t => new SearchedTripViewModel
      {
        startingDateTime = t.startingDateTime.ToString("HH:mm"),
        arrivalDateTime = t.startingDateTime.AddMinutes(traveltime_mins).ToString("HH:mm"),
        origin = $"{t.originCityName}({t.oringinLocationName})",
        destination = $"{t.destinationCityName}({t.destinationLocationName})",
        originalPrice = t.originalTicketprice.ToString("N0"),
        afterdiscount = t.afterdiscticketprice.ToString("N0"),
        taxiSupervisorName = t.taxiSupervisorName,
        taxiSupervisorID = t.taxiSupervisorID,
        tripcode = t.tripPlanCode,
        carModelName = t.carModelName
      }).ToList();

      return Json(searchedTripViewModels);
    }
  }
}
