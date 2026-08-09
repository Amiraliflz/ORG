namespace Application.Services.Seo;

/// <summary>City profiles for unique city-hub pages and per-route copy (generated + hand overlay + stubs).</summary>
public static class CityCatalog
{
  public sealed record CityProfile(
    string NameFa,
    string Slug,
    string RegionFa,
    string RoleFa,
    string BlurbFa,
    string[] TripTipsFa,
    string[] NearbyDestHintsFa);

  /// <summary>Hand-authored profiles — overlay richer copy when city appears in generated catalog.</summary>
  private static readonly Dictionary<string, CityProfile> HandByName =
    new(StringComparer.Ordinal)
    {
      ["تهران"] = P("تهران", "tehran", "پایتخت و مرکز ایران", "قطب اصلی سفرهای بین‌شهری",
        "تهران پرتقاضاترین مبدأ و مقصد سواری بین‌شهری است؛ اتصال به فرودگاه‌ها، ایستگاه‌ها و شهرهای شمال، غرب و مرکز کشور از اینجا شکل می‌گیرد.",
        ["برای خروج از تهران، ساعت‌های شلوغ صبح و عصر (ترافیک سنگین) را در برنامه‌تان بگذارید.", "اگر قصد حرکت از مبدأ فرودگاه تهران را دارید، زمان ترافیک در مسیر مقصد را در نظر بگیرید؛ یا اگر در تصمیم برای زمان سفر دچار تردید هستید، با پشتیبانی ما در تماس باشید."],
        ["شمال کشور", "اصفهان و قم", "غرب کشور"]),
      ["اصفهان"] = P("اصفهان", "isfahan", "مرکز ایران", "گردشگری و اتصال بین تهران و جنوب",
        "اصفهان یکی از مسیرهای کلاسیک تهران–مرکز است و برای سفر کاری، گردشگری و اتصال به شهرهای اطراف پرتقاضا است.",
        ["مسیر اتوبانی تهران–اصفهان عموماً روان‌تر از جاده‌های کوهستانی شمال است.", "رزرو از قبل، در تعطیلات نوروز و پایان هفته توصیه می‌شود."],
        ["تهران", "کاشان", "شیراز"]),
      ["رشت"] = P("رشت", "rasht", "شمال", "دروازه گیلان",
        "رشت قطب مسیرهای شمال و باران‌خیز است؛ سفر از تهران و شهرهای مرکزی به رشت در تعطیلات بسیار پرتقاضا است.",
        ["در بارندگی و مه، زمان مسیر می‌تواند طولانی‌تر شود.", "اگر مقصد جنگلی یا ساحلی دارید، برای هماهنگی مقصد خود با پشتیبانی تماس حاصل کنید."],
        ["لاهیجان", "تهران", "قزوین"]),
      ["لاهیجان"] = P("لاهیجان", "lahijan", "گیلان / شرق گیلان", "گردشگری چای و طبیعت",
        "لاهیجان مقصد محبوب سفرهای کوتاه و میان‌مدت از تهران و رشت است.",
        ["ترکیب ترافیک شمال و پایان هفته را در برنامه‌ریزی لحاظ کنید."],
        ["رشت", "رامسر", "تهران"]),
      ["چالوس"] = P("چالوس", "chalus", "مازندران / جاده چالوس", "مسیر کوهستانی و گردشگری",
        "چالوس با جاده معروف خود یکی از حساس‌ترین مسیرهای شمال از نظر زمان سفر و ترافیک فصلی است.",
        ["در تعطیلات آخر هفته و تعطیلات نوروزی ترافیک در این محور شلوغ‌تر می‌شود؛ ممکن است در زمان رسیدن شما تا مقصد اختلاف وجود داشته باشد.", "شرایط جوی این محور ممکن است در مواقعی باعث تأخیر در رسیدن شود."],
        ["نوشهر", "تهران", "رامسر"]),
      ["نوشهر"] = P("نوشهر", "nowshahr", "مازندران / ساحل", "گردشگری ساحلی و فرودگاهی منطقه",
        "نوشهر از مقاصد پرتکرار شمال برای سفر تفریحی و اتصال به نوار ساحلی است.",
        ["برای رسیدن به ساحل یا هتل، جزئیات محل پیاده شدن را با راننده هماهنگ کنید."],
        ["چالوس", "رامسر", "تهران"]),
      ["رامسر"] = P("رامسر", "ramsar", "مازندران / غرب مازندران", "گردشگری کوه و دریا",
        "رامسر مقصد ترکیبی کوهستان و ساحل است و مسیر تهران–رامسر در فصل سفر پر ازدحام می‌شود.",
        ["در فصل شلوغی سفر، رزرو زودهنگام کمک می‌کند."],
        ["چالوس", "لاهیجان", "تهران"]),
      ["ساری"] = P("ساری", "sari", "مازندران / مرکز استان", "مرکز اداری و اتصال شرق مازندران",
        "ساری مرکز استان مازندران و گره ارتباطی شرق شمال با تهران است.",
        ["مدت مسیر نسبت به جاده چالوس معمولاً پایدارتر است ولی در تعطیلات همچنان شلوغ می‌شود."],
        ["تهران", "گرگان", "رامسر"]),
      ["کاشان"] = P("کاشان", "kashan", "مرکز / اصفهان", "توقفگاهی میان تهران و مرکز",
        "کاشان روی محور تهران–اصفهان قرار دارد و برای سفرهای کوتاه و میان‌مدت گزینه پرتکرار است.",
        ["زمان تقریبی مسیر از تهران معمولاً کوتاه‌تر از اصفهان است."],
        ["تهران", "اصفهان", "قم"]),
      ["همدان"] = P("همدان", "hamedan", "غرب ایران", "گردشگری تاریخی غرب",
        "همدان از مسیرهای پرتقاضای غرب کشور از تهران است.",
        ["در زمستان، شرایط جوی غرب را بررسی کنید."],
        ["تهران", "سنندج", "کرمانشاه"]),
      ["زنجان"] = P("زنجان", "zanjan", "شمال‌غرب", "اتصال به تبریز و اردبیل",
        "زنجان روی محور شمال‌غرب قرار دارد و اغلب در مسیرهای طولانی‌تر به‌عنوان گره عبور شناخته می‌شود.",
        ["برای ادامه مسیر به تبریز/اردبیل، برنامه‌ریزی توقف و زمان را یکجا ببینید."],
        ["تهران", "تبریز", "اردبیل"]),
      ["اردبیل"] = P("اردبیل", "ardabil", "شمال‌غرب / سردسیر", "گردشگری طبیعی و مسیر طولانی",
        "اردبیل مسیر طولانی‌تری از تهران دارد و در فصول سرد نیازمند برنامه‌ریزی دقیق‌تری است.",
        ["زمان تقریبی مسیر را با حاشیه در نظر بگیرید.", "در زمستان زنجیرچرخ و تأخیرهای جوی محتمل است."],
        ["تهران", "تبریز", "رشت"]),
      ["تبریز"] = P("تبریز", "tabriz", "شمال‌غرب", "قطب اقتصادی و گردشگری",
        "تبریز مقصد مهم تجاری و گردشگری شمال‌غرب است؛ مسیر تهران–تبریز از پرتکرارترین مسیرهای طولانی است.",
        ["رزرو از چند روز قبل در ایام پیک توصیه می‌شود."],
        ["تهران", "زنجان", "اردبیل"]),
      ["قم"] = P("قم", "qom", "مرکز / زیارتی", "مسیر کوتاه و پرتکرار از تهران",
        "قم یکی از کوتاه‌ترین و پرتکرارترین مسیرهای بین‌شهری از تهران است.",
        ["زمان مسیر معمولاً قابل پیش‌بینی‌تر از مسیرهای کوهستانی است."],
        ["تهران", "کاشان", "اصفهان"]),
      ["شهرکرد"] = P("شهرکرد", "shahrekord", "چهارمحال و بختیاری", "مرتفع و گردشگری طبیعی",
        "شهرکرد مسیر مرتفع‌تری دارد و شرایط جوی در فصول سرد اهمیت بیشتری پیدا می‌کند.",
        ["قبل از حرکت وضعیت جاده را چک کنید."],
        ["تهران", "اصفهان"]),
      ["کرمانشاه"] = P("کرمانشاه", "kermanshah", "غرب ایران", "مسیر غرب و گردشگری مرزی استان",
        "کرمانشاه از مسیرهای مهم غرب کشور است و برای سفر کاری و خانوادگی از تهران استفاده می‌شود.",
        ["زمان مسیر طولانی است؛ استراحت و برنامه حرکت را در نظر بگیرید."],
        ["تهران", "همدان", "سنندج"]),
      ["سنندج"] = P("سنندج", "sanandaj", "کردستان", "غرب کوهستانی",
        "سنندج مسیر غرب کوهستانی است؛ زمان سفر به شرایط جاده و فصل وابسته است.",
        ["در زمستان حاشیه زمانی بیشتری بگذارید."],
        ["تهران", "همدان", "کرمانشاه"]),
      ["شیراز"] = P("شیراز", "shiraz", "جنوب / فارس", "گردشگری تاریخی جنوب",
        "شیراز مقصد کلاسیک گردشگری جنوب است و مسیر طولانی‌تری نسبت به مرکز دارد.",
        ["رزرو از چند روز قبل در نوروز و تعطیلات توصیه می‌شود."],
        ["تهران", "اصفهان", "یزد"]),
      ["گرگان"] = P("گرگان", "gorgan", "گلستان", "شرق شمال و جنگل",
        "گرگان اتصال تهران به گلستان و شرق شمال را پوشش می‌دهد.",
        ["در بارندگی‌های شمالی، زمان مسیر را بیشتر فرض کنید."],
        ["تهران", "ساری"]),
      ["مشهد"] = P("مشهد", "mashhad", "خراسان رضوی", "زیارتی و مسیر شرق",
        "مشهد یکی از پرتقاضاترین مقاصد ملی برای سفر زیارتی و خانوادگی است.",
        ["در ایام خاص مذهبی تقاضای سفر به‌شدت بالا می‌رود؛ زودتر رزرو کنید."],
        ["تهران", "ساری"]),
      ["یزد"] = P("یزد", "yazd", "مرکز / کویر", "گردشگری تاریخی و مسیر جنوبی–مرکزی",
        "یزد مسیر کویری–مرکزی دارد و برای گردشگری و سفر کاری استفاده می‌شود.",
        ["در تابستان، زمان حرکت را با دمای روز هماهنگ کنید."],
        ["تهران", "اصفهان", "شیراز"]),
      ["قزوین"] = P("قزوین", "qazvin", "نزدیک تهران", "توقفگاه و مسیر شمال‌غرب",
        "قزوین نزدیک تهران است و اغلب به‌عنوان مسیر کوتاه یا گره به سمت شمال و شمال‌غرب دیده می‌شود.",
        ["زمان مسیر معمولاً کوتاه‌تر از مقاصد دورافتاده‌تر است."],
        ["تهران", "رشت", "زنجان"]),
      ["کرج"] = P("کرج", "karaj", "البرز / همسایه تهران", "مسیر بسیار کوتاه و پرتکرار",
        "کرج از نزدیک‌ترین مقاصد به تهران است و برای جابه‌جایی روزمره و بین‌شهری کوتاه استفاده می‌شود.",
        ["ترافیک غربی تهران را در ساعت اوج در نظر بگیرید."],
        ["تهران", "قزوین"]),
    };

  private static readonly Lazy<IReadOnlyDictionary<string, CityProfile>> ByNameLazy = new(BuildMerged);

  private static IReadOnlyDictionary<string, CityProfile> ByName => ByNameLazy.Value;

  public static IReadOnlyList<CityProfile> All =>
    ByName.Values.OrderBy(c => c.NameFa, StringComparer.Ordinal).ToList();

  public static CityProfile? FindBySlug(string? slug)
  {
    if (string.IsNullOrWhiteSpace(slug)) return null;
    slug = slug.Trim().ToLowerInvariant();
    return ByName.Values.FirstOrDefault(c => c.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
  }

  public static CityProfile? FindByName(string? nameFa)
  {
    if (nameFa is null) return null;
    var key = SeoSlugHelper.StripCityLabel(nameFa);
    if (ByName.TryGetValue(key, out var p)) return p;
    // Always return a stub so RouteContent never renders empty city blocks
    if (key.Length == 0) return null;
    var slug = SeoSlugHelper.SlugifyCity(key, out _);
    return CityStubFactory.Create(key, slug);
  }

  /// <summary>ORS city id from generated catalog when present.</summary>
  public static int? FindCityId(string? nameFa)
  {
    var key = SeoSlugHelper.StripCityLabel(nameFa);
    if (key.Length == 0) return null;
    return CityIds.TryGetValue(key, out var id) ? id : null;
  }

  public static string? SlugOf(string nameFa) => FindByName(nameFa)?.Slug;

  private static readonly Lazy<IReadOnlyDictionary<string, int>> CityIdsLazy = new(LoadCityIds);

  private static IReadOnlyDictionary<string, int> CityIds => CityIdsLazy.Value;

  private static IReadOnlyDictionary<string, int> LoadCityIds()
  {
    var map = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var dto in LoadGeneratedCities())
    {
      var name = SeoSlugHelper.StripCityLabel(dto.NameFa);
      if (name.Length == 0 || dto.CityId is not int id || id <= 0) continue;
      map.TryAdd(name, id);
    }
    return map;
  }

  private static IReadOnlyDictionary<string, CityProfile> BuildMerged()
  {
    var map = new Dictionary<string, CityProfile>(StringComparer.Ordinal);

    // 1) Generated cities (stubs or hand overlay)
    foreach (var dto in LoadGeneratedCities())
    {
      var name = SeoSlugHelper.StripCityLabel(dto.NameFa);
      if (name.Length == 0) continue;
      var slug = string.IsNullOrWhiteSpace(dto.Slug)
        ? SeoSlugHelper.SlugifyCity(name, out _)
        : dto.Slug;
      map[name] = HandByName.TryGetValue(name, out var hand)
        ? hand with { Slug = slug }
        : CityStubFactory.Create(name, slug);
    }

    // 2) Ensure hand cities remain even if not yet in generated file
    foreach (var (name, profile) in HandByName)
    {
      if (!map.ContainsKey(name))
        map[name] = profile;
    }

    // 3) Cities referenced by routes but missing from cities JSON
    foreach (var route in RouteCatalog.All)
    {
      EnsureFromRoute(map, route.OriginFa);
      EnsureFromRoute(map, route.DestinationFa);
    }

    return map;
  }

  private static void EnsureFromRoute(Dictionary<string, CityProfile> map, string nameFa)
  {
    var name = SeoSlugHelper.StripCityLabel(nameFa);
    if (name.Length == 0 || map.ContainsKey(name)) return;
    if (HandByName.TryGetValue(name, out var hand))
    {
      map[name] = hand;
      return;
    }
    map[name] = CityStubFactory.Create(name, SeoSlugHelper.SlugifyCity(name, out _));
  }

  private static List<GeneratedCityDto> LoadGeneratedCities()
  {
    try
    {
      var path = SeoDataPaths.CitiesGeneratedPath;
      if (!File.Exists(path)) return [];
      using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
      if (!doc.RootElement.TryGetProperty("cities", out var cities) ||
          cities.ValueKind != System.Text.Json.JsonValueKind.Array)
        return [];

      var list = new List<GeneratedCityDto>();
      foreach (var el in cities.EnumerateArray())
      {
        var name = el.TryGetProperty("nameFa", out var n) ? n.GetString()
          : el.TryGetProperty("NameFa", out var n2) ? n2.GetString() : null;
        if (string.IsNullOrWhiteSpace(name)) continue;
        var slug = el.TryGetProperty("slug", out var s) ? s.GetString()
          : el.TryGetProperty("Slug", out var s2) ? s2.GetString() : null;
        int? id = null;
        if (el.TryGetProperty("cityId", out var idEl) && idEl.ValueKind == System.Text.Json.JsonValueKind.Number)
          id = idEl.GetInt32();
        else if (el.TryGetProperty("CityId", out var idEl2) && idEl2.ValueKind == System.Text.Json.JsonValueKind.Number)
          id = idEl2.GetInt32();
        list.Add(new GeneratedCityDto
        {
          NameFa = name!,
          Slug = slug ?? SeoSlugHelper.SlugifyCity(name, out _),
          CityId = id
        });
      }
      return list;
    }
    catch
    {
      return [];
    }
  }

  private static CityProfile P(
    string name, string slug, string region, string role, string blurb, string[] tips, string[] nearby) =>
    new(name, slug, region, role, blurb, tips, nearby);
}
