using Application.Services;
using Application.Services.Seo;
using Microsoft.AspNetCore.Mvc;

namespace Application.Areas.AgencyArea;

[Area("AgencyArea")]
public class RoutesController : Controller
{
  private static readonly string ContentStamp = "2026-08-05";

  [AcceptVerbs("GET", "HEAD")]
  public IActionResult Index()
  {
    ViewData["Title"] = "مسیرهای سواری بین‌شهری";
    ViewData["MetaDescription"] =
      "فهرست مسیرهای پرتردد سواری بین‌شهری مسترشوفر — رزرو آنلاین از تهران و شهرهای ایران با رانندگان تأییدشده. شهر مبدأ را انتخاب کنید یا مسیر دقیق را باز کنید.";
    ViewData["OgTitle"] = SeoDefaults.BuildOgTitle("مسیرهای سواری بین‌شهری");
    ViewData["OgDescription"] = (string)ViewData["MetaDescription"]!;
    ViewData["OgImage"] = SeoDefaults.DefaultOgImageUrl;
    ViewData["OgImageAlt"] = "مسیرهای سواری بین‌شهری مسترشوفر";
    ViewData["CanonicalUrl"] = SeoDefaults.BuildCanonical("/routes");
    ViewData["Breadcrumbs"] = new List<(string Name, string Url)>
    {
      ("صفحه اصلی", SeoDefaults.PreferredOrigin + "/"),
      ("مسیرها", SeoDefaults.BuildCanonical("/routes"))
    };
    ViewData["HowToSteps"] = SeoDefaults.BookingHowToSteps;
    ViewData["ItemList"] = RouteCatalog.All
      .Take(48)
      .Select(r => ($"سواری {r.OriginFa} به {r.DestinationFa}", SeoDefaults.BuildCanonical($"/routes/{r.Slug}")))
      .ToList();
    ViewData["DateModified"] = ContentStamp;
    ViewBag.Cities = CityCatalog.All;

    return View(RouteCatalog.All);
  }

  /// <summary>
  /// Programmatic landing URL for Google: real search results UX (today) + SEO copy at bottom.
  /// Renders TaxiTrips/Index so the experience matches /TaxiTrips results.
  /// </summary>
  [AcceptVerbs("GET", "HEAD")]
  public IActionResult Detail(string slug)
  {
    var route = RouteCatalog.FindBySlug(slug);
    if (route is null) return NotFound();

    var content = RouteContent.For(route);
    var title = RouteCatalog.Title(route);
    var todayPd = DateTime.Now.ToPersianDate();
    // Zero-padded for flatpickr-jdate (Y/m/d)
    var today = $"{todayPd.Year}/{todayPd.Month:D2}/{todayPd.Day:D2}";


    ViewData["Title"] = title;
    ViewData["MetaDescription"] = content.MetaDescription;
    ViewData["OgTitle"] = SeoDefaults.BuildOgTitle(title);
    ViewData["OgDescription"] = content.MetaDescription;
    ViewData["OgImage"] = SeoDefaults.DefaultOgImageUrl;
    ViewData["OgImageAlt"] = SeoDefaults.BuildRouteOgAlt(route.OriginFa, route.DestinationFa);
    ViewData["CanonicalUrl"] = SeoDefaults.BuildCanonical($"/routes/{route.Slug}");
    ViewData["Breadcrumbs"] = new List<(string Name, string Url)>
    {
      ("صفحه اصلی", SeoDefaults.PreferredOrigin + "/"),
      ("مسیرها", SeoDefaults.BuildCanonical("/routes")),
      (title, SeoDefaults.BuildCanonical($"/routes/{route.Slug}"))
    };
    ViewData["RouteServiceName"] = title;
    ViewData["RouteFaqs"] = content.Faqs;
    ViewData["HowToSteps"] = content.HowToSteps;
    ViewData["OriginCity"] = route.OriginFa;
    ViewData["DestinationCity"] = route.DestinationFa;
    ViewData["DateModified"] = ContentStamp;
    ViewData["ItemList"] = RouteCatalog.Related(route)
      .Select(r => ($"سواری {r.OriginFa} به {r.DestinationFa}", SeoDefaults.BuildCanonical($"/routes/{r.Slug}")))
      .ToList();
    // Indexable landing (override any noindex from generic search pages)
    ViewData["Robots"] = "index, follow, max-image-preview:large, max-snippet:-1, max-video-preview:-1";

    // Same ViewBag contract as TaxiTripsController.Index so the results UI auto-searches.
    ViewBag.origin_city_text = route.OriginFa;
    ViewBag.dest_city_text = route.DestinationFa;
    ViewBag.searchdate = today;
    ViewBag.selecteddate = DateTime.Now.Date;
    ViewBag.searchpdate = DateTime.Now.ToPersianDate();

    ViewBag.RouteSeo = content;
    ViewBag.RoutePage = route;
    ViewBag.RelatedRoutes = RouteCatalog.Related(route);
    ViewBag.ReverseRoute = RouteCatalog.ReverseOf(route);
    ViewBag.IsSeoRouteLanding = true;

    // Render TaxiTrips UI under this clean URL. Point view search at TaxiTrips
    // so Partials like TimelinePartial resolve correctly.
    RouteData.Values["controller"] = "TaxiTrips";
    return View("~/Areas/AgencyArea/Views/TaxiTrips/Index.cshtml");
  }
}
