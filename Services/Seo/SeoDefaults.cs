using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Application.Services.Seo;

/// <summary>Global-class SEO defaults (meta, robots helpers, JSON-LD graph).</summary>
public static class SeoDefaults
{
  public static string PreferredOrigin { get; private set; } = "https://mrshoofer.com";
  public static string PublicSiteHost { get; private set; } = "mrshoofer.com";

  public static void Configure(IConfiguration configuration)
  {
    var section = configuration.GetSection(SeoOptions.SectionName);
    var origin = section["PreferredOrigin"];
    if (!string.IsNullOrWhiteSpace(origin))
      PreferredOrigin = origin.Trim().TrimEnd('/');

    var host = section["PublicSiteHost"];
    if (!string.IsNullOrWhiteSpace(host))
      PublicSiteHost = host.Trim();
  }

  public static string DefaultOgImageUrl => PreferredOrigin + DefaultOgImagePath + "?v=1";

  public const string SiteName = "مسترشوفر";
  /// <summary>Spaced brand spelling people type in search («مستر شوفر»).</summary>
  public const string SiteNameSpaced = "مستر شوفر";
  public const string SiteNameEn = "MrShoofer";
  /// <summary>Spaced English brand spelling («Mr Shoofer» / «mr shoofer»).</summary>
  public const string SiteNameEnSpaced = "Mr Shoofer";
  public const string DefaultTitle = "رزرو تاکسی بین شهری و سواری دربستی";
  public const string DefaultDescription =
    "مستر شوفر (مسترشوفر) — رزرو آنلاین تاکسی بین شهری، سواری دربستی و ترانسفر فرودگاهی با رانندگان تأییدشده | رزرو آنلاین";

  /// <summary>All brand spellings for schema alternateName (FA + EN, spaced and compound).</summary>
  public static readonly string[] BrandAlternateNames =
  [
    SiteNameSpaced,
    SiteNameEn,
    SiteNameEnSpaced,
    "mrshoofer",
    "mr shoofer"
  ];
  public const string DefaultOgImagePath = "/og-home.jpg";
  public const int OgImageWidth = 1200;
  public const int OgImageHeight = 630;
  public const string SupportPhone = "+982128422243";
  public const string SupportEmail = "support@mrshoofer.ir";
  public const string ContentLanguage = "fa-IR";

  /// <summary>Homepage FAQs — must match visible FAQ section on Index.</summary>
  public static readonly (string Question, string Answer)[] HomeFaqs =
  [
    (
      "مسترشوفر چه خدماتی ارائه می‌دهد؟",
      "مستر شوفر (همان مسترشوفر) سامانه رزرو آنلاین تاکسی بین شهری، سواری دربستی و ترانسفر فرودگاهی با ناوگان سواری است. می‌توانید از بین کلاس‌های متنوع سفر انتخاب کنید و بلیط را در کمتر از چند دقیقه دریافت کنید."
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
    ("انتخاب مبدأ و مقصد", "در فرم جستجو مبدأ و مقصد را بنویسید، یا از صفحه یک مسیر آماده مثل تهران–اصفهان وارد شوید."),
    ("انتخاب تاریخ", "تاریخ حرکت را انتخاب کنید تا سواری‌های همان روز نمایش داده شوند."),
    ("مقایسه و رزرو", "بین کلاس‌های متنوع ناوگان ما خودرو متناسب با سفر خود را انتخاب کنید و سفر خود را رزرو کنید."),
    ("دریافت بلیط", "بلیط خود را با چند کلیک ساده دریافت کنید؛ همکاران ما در پشتیبانی مسترشوفر برای هماهنگی‌های مورد نیاز سفر با شما تماس خواهند گرفت.")
  ];

  public sealed record FaqSection(string Heading, (string Question, string Answer)[] Items);

  /// <summary>Dedicated /Home/FAQ content — must match the visible accordion (not HomeFaqs).</summary>
  public static readonly FaqSection[] FaqPageSections =
  [
    new("خرید و رزرو بلیط",
    [
      (
        "چطور بلیط بخرم؟",
        "در صفحه اصلی مبدأ، مقصد و تاریخ را وارد کنید و جستجو را بزنید. از نتایج، کلاس سفر (اشتراکی یا دربستی) را انتخاب کنید، مشخصات مسافر را وارد کنید و پرداخت را تمام کنید. پس از تأیید، بلیط صادر می‌شود و پشتیبانی برای هماهنگی مبدأ و مقصد با شما تماس می‌گیرد."
      ),
      (
        "آیا می‌توانم بلیط را کنسل کنم؟",
        "بله، تا وقتی که مهلت کنسلی تمام نشده باشد. تا یک ساعت بعد از خرید بدون جریمه است؛ تا ۱۲ ساعت مانده به سفر ۲۰٪ و تا ۳ ساعت مانده ۵۰٪ جریمه دارد. کمتر از ۳ ساعت مانده به سفر امکان کنسلی نیست. جزئیات در صفحه قوانین سفر آمده است."
      ),
      (
        "کد تخفیف چگونه اعمال می‌شود؟",
        "در صفحه تأیید بلیط، کد را در بخش «کد تخفیف» وارد کنید و «اعمال» را بزنید. مبلغ تخفیف همان‌جا از قیمت نهایی کم می‌شود."
      ),
      (
        "بلیطم کجا ارسال می‌شود؟",
        "پس از صدور، لینک اطلاعات سفر و فایل بلیط به شماره موبایلی که برای مسافر ثبت کرده‌اید پیامک می‌شود. همان اطلاعات در حساب کاربری، بخش سفرهای من، هم قابل مشاهده است."
      ),
      (
        "آیا امکان رزرو برای چند نفر وجود دارد؟",
        "هر رزرو برای یک مسافر ثبت می‌شود. برای همراهان، فرآیند را به تعداد نفرات تکرار کنید. اگر کل خودرو را می‌خواهید، کلاس دربستی را انتخاب کنید — ظرفیت دربستی ۳ مسافر است."
      )
    ]),
    new("پرداخت و کیف پول",
    [
      (
        "چه روش‌های پرداختی پشتیبانی می‌شود؟",
        "پرداخت از طریق درگاه اینترنتی (کارت بانکی) و کیف پول مسترشوفر انجام می‌شود. جزئیات کارت نزد درگاه می‌ماند؛ نتیجه تراکنش برای صدور بلیط ثبت می‌شود."
      ),
      (
        "کیف پول مسترشوفر چیست؟",
        "یک اعتبار داخلی در حساب کاربری است. پس از ورود می‌توانید آن را شارژ کنید و برای خرید بلیط استفاده کنید. بدون ورود، پرداخت از کیف پول در دسترس نیست."
      )
    ]),
    new("سفر و جابجایی",
    [
      (
        "مبدأ و مقصد دقیق چطور مشخص می‌شود؟",
        "پس از تأیید بلیط، تیم مسترشوفر محل سوار و پیاده شدن را با مسافر هماهنگ می‌کند. شماره تماس مسافر باید صحیح باشد تا هماهنگی انجام شود."
      ),
      (
        "ترانسفر فرودگاهی چطور رزرو می‌شود؟",
        "مثل سواری بین‌شهری: مبدأ یا مقصد را فرودگاه (یا شهر متصل به آن) بگذارید، تاریخ را انتخاب کنید و از نتایج کلاس مناسب را رزرو کنید. زمان پرواز را هنگام هماهنگی اعلام کنید."
      ),
      (
        "مدارک هنگام سوار شدن چیست؟",
        "همراه داشتن مدرک شناسایی مسافر هنگام سوار شدن الزامی است. بلیط پیامک‌شده را هم در دسترس داشته باشید تا هویت و رزرو تطبیق داده شود."
      ),
      (
        "ظرفیت خودرو دربستی چند نفر است؟",
        "ظرفیت سرویس دربستی ۳ مسافر است. تغییر مسیر یا مقصد اضافه فقط با هماهنگی راننده ممکن است و هزینه‌اش جدا حساب می‌شود. متن کامل در قوانین سفر است."
      )
    ])
  ];

  public static IReadOnlyList<(string Question, string Answer)> FaqPageItems()
    => FaqPageSections.SelectMany(s => s.Items).ToList();

  /// <summary>Hero search dropdown «شهرهای پرتردد» — filtered against live ORS cities.</summary>
  public static readonly string[] HomepagePopularOriginCities =
  [
    "تهران",
    "اصفهان",
    "رشت",
    "چالوس",
    "کرمانشاه",
    "نوشهر",
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

  /// <summary>
  /// Static fallback starting prices (تومان) when live ORS quotes are unavailable.
  /// Live ECO prices come from <see cref="Homepage.IHomepageCatalogCache"/>.
  /// </summary>
  private static readonly Dictionary<string, long> HomepageRouteStartingPriceToman = new(StringComparer.OrdinalIgnoreCase)
  {
    ["tehran-isfahan"] = 3_500_000,
    ["tehran-mashhad"] = 7_500_000,
    ["tehran-rasht"] = 2_800_000,
    ["tehran-shiraz"] = 6_500_000,
    ["tehran-tabriz"] = 5_500_000,
    ["tehran-chalus"] = 2_200_000,
    ["tehran-bandarabbas"] = 9_000_000,
    ["tehran-ahvaz"] = 7_000_000,
    ["tehran-sari"] = 3_200_000,
    ["tehran-qom"] = 1_200_000,
    ["isfahan-bandarabbas"] = 5_500_000,
    ["isfahan-shiraz"] = 2_800_000,
  };

  public static long? HomepageRouteStartingPrice(string? slug)
  {
    if (string.IsNullOrWhiteSpace(slug)) return null;
    return HomepageRouteStartingPriceToman.TryGetValue(slug.Trim(), out var price) ? price : null;
  }

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
      || path.Contains("/otapanel", StringComparison.Ordinal)
      || path.Contains("/admin", StringComparison.Ordinal)
      || path.Contains("/payment", StringComparison.Ordinal)
      || path.Contains("/agency", StringComparison.Ordinal)
      || path.Contains("/customer/", StringComparison.Ordinal)
      || path.Contains("/customerservice", StringComparison.Ordinal)
      || path.Contains("/reserveinfo", StringComparison.Ordinal)
      || path.Contains("/tripreceipt", StringComparison.Ordinal)
      || path.Contains("/partner", StringComparison.Ordinal)
      || path.Contains("/taxitrips", StringComparison.Ordinal)
      || path.Contains("/error", StringComparison.Ordinal)
      || path.EndsWith("/home/error", StringComparison.Ordinal)
      || path == "/home/error";
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
    ["alternateName"] = BrandAlternateNames,
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
    ["alternateName"] = BrandAlternateNames,
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
