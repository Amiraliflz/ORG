using Application.Services.Seo;
using Microsoft.AspNetCore.Mvc;

namespace Application.Areas.AgencyArea;

[Area("AgencyArea")]
public class CitiesController : Controller
{
  private static readonly string ContentStamp = "2026-08-05";

  [AcceptVerbs("GET", "HEAD")]
  public IActionResult Index()
  {
    ViewData["Title"] = "شهرهای تحت پوشش سواری بین‌شهری";
    ViewData["MetaDescription"] =
      "فهرست شهرهای مبدأ و مقصد مسترشوفر برای رزرو آنلاین سواری بین‌شهری — تهران، اصفهان، شمال، غرب و دیگر شهرهای ایران.";
    ViewData["OgTitle"] = SeoDefaults.BuildOgTitle("شهرهای تحت پوشش");
    ViewData["OgDescription"] = (string)ViewData["MetaDescription"]!;
    ViewData["OgImage"] = SeoDefaults.DefaultOgImageUrl;
    ViewData["OgImageAlt"] = "شهرهای تحت پوشش سواری بین‌شهری مسترشوفر";
    ViewData["CanonicalUrl"] = SeoDefaults.BuildCanonical("/cities");
    ViewData["Breadcrumbs"] = new List<(string Name, string Url)>
    {
      ("صفحه اصلی", SeoDefaults.PreferredOrigin + "/"),
      ("شهرها", SeoDefaults.BuildCanonical("/cities"))
    };
    ViewData["ItemList"] = CityCatalog.All
      .Select(c => ($"سواری از/به {c.NameFa}", SeoDefaults.BuildCanonical($"/cities/{c.Slug}")))
      .ToList();
    ViewData["DateModified"] = ContentStamp;
    return View(CityCatalog.All);
  }

  [AcceptVerbs("GET", "HEAD")]
  public IActionResult Detail(string slug)
  {
    var city = CityCatalog.FindBySlug(slug);
    if (city is null) return NotFound();

    var from = RouteCatalog.FromCity(city.NameFa);
    var to = RouteCatalog.ToCity(city.NameFa);

    ViewData["Title"] = $"سواری بین‌شهری {city.NameFa}";
    ViewData["MetaDescription"] =
      $"رزرو آنلاین سواری از و به {city.NameFa} با مسترشوفر — {city.RegionFa}. مسیرهای پرتردد، رانندگان تأییدشده و پشتیبانی ۲۴/۷.";
    ViewData["OgTitle"] = SeoDefaults.BuildOgTitle($"سواری بین‌شهری {city.NameFa}");
    ViewData["OgDescription"] = (string)ViewData["MetaDescription"]!;
    ViewData["OgImage"] = SeoDefaults.DefaultOgImageUrl;
    ViewData["OgImageAlt"] = SeoDefaults.BuildCityOgAlt(city.NameFa);
    ViewData["CanonicalUrl"] = SeoDefaults.BuildCanonical($"/cities/{city.Slug}");
    ViewData["Breadcrumbs"] = new List<(string Name, string Url)>
    {
      ("صفحه اصلی", SeoDefaults.PreferredOrigin + "/"),
      ("شهرها", SeoDefaults.BuildCanonical("/cities")),
      (city.NameFa, SeoDefaults.BuildCanonical($"/cities/{city.Slug}"))
    };
    ViewData["RouteServiceName"] = $"سواری بین‌شهری {city.NameFa}";
    ViewData["RouteFaqs"] = new List<(string, string)>
    {
      ($"آیا از {city.NameFa} می‌توانم سواری رزرو کنم؟",
        $"بله. مسیرهای فعال از و به {city.NameFa} در همین صفحه فهرست شده‌اند. مبدأ/مقصد را باز کنید یا از صفحه اصلی جستجو کنید."),
      ("قیمت چطور مشخص می‌شود؟",
        "پس از انتخاب مسیر و تاریخ، قیمت گزینه‌های همان روز در نتایج جستجو دیده می‌شود."),
      ("پشتیبانی دارید؟",
        "پشتیبانی ۲۴/۷ تا پایان سفر در دسترس است.")
    };
    ViewData["HowToSteps"] = SeoDefaults.BookingHowToSteps;
    ViewData["DateModified"] = ContentStamp;
    ViewData["ItemList"] = from.Concat(to).Take(30)
      .Select(r => ($"سواری {r.OriginFa} به {r.DestinationFa}", SeoDefaults.BuildCanonical($"/routes/{r.Slug}")))
      .Distinct()
      .ToList();

    ViewBag.FromRoutes = from;
    ViewBag.ToRoutes = to;
    return View(city);
  }
}
