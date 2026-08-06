using System.Text.Encodings.Web;
using System.Text.Json;

namespace Application.Services.Seo;

/// <summary>Global-class SEO defaults for sale.shoofer.taxi (meta, robots helpers, JSON-LD graph).</summary>
public static class SeoDefaults
{
  public const string PreferredOrigin = "https://sale.shoofer.taxi";
  public const string SiteName = "مسترشوفر";
  public const string SiteNameEn = "MrShoofer";
  public const string DefaultTitle = "سواری بین شهری";
  public const string DefaultDescription =
    "سواری بین‌شهری و ترانسفر فرودگاهی با رانندگان تأییدشده و بهترین خودروها | رزرو آنلاین مسترشوفر";
  public const string DefaultOgImagePath = "/og-home.jpg";
  /// <summary>Cache-busted absolute OG image (shared brand asset; titles/descriptions stay per-page).</summary>
  public const string DefaultOgImageUrl = PreferredOrigin + DefaultOgImagePath + "?v=1";
  public const int OgImageWidth = 1200;
  public const int OgImageHeight = 630;
  public const string SupportPhone = "+982128422243";
  public const string SupportEmail = "support@mrshoofer.ir";
  public const string ContentLanguage = "fa-IR";

  /// <summary>Homepage FAQs — must match visible FAQ section on Index.</summary>
  public static readonly (string Question, string Answer)[] HomeFaqs =
  [
    (
      "مسترشوفر چیست؟",
      "مسترشوفر سامانه رزرو آنلاین سواری بین‌شهری و ترانسفر فرودگاهی با ناوگان سواری است. می‌توانید از بین کلاس‌های متنوع سفر انتخاب کنید و بلیط را در کمتر از چند دقیقه دریافت کنید."
    ),
    (
      "چطور سواری رزرو کنم؟",
      "در صفحه اصلی مبدأ، مقصد و تاریخ سفر را وارد کنید، روی «بزن بریم» بزنید، کلاس و سواری مناسب را انتخاب کنید و رزرو را تکمیل کنید. همکاران ما برای هماهنگی سفر در سریع‌ترین زمان ممکن با شما تماس می‌گیرند."
    ),
    (
      "آیا ناوگان و رانندگان تأییدشده هستند؟",
      "بله. مسترشوفر با ناوگان سواری و رانندگان مجرب و تأییدشده همکاری می‌کند تا سفر شما با آرامش خاطر و امنیت انجام شود."
    ),
    (
      "آیا ترانسفر فرودگاهی هم دارید؟",
      "بله. علاوه بر سواری بین‌شهری، ترانسفر فرودگاهی نیز از طریق همین سامانه قابل جستجو و رزرو است."
    ),
    (
      "کلاس‌های متنوع سفر یعنی چه؟",
      "پس از جستجو می‌توانید از بین گزینه‌ها و کلاس‌های مختلف نمایش‌داده‌شده، سفری متناسب با نیاز و بودجه خود انتخاب کنید — از جمله سرویس اشتراکی یا دربستی در مسیرهای پشتیبانی‌شده."
    ),
    (
      "بلیط را چه زمانی دریافت می‌کنم؟",
      "پس از تکمیل رزرو، بلیط خود را در کمتر از چند دقیقه دریافت می‌کنید و جزئیات سفر در اختیار شما قرار می‌گیرد."
    ),
    (
      "هماهنگی بعد از رزرو چگونه است؟",
      "همکاران مسترشوفر پس از رزرو برای هماهنگی سفر در سریع‌ترین زمان ممکن با شما تماس می‌گیرند و تا آخرین لحظه سفر همراه شما هستند."
    ),
    (
      "در چه مسیرهایی می‌توانم سفر کنم؟",
      "مسترشوفر پوشش مسیرهای بین‌شهری در ایران را ارائه می‌دهد. صفحه مسیرها را ببینید یا مبدأ و مقصد را در صفحه اصلی جستجو کنید."
    ),
    (
      "هزینه سفر چطور مشخص می‌شود؟",
      "پس از انتخاب مبدأ، مقصد و تاریخ، قیمت گزینه‌های موجود در نتایج جستجو نمایش داده می‌شود تا بتوانید قبل از رزرو هزینه را مقایسه کنید."
    ),
    (
      "اگر به پشتیبانی نیاز داشتم چه کنم؟",
      "پشتیبانی ۲۴/۷ تا آخرین لحظه سفر در دسترس است. از بخش ارتباط با ما یا ایمیل support@mrshoofer.ir می‌توانید پیام بگذارید. تلفن: ۰۲۱-۲۸۴۲۲۲۴۳."
    )
  ];

  public static readonly (string Title, string Text)[] BookingHowToSteps =
  [
    ("انتخاب مبدأ و مقصد", "در فرم جستجو مبدأ و مقصد سفر را مشخص کنید یا از صفحه یک مسیر آماده وارد شوید."),
    ("انتخاب تاریخ", "تاریخ حرکت را انتخاب کنید تا گزینه‌های همان روز نمایش داده شوند."),
    ("مقایسه و رزرو", "کلاس مناسب را انتخاب و رزرو را تکمیل کنید."),
    ("دریافت بلیط", "بلیط را دریافت کنید و منتظر هماهنگی پشتیبانی بمانید.")
  ];

  /// <summary>Curated homepage internal links (money routes) — not sync IsPrimary order.</summary>
  public static readonly string[] HomepagePopularRouteSlugs =
  [
    "tehran-isfahan",
    "tehran-mashhad",
    "tehran-rasht",
    "tehran-shiraz",
    "tehran-tabriz",
    "tehran-chalus",
    "tehran-bandarabbas",
    "tehran-ahvaz",
    "tehran-sari",
    "tehran-qom",
    "isfahan-bandarabbas",
    "isfahan-shiraz",
  ];

  /// <summary>City hub chips under homepage popular routes.</summary>
  public static readonly string[] HomepagePopularCitySlugs =
  [
    "tehran", "karaj", "isfahan", "shiraz", "mashhad", "bandarabbas"
  ];

  public static IReadOnlyList<RouteCatalog.RoutePage> HomepagePopularRoutes()
  {
    var list = new List<RouteCatalog.RoutePage>(HomepagePopularRouteSlugs.Length);
    foreach (var slug in HomepagePopularRouteSlugs)
    {
      var r = RouteCatalog.FindBySlug(slug);
      if (r is not null) list.Add(r);
    }
    return list;
  }

  public static IReadOnlyList<CityCatalog.CityProfile> HomepagePopularCities()
  {
    var list = new List<CityCatalog.CityProfile>(HomepagePopularCitySlugs.Length);
    foreach (var slug in HomepagePopularCitySlugs)
    {
      var c = CityCatalog.FindBySlug(slug);
      if (c is not null) list.Add(c);
    }
    return list;
  }

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    WriteIndented = false
  };

  public static string BuildCanonical(string? path)
  {
    path ??= "/";
    if (!path.StartsWith('/')) path = "/" + path;
    var q = path.IndexOf('?', StringComparison.Ordinal);
    if (q >= 0) path = path[..q];
    if (path.Length > 1) path = path.TrimEnd('/');
    return path == "/" || path.Length == 0
      ? PreferredOrigin + "/"
      : PreferredOrigin + path;
  }

  /// <summary>Normalize any relative/absolute OG image path to an absolute https URL.</summary>
  public static string ResolveOgImage(string? imageUrlOrPath = null)
  {
    if (string.IsNullOrWhiteSpace(imageUrlOrPath))
      return DefaultOgImageUrl;

    var s = imageUrlOrPath.Trim();
    if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
      return s;

    if (!s.StartsWith('/')) s = "/" + s;
    return PreferredOrigin + s;
  }

  public static string BuildOgTitle(string? pageTitle)
  {
    var t = string.IsNullOrWhiteSpace(pageTitle) ? DefaultTitle : pageTitle.Trim();
    if (t.Contains(SiteName, StringComparison.Ordinal))
      return t;
    return $"{t} | {SiteName}";
  }

  public static string BuildRouteOgAlt(string originFa, string destinationFa) =>
    $"رزرو سواری {originFa} به {destinationFa} — {SiteName}";

  public static string BuildCityOgAlt(string cityFa) =>
    $"سواری بین‌شهری از و به {cityFa} — {SiteName}";

  public static bool IsPrivatePath(string? path)
  {
    path = (path ?? "").ToLowerInvariant();
    return path.Contains("/auth", StringComparison.Ordinal)
      || path.Contains("/admin", StringComparison.Ordinal)
      || path.Contains("/payment", StringComparison.Ordinal)
      || path.Contains("/agency", StringComparison.Ordinal)
      || path.Contains("/customerservice", StringComparison.Ordinal)
      || path.Contains("/reserveinfo", StringComparison.Ordinal)
      || path.Contains("/tripreceipt", StringComparison.Ordinal)
      || path.Contains("/partner", StringComparison.Ordinal)
      || path.Contains("/taxitrips", StringComparison.Ordinal);
  }

  public sealed class JsonLdOptions
  {
    public IReadOnlyList<(string Question, string Answer)>? Faqs { get; init; }
    public IReadOnlyList<(string Name, string Url)>? Breadcrumbs { get; init; }
    public string? ServiceName { get; init; }
    public string? OriginCity { get; init; }
    public string? DestinationCity { get; init; }
    public IReadOnlyList<(string Title, string Text)>? HowToSteps { get; init; }
    public IReadOnlyList<(string Name, string Url)>? ItemList { get; init; }
    public string? OgImageUrl { get; init; }
    public string? DateModifiedIso { get; init; }
  }

  public static string BuildJsonLdGraph(
    string canonical,
    string fullTitle,
    string description,
    IReadOnlyList<(string Question, string Answer)>? faqs = null,
    IReadOnlyList<(string Name, string Url)>? breadcrumbs = null,
    string? serviceName = null) =>
    BuildJsonLdGraph(canonical, fullTitle, description, new JsonLdOptions
    {
      Faqs = faqs,
      Breadcrumbs = breadcrumbs,
      ServiceName = serviceName
    });

  public static string BuildJsonLdGraph(
    string canonical,
    string fullTitle,
    string description,
    JsonLdOptions options)
  {
    options ??= new JsonLdOptions();
    var og = string.IsNullOrWhiteSpace(options.OgImageUrl)
      ? DefaultOgImageUrl
      : ResolveOgImage(options.OgImageUrl);

    var nodes = new List<object>
    {
      OrganizationNode(),
      WebsiteNode(),
      WebPageNode(canonical, fullTitle, description, og, options.DateModifiedIso),
      ServiceNode(canonical, options)
    };

    if (options.Breadcrumbs is { Count: > 0 } crumbs)
    {
      nodes.Add(new Dictionary<string, object?>
      {
        ["@type"] = "BreadcrumbList",
        ["@id"] = canonical + "#breadcrumb",
        ["itemListElement"] = crumbs.Select((b, i) => new Dictionary<string, object?>
        {
          ["@type"] = "ListItem",
          ["position"] = i + 1,
          ["name"] = b.Name,
          ["item"] = b.Url
        }).ToArray()
      });
    }

    if (options.Faqs is { Count: > 0 } faqList)
    {
      nodes.Add(new Dictionary<string, object?>
      {
        ["@type"] = "FAQPage",
        ["@id"] = canonical + "#faq",
        ["isPartOf"] = new Dictionary<string, object?> { ["@id"] = canonical + "#webpage" },
        ["mainEntity"] = faqList.Select(f => new Dictionary<string, object?>
        {
          ["@type"] = "Question",
          ["name"] = f.Question,
          ["acceptedAnswer"] = new Dictionary<string, object?>
          {
            ["@type"] = "Answer",
            ["text"] = f.Answer
          }
        }).ToArray()
      });
    }

    var howTo = options.HowToSteps is { Count: > 0 } ? options.HowToSteps : null;
    if (howTo is not null)
    {
      nodes.Add(new Dictionary<string, object?>
      {
        ["@type"] = "HowTo",
        ["@id"] = canonical + "#howto",
        ["name"] = options.ServiceName is { Length: > 0 } s
          ? $"نحوه رزرو {s}"
          : "نحوه رزرو سواری بین‌شهری در مسترشوفر",
        ["description"] = description,
        ["inLanguage"] = ContentLanguage,
        ["step"] = howTo.Select((step, i) => new Dictionary<string, object?>
        {
          ["@type"] = "HowToStep",
          ["position"] = i + 1,
          ["name"] = step.Title,
          ["text"] = step.Text
        }).ToArray()
      });
    }

    if (options.ItemList is { Count: > 0 } items)
    {
      nodes.Add(new Dictionary<string, object?>
      {
        ["@type"] = "ItemList",
        ["@id"] = canonical + "#itemlist",
        ["itemListElement"] = items.Select((it, i) => new Dictionary<string, object?>
        {
          ["@type"] = "ListItem",
          ["position"] = i + 1,
          ["name"] = it.Name,
          ["url"] = it.Url
        }).ToArray()
      });
    }

    var graph = new Dictionary<string, object?>
    {
      ["@context"] = "https://schema.org",
      ["@graph"] = nodes.ToArray()
    };

    return JsonSerializer.Serialize(graph, JsonOptions);
  }

  private static Dictionary<string, object?> OrganizationNode() => new()
  {
    ["@type"] = new[] { "Organization", "TravelAgency" },
    ["@id"] = PreferredOrigin + "/#organization",
    ["name"] = SiteName,
    ["alternateName"] = new[] { SiteNameEn, "مستر شوفر", "Mr Shoofer" },
    ["url"] = PreferredOrigin + "/",
    ["logo"] = new Dictionary<string, object?>
    {
      ["@type"] = "ImageObject",
      ["url"] = PreferredOrigin + "/logo_full_b.png",
      ["width"] = 512,
      ["height"] = 512
    },
    ["image"] = DefaultOgImageUrl,
    ["description"] = "سواری بین‌شهری و ترانسفر فرودگاهی با رانندگان تأییدشده",
    ["email"] = SupportEmail,
    ["telephone"] = SupportPhone,
    ["areaServed"] = new Dictionary<string, object?>
    {
      ["@type"] = "Country",
      ["name"] = "Iran"
    },
    ["contactPoint"] = new[]
    {
      new Dictionary<string, object?>
      {
        ["@type"] = "ContactPoint",
        ["telephone"] = SupportPhone,
        ["email"] = SupportEmail,
        ["contactType"] = "customer support",
        ["availableLanguage"] = new[] { "Persian", "fa" },
        ["areaServed"] = "IR"
      }
    },
    ["sameAs"] = Array.Empty<string>() // fill when official social profiles are confirmed
  };

  private static Dictionary<string, object?> WebsiteNode() => new()
  {
    ["@type"] = "WebSite",
    ["@id"] = PreferredOrigin + "/#website",
    ["url"] = PreferredOrigin + "/",
    ["name"] = SiteName,
    ["alternateName"] = SiteNameEn,
    ["inLanguage"] = ContentLanguage,
    ["publisher"] = new Dictionary<string, object?> { ["@id"] = PreferredOrigin + "/#organization" },
    // SearchAction targets a stable public landing (homepage form), never thin result pages.
    ["potentialAction"] = new Dictionary<string, object?>
    {
      ["@type"] = "SearchAction",
      ["target"] = new Dictionary<string, object?>
      {
        ["@type"] = "EntryPoint",
        ["urlTemplate"] = PreferredOrigin + "/#tripForm"
      },
      ["query-input"] = "required name=search_term_string"
    }
  };

  private static Dictionary<string, object?> WebPageNode(
    string canonical, string fullTitle, string description, string og, string? dateModified) =>
    new()
    {
      ["@type"] = "WebPage",
      ["@id"] = canonical + "#webpage",
      ["url"] = canonical,
      ["name"] = fullTitle,
      ["description"] = description,
      ["inLanguage"] = ContentLanguage,
      ["isPartOf"] = new Dictionary<string, object?> { ["@id"] = PreferredOrigin + "/#website" },
      ["about"] = new Dictionary<string, object?> { ["@id"] = PreferredOrigin + "/#organization" },
      ["primaryImageOfPage"] = new Dictionary<string, object?>
      {
        ["@type"] = "ImageObject",
        ["url"] = og,
        ["width"] = OgImageWidth,
        ["height"] = OgImageHeight
      },
      ["dateModified"] = dateModified ?? DateTime.UtcNow.ToString("yyyy-MM-dd")
    };

  private static Dictionary<string, object?> ServiceNode(string canonical, JsonLdOptions options)
  {
    var name = string.IsNullOrWhiteSpace(options.ServiceName)
      ? "سواری بین‌شهری"
      : options.ServiceName!;

    var node = new Dictionary<string, object?>
    {
      ["@type"] = "Service",
      ["@id"] = canonical + "#service",
      ["name"] = name,
      ["serviceType"] = "Intercity taxi and airport transfer booking",
      ["provider"] = new Dictionary<string, object?> { ["@id"] = PreferredOrigin + "/#organization" },
      ["areaServed"] = new Dictionary<string, object?>
      {
        ["@type"] = "Country",
        ["name"] = "Iran"
      },
      ["url"] = canonical,
      ["availableChannel"] = new Dictionary<string, object?>
      {
        ["@type"] = "ServiceChannel",
        ["serviceUrl"] = PreferredOrigin + "/",
        ["availableLanguage"] = ContentLanguage
      }
    };

    if (!string.IsNullOrWhiteSpace(options.OriginCity) && !string.IsNullOrWhiteSpace(options.DestinationCity))
    {
      node["providerMobility"] = "dynamic";
      node["additionalType"] = "https://schema.org/TaxiService";
      node["description"] =
        $"رزرو آنلاین سواری از {options.OriginCity} به {options.DestinationCity}";
    }

    return node;
  }
}
