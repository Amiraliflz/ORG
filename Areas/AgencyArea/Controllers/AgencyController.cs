using Application.Data;
using Application.Services;
using Application.Services.MrShooferORS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Application.ViewModels;
using Application.Models;
namespace Application.Areas.AgencyArea
{
  [Area("AgencyArea")]
  [Authorize]
  public class AgencyController : Controller
  {
    private readonly UserManager<IdentityUser> _userManager;
    private readonly MrShooferAPIClient _apiClient;
    private readonly AppDbContext _context;
    private Agency agency;

    public AgencyController(AppDbContext context, UserManager<IdentityUser> userManager, MrShooferAPIClient apiClient)
    {
      _context = context;
      _userManager = userManager;
      _apiClient = apiClient;
    }

    // Main agency page, general info last tickets
    [HttpGet]
    public async Task<IActionResult> Index()
    {
      ViewBag.agency = agency;

      // loading and fetching TODAY sold group
      await _context.Entry(agency)
         .Collection(a => a.SoldTickets)
         .LoadAsync();

      AgencyAnalyzerService analyzer = new AgencyAnalyzerService(agency);

      ViewBag.totalsold = analyzer.GetTotalSold();
      ViewBag.todaysold = analyzer.GetTodaySold();
      ViewBag.thismonthsold = analyzer.GetThisMonthSold();
      ViewBag._7dayssold = analyzer.GetLast7DaysSold();
      ViewBag.thismonthtotalprice = analyzer.GetThisMonthSoldTotalPrice();
      ViewBag.thismonthtotalprofit = analyzer.GetThisMonthTotalProfit();
      ViewBag.todaytotalprofit = analyzer.GetTodayTotalPrifit();


      ViewBag.Last7weekprofit = analyzer.GetLast7DaysProfit();


      ViewBag.agancy_balance = (long)Convert.ToDecimal(await _apiClient.GetAccountBalance());

      ViewBag.today_soldTickets = agency.SoldTickets
        .Where(t => t.RegisteredAt >= DateTime.Today && t.RegisteredAt < DateTime.Today.AddDays(1))
        .ToList();


      return View();
    }


    [HttpGet]
    public JsonResult GetSalesChartValues()
    {
      AgencyAnalyzerService analyzer = new AgencyAnalyzerService(agency);
      _context.Entry(agency)
        .Collection(a => a.SoldTickets)
        .Load();

      var valuesdictionary = analyzer.GetLast7Days_SaleChartNumbers();
      // Your data array
      var newdictionary = valuesdictionary.ToDictionary(
        kv => kv.Key.ToPersianDate().Month + "/" + kv.Key.ToPersianDate().Day,
        kv => kv.Value);




      var last = newdictionary.Last();
      var oldkey = last.Key;
      var value = last.Value;

      newdictionary.Remove(oldkey);
      newdictionary.Add("امروز", value);
      //int[] values = newdictionary.Select(kv => kv.Value).ToArray();
      //string[] daynames = newdictionary.Select(kv => kv.Value.ToString()).ToArray();

      // Return the array as a JSON object
      return Json(newdictionary);
    }


    // For setting api key and getting agency entity related to current request from database
    public override void OnActionExecuting(ActionExecutingContext context)
    {
      base.OnActionExecuting(context);
        
      var identityUser = _userManager.GetUserAsync(User).Result;
      agency = _context.Agencies.FirstOrDefault(a => a.IdentityUser == identityUser);


      _apiClient.SetSellerApiKey(agency.ORSAPI_token);
    }
  }
}
