using Application.Data;
using Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Data.Entity;
using Application.Models;

namespace Application.Areas.AgencyArea
{
  [Area("AgencyArea")]
  [Authorize]
  public class TicketInfoController : Controller
  {
    private readonly AppDbContext context;
    private readonly UserManager<IdentityUser> userManager;
    public TicketInfoController(AppDbContext context, UserManager<IdentityUser> userManager)
    {
      this.context = context;
      this.userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {

      var agency = context.Agencies
        .Where(a => a.IdentityUser.UserName == User.Identity.Name)
        .Include(a => a.SoldTickets)
        .First();

      await context.Entry(agency)
        .Collection(a => a.SoldTickets)
        .LoadAsync();

      var tickets = agency.SoldTickets;



      ViewBag.tickets = tickets.OrderByDescending(t => t.RegisteredAt).ToList();


      return View();
    }


    [HttpGet]
    public async Task<IActionResult> Filter(string datesFilter)
    {
      // Date filters
      string[] date_strings = datesFilter.Replace(" ", "").Split('-');

      DateTime startDate = new PersianDate(date_strings[0]).ToDateTime();
      startDate = new DateTime(startDate.Year, startDate.Month, startDate.Day, 0, 0, 0);

      DateTime endDate = new PersianDate(date_strings[1]).ToDateTime();
      endDate = new DateTime(endDate.Year, endDate.Month, endDate.Day, 23, 59, 59);



      var agency = context.Agencies
        .Where(a => a.IdentityUser.UserName == User.Identity.Name)
        .Include(a => a.SoldTickets)
        .First();

      await context.Entry(agency)
        .Collection(a => a.SoldTickets)
        .LoadAsync();

      var tickets = agency.SoldTickets.AsQueryable();


      tickets = FilterTickets_by_date(tickets, startDate, endDate);


      ViewBag.dateFilter = datesFilter;


      ViewBag.tickets = tickets.ToList();
      ViewBag.tickets = tickets.OrderByDescending(t => t.RegisteredAt).ToList();


      return View("Index");
    }



    private IQueryable<Ticket> FilterTickets_by_date(IQueryable<Ticket> tickets, DateTime startDate, DateTime endDate)
    {
      return tickets.Where(t => t.RegisteredAt >= startDate && t.RegisteredAt <= endDate);
    }
  }
}
