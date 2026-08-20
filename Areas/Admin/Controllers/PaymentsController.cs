using Application.Data;
using Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Application.Areas.Admin.Controllers
{

  [Area("Admin")]
  [Authorize(Policy = "Admin")]
  public class PaymentsController : Controller
  {

    private readonly AppDbContext _context;

    public PaymentsController(AppDbContext dbContext)
    {
      this._context = dbContext;
    }


    public IActionResult Index()
    {
      return View();
    }


    public IActionResult ChargeRequests()
    {
      return View();
    }


    public IActionResult ChargeRequestsJson(int page = 0, int size = 20)
    {
      try
      {
        var requests = _context.ChargePaymentRequests
                                      .OrderByDescending(c => c.RequestedOn)
                                     .Skip(page * size)
                                     .Take(size)
                                     .Include(r => r.Agency)
                                     .AsNoTracking()
                                     
                                     .Select(r => new
                                     {
                                       RequestedOn = r.RequestedOn.ToPersianDate().ToShortDateString() + " " + r.RequestedOn.ToString("HH:mm"),
                                       PaymentMethod = r.PaymentMethod,
                                       Amout = r.Amout,
                                       Message = r.Message,
                                       Agency = new { Name = r.Agency.Name, Id = r.Agency.Id }
                                     })
                                     .ToList();
        var totalRecords =  _context.ChargePaymentRequests.Count();
        return Json(new { recordsTotal = totalRecords, recordsFiltered = totalRecords, data = requests });
      }
      catch (Exception e)
      {
        return BadRequest();
      }
    }
  }
}
