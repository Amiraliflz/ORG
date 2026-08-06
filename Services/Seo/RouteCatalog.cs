using System.Text.Json;

namespace Application.Services.Seo;

/// <summary>Programmatic SEO route catalog for /routes/{slug} — loaded from generated JSON + seed fallback.</summary>
public static class RouteCatalog
{
  public sealed record RoutePage(
    string OriginFa,
    string DestinationFa,
    string Slug,
    int? TravelTimeMins,
    bool IsPrimary);

  /// <summary>Tehran hub pairs used only if generated JSON is missing.</summary>
  private static readonly (string Origin, string Dest, int? Mins)[] SeedPairs =
  [
    ("تهران", "اصفهان", 270),
    ("تهران", "رشت", 240),
    ("تهران", "لاهیجان", 270),
    ("تهران", "چالوس", 160),
    ("تهران", "نوشهر", 170),
    ("تهران", "رامسر", 265),
    ("تهران", "ساری", 280),
    ("تهران", "کاشان", 164),
    ("تهران", "همدان", 220),
    ("تهران", "زنجان", 220),
    ("تهران", "اردبیل", 420),
    ("تهران", "تبریز", 405),
    ("تهران", "قم", 105),
    ("تهران", "شهرکرد", 360),
    ("تهران", "کرمانشاه", 380),
    ("تهران", "سنندج", 360),
    ("تهران", "شیراز", 540),
    ("تهران", "گرگان", 300),
    ("تهران", "مشهد", 600),
    ("تهران", "یزد", 420),
    ("تهران", "قزوین", 110),
    ("تهران", "کرج", 50),
  ];

  private static readonly Lazy<IReadOnlyList<RoutePage>> AllLazy = new(LoadAll);

  public static IReadOnlyList<RoutePage> All => AllLazy.Value;

  public static RoutePage? FindBySlug(string? slug)
  {
    if (string.IsNullOrWhiteSpace(slug)) return null;
    slug = slug.Trim().ToLowerInvariant();
    return All.FirstOrDefault(r => r.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
  }

  /// <summary>Match a live search OD pair to a catalog route (ignores terminal suffixes).</summary>
  public static RoutePage? FindByCities(string? originFa, string? destinationFa)
  {
    var o = SeoSlugHelper.StripCityLabel(originFa);
    var d = SeoSlugHelper.StripCityLabel(destinationFa);
    if (o.Length == 0 || d.Length == 0) return null;

    var exact = All.FirstOrDefault(r => r.OriginFa == o && r.DestinationFa == d);
    if (exact is not null) return exact;

    return FindBySlug(SeoSlugHelper.RouteSlug(o, d));
  }

  public static string MakeSlug(string originFa, string destinationFa) =>
    SeoSlugHelper.RouteSlug(originFa, destinationFa);

  public static string Title(RoutePage route) =>
    $"سواری {route.OriginFa} به {route.DestinationFa}";

  public static string MetaDescription(RoutePage route) =>
    RouteContent.For(route).MetaDescription;

  public static string Intro(RoutePage route) =>
    RouteContent.For(route).Intro;

  public static RoutePage? ReverseOf(RoutePage route) =>
    All.FirstOrDefault(r => r.OriginFa == route.DestinationFa && r.DestinationFa == route.OriginFa);

  public static IReadOnlyList<RoutePage> Related(RoutePage route, int take = 8) =>
    All
      .Where(r => r.Slug != route.Slug &&
                 (r.OriginFa == route.OriginFa ||
                  r.DestinationFa == route.DestinationFa ||
                  r.OriginFa == route.DestinationFa ||
                  r.DestinationFa == route.OriginFa))
      .OrderByDescending(r => r.IsPrimary)
      .ThenBy(r => r.OriginFa, StringComparer.Ordinal)
      .Take(take)
      .ToList();

  public static IReadOnlyList<RoutePage> FromCity(string cityFa) =>
    All.Where(r => r.OriginFa == cityFa).OrderByDescending(r => r.IsPrimary).ToList();

  public static IReadOnlyList<RoutePage> ToCity(string cityFa) =>
    All.Where(r => r.DestinationFa == cityFa).OrderByDescending(r => r.IsPrimary).ToList();

  /// <summary>Routes grouped by origin for hub index pages.</summary>
  public static IReadOnlyList<IGrouping<string, RoutePage>> GroupedByOrigin() =>
    All
      .GroupBy(r => r.OriginFa)
      .OrderBy(g => g.Key == "تهران" ? 0 : 1)
      .ThenBy(g => g.Key, StringComparer.Ordinal)
      .ToList();

  public static IEnumerable<string> SitemapPaths()
  {
    yield return "/";
    yield return "/routes";
    yield return "/cities";
    yield return "/Home/ContactUs";
    yield return "/Home/FAQ";
    yield return "/Home/TravelPolicy";
    yield return "/Home/Privacy";
    foreach (var c in CityCatalog.All)
      yield return $"/cities/{c.Slug}";
    foreach (var r in All)
      yield return $"/routes/{r.Slug}";
  }

  private static IReadOnlyList<RoutePage> LoadAll()
  {
    var fromFile = TryLoadGenerated();
    if (fromFile is { Count: > 0 })
      return fromFile;

    return BuildSeedFallback();
  }

  private static IReadOnlyList<RoutePage>? TryLoadGenerated()
  {
    try
    {
      var path = SeoDataPaths.RoutesGeneratedPath;
      if (!File.Exists(path)) return null;
      using var doc = JsonDocument.Parse(File.ReadAllText(path));
      if (!doc.RootElement.TryGetProperty("routes", out var routesEl) ||
          routesEl.ValueKind != JsonValueKind.Array)
        return null;

      var list = new List<RoutePage>();
      var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var el in routesEl.EnumerateArray())
      {
        var origin = el.TryGetProperty("originFa", out var o) ? o.GetString()
          : el.TryGetProperty("OriginFa", out var o2) ? o2.GetString() : null;
        var dest = el.TryGetProperty("destinationFa", out var d) ? d.GetString()
          : el.TryGetProperty("DestinationFa", out var d2) ? d2.GetString() : null;
        if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(dest)) continue;

        origin = SeoSlugHelper.StripCityLabel(origin);
        dest = SeoSlugHelper.StripCityLabel(dest);
        var slug = el.TryGetProperty("slug", out var s) ? s.GetString()
          : el.TryGetProperty("Slug", out var s2) ? s2.GetString() : null;
        if (string.IsNullOrWhiteSpace(slug))
          slug = SeoSlugHelper.RouteSlug(origin, dest);

        if (!seen.Add(slug!)) continue;

        int? mins = null;
        if (el.TryGetProperty("travelTimeMins", out var t) && t.ValueKind == JsonValueKind.Number)
          mins = t.GetInt32();
        else if (el.TryGetProperty("TravelTimeMins", out var t2) && t2.ValueKind == JsonValueKind.Number)
          mins = t2.GetInt32();

        var primary = true;
        if (el.TryGetProperty("isPrimary", out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False)
          primary = p.GetBoolean();
        else if (el.TryGetProperty("IsPrimary", out var p2) && p2.ValueKind is JsonValueKind.True or JsonValueKind.False)
          primary = p2.GetBoolean();

        list.Add(new RoutePage(origin, dest, slug!, mins, primary));
      }

      return list.Count > 0 ? list : null;
    }
    catch
    {
      return null;
    }
  }

  private static IReadOnlyList<RoutePage> BuildSeedFallback()
  {
    var list = new List<RoutePage>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    void Add(string origin, string dest, int? mins, bool primary)
    {
      var slug = SeoSlugHelper.RouteSlug(origin, dest);
      if (!seen.Add(slug)) return;
      list.Add(new RoutePage(origin, dest, slug, mins, primary));
    }

    foreach (var (o, d, m) in SeedPairs)
    {
      Add(o, d, m, primary: true);
      Add(d, o, m, primary: false);
    }

    return list;
  }
}
