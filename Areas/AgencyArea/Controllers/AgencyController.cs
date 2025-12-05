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
    private Agency? agency;

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
      if (agency == null)
      {
        return Forbid();
      }

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
      if (agency == null)
      {
        return Json(new { error = "forbidden" });
      }

      AgencyAnalyzerService analyzer = new AgencyAnalyzerService(agency);
      _context.Entry(agency)
        .Collection(a => a.SoldTickets)
        .Load();

      var valuesdictionary = analyzer.GetLast7Days_SaleChartNumbers();
      var newdictionary = valuesdictionary.ToDictionary(
        kv => kv.Key.ToPersianDate().Month + "/" + kv.Key.ToPersianDate().Day,
        kv => kv.Value);

      var last = newdictionary.Last();
      var oldkey = last.Key;
      var value = last.Value;

      newdictionary.Remove(oldkey);
      newdictionary.Add("امروز", value);

      return Json(newdictionary);
    }

    // For setting api key and getting agency entity related to current request from database
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
      var identityUser = await _userManager.GetUserAsync(User);
      if (identityUser == null)
      {
        context.Result = Forbid();
        return;
      }

      agency = await _context.Agencies.FirstOrDefaultAsync(a => a.IdentityUser == identityUser);
      if (agency == null)
      {
        context.Result = Forbid();
        return;
      }

      _apiClient.SetSellerApiKey(agency.ORSAPI_token);
      await next();
    }
  }
}
