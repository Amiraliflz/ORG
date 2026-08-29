using Application.Services.Seo;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Xml.Linq;

namespace Application.Controllers;

/// <summary>Public crawler endpoints (sitemap index + urlset). robots.txt / llms.txt are static under wwwroot.</summary>
[ApiExplorerSettings(IgnoreApi = true)]
public class SeoController : Controller
{
  private static readonly string ContentStamp = DateTime.UtcNow.ToString("yyyy-MM-dd");

  // Accept HEAD so crawlers / GSC probes don't 405 → broken status-code re-execute.
  [AcceptVerbs("GET", "HEAD")]
  [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
  public IActionResult SitemapIndex()
  {
    XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
    var doc = new XDocument(
      new XDeclaration("1.0", "utf-8", "yes"),
      new XElement(ns + "sitemapindex",
        new XElement(ns + "sitemap",
          new XElement(ns + "loc", SeoDefaults.PreferredOrigin + "/sitemap-pages.xml"),
          new XElement(ns + "lastmod", ContentStamp)),
        new XElement(ns + "sitemap",
          new XElement(ns + "loc", SeoDefaults.PreferredOrigin + "/sitemap-routes.xml"),
          new XElement(ns + "lastmod", ContentStamp)),
        new XElement(ns + "sitemap",
          new XElement(ns + "loc", SeoDefaults.PreferredOrigin + "/sitemap-cities.xml"),
          new XElement(ns + "lastmod", ContentStamp)),
        new XElement(ns + "sitemap",
          new XElement(ns + "loc", SeoDefaults.PreferredOrigin + "/sitemap-guides.xml"),
          new XElement(ns + "lastmod", ContentStamp))
      )
    );
    return Xml(doc);
  }

  [AcceptVerbs("GET", "HEAD")]
  [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
  public IActionResult SitemapPages()
  {
    var urls = new (string Path, string Freq, string Priority)[]
    {
      ("/", "daily", "1.0"),
      ("/routes", "daily", "0.9"),
      ("/cities", "weekly", "0.85"),
      ("/Home/ContactUs", "monthly", "0.5"),
      ("/Home/FAQ", "monthly", "0.6"),
      ("/Home/TravelPolicy", "monthly", "0.4"),
      ("/Home/Privacy", "monthly", "0.4"),
    };
    return Urlset(urls, includeImage: true);
  }

  [AcceptVerbs("GET", "HEAD")]
  [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
  public IActionResult SitemapRoutes()
  {
    var urls = RouteCatalog.All
      .Select(r => ($"/routes/{r.Slug}", r.IsPrimary ? "weekly" : "monthly", r.IsPrimary ? "0.8" : "0.65"))
      .ToArray();
    return Urlset(urls, includeImage: false);
  }

  [AcceptVerbs("GET", "HEAD")]
  [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
  public IActionResult SitemapCities()
  {
    var urls = CityCatalog.All
      .Select(c => ($"/cities/{c.Slug}", "weekly", "0.75"))
      .ToArray();
    return Urlset(urls, includeImage: false);
  }

  [AcceptVerbs("GET", "HEAD")]
  [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
  public IActionResult SitemapGuides()
  {
    var urls = RouteCatalog.All
      .Select(r => ($"/routes/{r.Slug}/guide", r.IsPrimary ? "monthly" : "monthly", r.IsPrimary ? "0.7" : "0.6"))
      .ToArray();
    return Urlset(urls, includeImage: false);
  }

  private IActionResult Urlset(
    IReadOnlyList<(string Path, string Freq, string Priority)> urls,
    bool includeImage)
  {
    XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
    XNamespace image = "http://www.google.com/schemas/sitemap-image/1.1";

    var urlElements = urls.Select(u =>
    {
      var el = new XElement(ns + "url",
        new XElement(ns + "loc", SeoDefaults.BuildCanonical(u.Path)),
        new XElement(ns + "lastmod", ContentStamp),
        new XElement(ns + "changefreq", u.Freq),
        new XElement(ns + "priority", u.Priority)
      );
      if (includeImage && u.Path == "/")
      {
        el.Add(new XElement(image + "image",
          new XElement(image + "loc", SeoDefaults.DefaultOgImageUrl.Split('?')[0]),
          new XElement(image + "title", SeoDefaults.SiteName + " — سواری بین‌شهری")
        ));
      }
      return el;
    });

    var root = includeImage
      ? new XElement(ns + "urlset",
          new XAttribute(XNamespace.Xmlns + "image", image),
          urlElements)
      : new XElement(ns + "urlset", urlElements);

    var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
    return Xml(doc);
  }

  private ContentResult Xml(XDocument doc)
  {
    var sb = new StringBuilder();
    using (var writer = new StringWriter(sb))
      doc.Save(writer);

    var xml = sb.ToString();
    if (xml.StartsWith("<?xml", StringComparison.Ordinal))
    {
      var end = xml.IndexOf("?>", StringComparison.Ordinal);
      if (end > 0)
        xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + xml[(end + 2)..];
    }

    Response.Headers["X-Robots-Tag"] = "noarchive";
    return Content(xml, "application/xml", Encoding.UTF8);
  }
}
