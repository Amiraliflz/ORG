using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Application.Models;
using Microsoft.AspNetCore.Identity;
using Humanizer;
using Application.Data;
using System.Security.Claims;
using Application.Services.Homepage;
using Application.Services.Seo;

namespace Application.Areas.AgencyArea
{
  [Area("AgencyArea")]
  // Removed [Authorize] - Allow guest access to home page
  public class HomeController : Controller
  {
    private readonly ILogger<HomeController> _logger;

    private readonly UserManager<IdentityUser> userManager;

    private readonly AppDbContext context;

    private readonly SignInManager<IdentityUser> signInManager;


    public HomeController(ILogger<HomeController> logger, UserManager<IdentityUser> userManager, AppDbContext context, SignInManager<IdentityUser> signInManager)
    {
      this.context = context;
      _logger = logger;
      this.userManager = userManager;
      this.signInManager = signInManager;
    }


    public async Task<IActionResult> Index(
      [FromServices] IHomepageCatalogCache homepageCatalogCache,
      CancellationToken cancellationToken)
    {
      await homepageCatalogCache.EnsureFreshAsync(cancellationToken);
      return View();
    }
    
    public IActionResult Privacy()
    {
      return View();
    }

    public IActionResult FAQ()
    {
      return View();
    }

    public IActionResult ContactUs()
    {
      return View();
    }

    public IActionResult TravelPolicy()
    {
      return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
      ViewData["Robots"] = "noindex, nofollow";
      ViewData["CanonicalUrl"] = SeoDefaults.BuildCanonical("/");
      Response.StatusCode = StatusCodes.Status500InternalServerError;
      return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
  }
}
