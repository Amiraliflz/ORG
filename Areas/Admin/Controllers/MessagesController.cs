using Application.Data;
using Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data.Entity;

namespace Application.Areas.Admin.Controllers
{
  [Area("Admin")]
  [Authorize(Policy = "Admin")]
  public class MessagesController : Controller
  {

    private readonly AppDbContext context;

    public MessagesController(AppDbContext context)
    {
      this.context = context;
    }

    public IActionResult Index()
    {
      return View();
    }


    public IActionResult GetContactUsMessagesJson()
    {
      return Json(new
      {
        data = context.ContactMessages.AsNoTracking().OrderByDescending(o => o.RegisteredDateTime).Select(m => new
        {
          Name = m.Name,
          Number = m.Number,
          Message = m.Message,
          RegisteredDateTime = m.RegisteredDateTime.ToString("HH:mm")  + " " + m.RegisteredDateTime.ToPersianDate().ToShortDateString()
        }).ToList()
      });
    }
  }
}
