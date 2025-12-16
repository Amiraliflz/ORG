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
        ViewBag.tickets = new List<Ticket>();
        ViewBag.totalTickets = 0;
        ViewBag.activeTickets = 0;
        ViewBag.cancelledTickets = 0;
        return View();
      }

      var tickets = agency.SoldTickets ?? new List<Ticket>();
      ViewBag.tickets = tickets.OrderByDescending(t => t.RegisteredAt).ToList();
      
      // Statistics
      ViewBag.totalTickets = tickets.Count;
      ViewBag.activeTickets = tickets.Count(t => !t.IsCancelled);
      ViewBag.cancelledTickets = tickets.Count(t => t.IsCancelled);

      return View();
    }


    [HttpGet]
    public async Task<IActionResult> Filter(string datesFilter, string statusFilter)
    {
      var username = User?.Identity != null ? User.Identity.Name : null;

      var agency = await context.Agencies
        .Include(a => a.IdentityUser)
        .Include(a => a.SoldTickets)
        .FirstOrDefaultAsync(a => a.IdentityUser.UserName == username);

      if (agency == null)
      {
        ViewBag.tickets = new List<Ticket>();
        ViewBag.totalTickets = 0;
        ViewBag.activeTickets = 0;
        ViewBag.cancelledTickets = 0;
        return View("Index");
      }

      var ticketsQuery = agency.SoldTickets.AsQueryable();

      // Apply date filter if provided
      if (!string.IsNullOrEmpty(datesFilter))
      {
        string[] date_strings = datesFilter.Replace(" ", "").Split('-');
        if (date_strings.Length >= 2)
        {
          DateTime startDate = new PersianDate(date_strings[0]).ToDateTime();
          startDate = new DateTime(startDate.Year, startDate.Month, startDate.Day, 0, 0, 0);

          DateTime endDate = new PersianDate(date_strings[1]).ToDateTime();
          endDate = new DateTime(endDate.Year, endDate.Month, endDate.Day, 23, 59, 59);

          ticketsQuery = FilterTickets_by_date(ticketsQuery, startDate, endDate);
          ViewBag.dateFilter = datesFilter;
        }
      }

      // Apply status filter if provided
      if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "all")
      {
        if (statusFilter == "active")
        {
          ticketsQuery = ticketsQuery.Where(t => !t.IsCancelled);
        }
        else if (statusFilter == "cancelled")
        {
          ticketsQuery = ticketsQuery.Where(t => t.IsCancelled);
        }
        ViewBag.statusFilter = statusFilter;
      }

      var filteredTickets = ticketsQuery.OrderByDescending(t => t.RegisteredAt).ToList();
      ViewBag.tickets = filteredTickets;
      
      // Statistics for all tickets
      var allTickets = agency.SoldTickets ?? new List<Ticket>();
      ViewBag.totalTickets = allTickets.Count;
      ViewBag.activeTickets = allTickets.Count(t => !t.IsCancelled);
      ViewBag.cancelledTickets = allTickets.Count(t => t.IsCancelled);

      return View("Index");
    }



    private IQueryable<Ticket> FilterTickets_by_date(IQueryable<Ticket> tickets, DateTime startDate, DateTime endDate)
    {
      return tickets.Where(t => t.RegisteredAt >= startDate && t.RegisteredAt <= endDate);
    }
  }
}
