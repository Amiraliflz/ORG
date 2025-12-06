using Application.Data;
using Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
      var username = User?.Identity != null ? User.Identity.Name : null;

      var agency = await context.Agencies
        .Include(a => a.IdentityUser)
        .Include(a => a.SoldTickets)
        .FirstOrDefaultAsync(a => a.IdentityUser.UserName == username);

      if (agency == null)
      {
        // No agency found for the current user; show empty list gracefully
        ViewBag.tickets = new List<Ticket>();
        return View();
      }

      var tickets = agency.SoldTickets ?? new List<Ticket>();
      ViewBag.tickets = tickets.OrderByDescending(t => t.RegisteredAt).ToList();

      return View();
    }


    [HttpGet]
    public async Task<IActionResult> Filter(string datesFilter)
    {
      // Date filters
      string[] date_strings = (datesFilter ?? string.Empty).Replace(" ", "").Split('-');
      if (date_strings.Length < 2)
      {
        return RedirectToAction(nameof(Index));
      }

      DateTime startDate = new PersianDate(date_strings[0]).ToDateTime();
      startDate = new DateTime(startDate.Year, startDate.Month, startDate.Day, 0, 0, 0);

      DateTime endDate = new PersianDate(date_strings[1]).ToDateTime();
      endDate = new DateTime(endDate.Year, endDate.Month, endDate.Day, 23, 59, 59);

      var username = User?.Identity != null ? User.Identity.Name : null;

      var agency = await context.Agencies
        .Include(a => a.IdentityUser)
        .Include(a => a.SoldTickets)
        .FirstOrDefaultAsync(a => a.IdentityUser.UserName == username);

      if (agency == null)
      {
        ViewBag.tickets = new List<Ticket>();
        return View("Index");
      }

      var ticketsQuery = agency.SoldTickets.AsQueryable();
      ticketsQuery = FilterTickets_by_date(ticketsQuery, startDate, endDate);

      ViewBag.dateFilter = datesFilter;
      ViewBag.tickets = ticketsQuery.OrderByDescending(t => t.RegisteredAt).ToList();

      return View("Index");
    }



    private IQueryable<Ticket> FilterTickets_by_date(IQueryable<Ticket> tickets, DateTime startDate, DateTime endDate)
    {
      return tickets.Where(t => t.RegisteredAt >= startDate && t.RegisteredAt <= endDate);
    }
  }
}
