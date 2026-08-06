using System.Net.Http.Json;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Application.Services.Seo;

/// <summary>
/// Fetches ORS AvailableDirections and writes generated SEO route/city JSON under wwwroot/json/Seo/.
/// </summary>
public static class SeoCatalogSync
{
  private static readonly HashSet<string> HubOrigins = new(StringComparer.Ordinal)
  {
    "تهران", "اصفهان", "مشهد", "شیراز", "تبریز", "کرج"
  };

  public sealed record SyncResult(
    int RouteCount,
    int CityCount,
    IReadOnlyList<string> UnresolvedSlugs,
    string RoutesPath,
    string CitiesPath);

  public static async Task<SyncResult> RunAsync(
    string? apiBaseUrl = null,
    string? webRoot = null,
    CancellationToken ct = default)
  {
    if (!string.IsNullOrWhiteSpace(webRoot))
      SeoDataPaths.Configure(webRoot);

    var baseUrl = (apiBaseUrl ?? "https://ors.shoofer.taxi").TrimEnd('/');
    var directions = await FetchAvailableDirectionsAsync(baseUrl, ct);
    var travelTimes = LoadTravelTimesFromDirectionsJson();

    var unresolved = new List<string>();
    var routesBySlug = new Dictionary<string, GeneratedRouteDto>(StringComparer.OrdinalIgnoreCase);
    var citiesByName = new Dictionary<string, GeneratedCityDto>(StringComparer.Ordinal);

    void EnsureCity(string nameFa, int? id)
    {
      nameFa = SeoSlugHelper.StripCityLabel(nameFa);
      if (nameFa.Length == 0) return;
      var slug = SeoSlugHelper.SlugifyCity(nameFa, out var fallback);
      if (fallback && !unresolved.Contains(nameFa))
        unresolved.Add(nameFa);

      if (citiesByName.TryGetValue(nameFa, out var existing))
      {
        if (existing.CityId is null && id is not null)
          existing.CityId = id;
        return;
      }

      citiesByName[nameFa] = new GeneratedCityDto
      {
        NameFa = nameFa,
        Slug = slug,
        CityId = id
      };
    }

    void AddRoute(string originFa, string destFa, int? originId, int? destId, int? mins, bool isPrimary)
    {
      originFa = SeoSlugHelper.StripCityLabel(originFa);
      destFa = SeoSlugHelper.StripCityLabel(destFa);
      if (originFa.Length == 0 || destFa.Length == 0 || originFa == destFa) return;

      EnsureCity(originFa, originId);
      EnsureCity(destFa, destId);

      var slug = SeoSlugHelper.RouteSlug(originFa, destFa);
      if (routesBySlug.ContainsKey(slug)) return;

      if (mins is null &&
          travelTimes.TryGetValue(($"{originFa}|{destFa}"), out var t))
        mins = t;

      routesBySlug[slug] = new GeneratedRouteDto
      {
        OriginFa = originFa,
        DestinationFa = destFa,
        Slug = slug,
        OriginId = originId,
        DestinationId = destId,
        TravelTimeMins = mins,
        IsPrimary = isPrimary
      };
    }

    foreach (var d in directions)
    {
      var o = SeoSlugHelper.StripCityLabel(d.Cityone);
      var dest = SeoSlugHelper.StripCityLabel(d.Citytwo);
      var primary = IsPrimaryDirection(o, dest);
      AddRoute(o, dest, d.CityoneId, d.CitytwoId, null, primary);
    }

    // Synthesize reverse when missing
    foreach (var r in routesBySlug.Values.ToList())
    {
      var revSlug = SeoSlugHelper.RouteSlug(r.DestinationFa, r.OriginFa);
      if (routesBySlug.ContainsKey(revSlug)) continue;
      AddRoute(
        r.DestinationFa,
        r.OriginFa,
        r.DestinationId,
        r.OriginId,
        r.TravelTimeMins,
        isPrimary: false);
    }

    // Resolve slug collisions across cities (rare after transliteration)
    ResolveCitySlugCollisions(citiesByName);

    var outDir = Path.Combine(SeoDataPaths.WebRoot, "json", "Seo");
    Directory.CreateDirectory(outDir);

    var now = DateTime.UtcNow.ToString("o");
    var routeFile = new
    {
      generatedAtUtc = now,
      source = $"{baseUrl}/Directions/getAvailableDirections",
      routes = routesBySlug.Values
        .OrderByDescending(r => r.IsPrimary)
        .ThenBy(r => r.OriginFa, StringComparer.Ordinal)
        .ThenBy(r => r.DestinationFa, StringComparer.Ordinal)
        .ToList(),
      unresolvedSlugs = unresolved.OrderBy(x => x, StringComparer.Ordinal).ToList()
    };
    var cityFile = new
    {
      generatedAtUtc = now,
      source = $"{baseUrl}/Directions/getAvailableDirections",
      cities = citiesByName.Values
        .OrderBy(c => c.NameFa, StringComparer.Ordinal)
        .ToList()
    };

    await File.WriteAllTextAsync(
      SeoDataPaths.RoutesGeneratedPath,
      JsonSerializer.Serialize(routeFile, SeoDataPaths.JsonOptions),
      ct);
    await File.WriteAllTextAsync(
      SeoDataPaths.CitiesGeneratedPath,
      JsonSerializer.Serialize(cityFile, SeoDataPaths.JsonOptions),
      ct);

    // Combined snapshot for debugging / ops
    var catalog = new GeneratedSeoCatalogDto
    {
      GeneratedAtUtc = now,
      Source = routeFile.source,
      Routes = routeFile.routes,
      Cities = cityFile.cities,
      UnresolvedSlugs = routeFile.unresolvedSlugs
    };
    await File.WriteAllTextAsync(
      SeoDataPaths.CatalogGeneratedPath,
      JsonSerializer.Serialize(catalog, SeoDataPaths.JsonOptions),
      ct);

    return new SyncResult(
      routeFile.routes.Count,
      cityFile.cities.Count,
      routeFile.unresolvedSlugs,
      SeoDataPaths.RoutesGeneratedPath,
      SeoDataPaths.CitiesGeneratedPath);
  }

  /// <summary>Bootstrap from Directions.json when ORS is unreachable (dev fallback).</summary>
  public static async Task<SyncResult> RunFromDirectionsJsonAsync(
    string? webRoot = null,
    CancellationToken ct = default)
  {
    if (!string.IsNullOrWhiteSpace(webRoot))
      SeoDataPaths.Configure(webRoot);

    var path = SeoDataPaths.DirectionsJsonPath;
    if (!File.Exists(path))
      throw new FileNotFoundException("Directions.json not found", path);

    var json = await File.ReadAllTextAsync(path, ct);
    var node = JsonNode.Parse(json) as JsonArray
      ?? throw new InvalidOperationException("Directions.json is not an array");

    var fake = new List<(string, string, int?, int?)>();
    foreach (var item in node)
    {
      if (item is not JsonObject obj) continue;
      var c1 = obj["Cityone"]?.GetValue<string>();
      var c2 = obj["Citytwo"]?.GetValue<string>();
      if (string.IsNullOrWhiteSpace(c1) || string.IsNullOrWhiteSpace(c2)) continue;
      fake.Add((c1!, c2!, null, null));
    }

    // Write a temporary in-memory sync by reusing travel times + same writer via public API shape
    // Call ORS-shaped path: synthesize AvaiableDirection list through Fetch bypass
    return await RunWithPairsAsync(
      fake.Select(f => (f.Item1, f.Item2, f.Item3, f.Item4)).ToList(),
      sourceLabel: $"file:{path}",
      ct);
  }

  private static async Task<SyncResult> RunWithPairsAsync(
    List<(string O, string D, int? Oid, int? Did)> pairs,
    string sourceLabel,
    CancellationToken ct)
  {
    var travelTimes = LoadTravelTimesFromDirectionsJson();
    var unresolved = new List<string>();
    var routesBySlug = new Dictionary<string, GeneratedRouteDto>(StringComparer.OrdinalIgnoreCase);
    var citiesByName = new Dictionary<string, GeneratedCityDto>(StringComparer.Ordinal);

    void EnsureCity(string nameFa, int? id)
    {
      nameFa = SeoSlugHelper.StripCityLabel(nameFa);
      if (nameFa.Length == 0) return;
      var slug = SeoSlugHelper.SlugifyCity(nameFa, out var fallback);
      if (fallback && !unresolved.Contains(nameFa)) unresolved.Add(nameFa);
      if (citiesByName.TryGetValue(nameFa, out var existing))
      {
        if (existing.CityId is null && id is not null) existing.CityId = id;
        return;
      }
      citiesByName[nameFa] = new GeneratedCityDto { NameFa = nameFa, Slug = slug, CityId = id };
    }

    void AddRoute(string originFa, string destFa, int? originId, int? destId, bool isPrimary)
    {
      originFa = SeoSlugHelper.StripCityLabel(originFa);
      destFa = SeoSlugHelper.StripCityLabel(destFa);
      if (originFa.Length == 0 || destFa.Length == 0 || originFa == destFa) return;
      EnsureCity(originFa, originId);
      EnsureCity(destFa, destId);
      var slug = SeoSlugHelper.RouteSlug(originFa, destFa);
      if (routesBySlug.ContainsKey(slug)) return;
      travelTimes.TryGetValue($"{originFa}|{destFa}", out var mins);
      if (mins is null)
        travelTimes.TryGetValue($"{destFa}|{originFa}", out mins);
      routesBySlug[slug] = new GeneratedRouteDto
      {
        OriginFa = originFa,
        DestinationFa = destFa,
        Slug = slug,
        OriginId = originId,
        DestinationId = destId,
        TravelTimeMins = mins,
        IsPrimary = isPrimary
      };
    }

    foreach (var (o, d, oid, did) in pairs)
      AddRoute(o, d, oid, did, IsPrimaryDirection(o, d));

    foreach (var r in routesBySlug.Values.ToList())
    {
      var rev = SeoSlugHelper.RouteSlug(r.DestinationFa, r.OriginFa);
      if (routesBySlug.ContainsKey(rev)) continue;
      AddRoute(r.DestinationFa, r.OriginFa, r.DestinationId, r.OriginId, false);
    }

    ResolveCitySlugCollisions(citiesByName);

    var outDir = Path.Combine(SeoDataPaths.WebRoot, "json", "Seo");
    Directory.CreateDirectory(outDir);
    var now = DateTime.UtcNow.ToString("o");
    var routeList = routesBySlug.Values
      .OrderByDescending(r => r.IsPrimary)
      .ThenBy(r => r.OriginFa, StringComparer.Ordinal)
      .ThenBy(r => r.DestinationFa, StringComparer.Ordinal)
      .ToList();
    var cityList = citiesByName.Values.OrderBy(c => c.NameFa, StringComparer.Ordinal).ToList();

    var routeFile = new
    {
      generatedAtUtc = now,
      source = sourceLabel,
      routes = routeList,
      unresolvedSlugs = unresolved.OrderBy(x => x, StringComparer.Ordinal).ToList()
    };
    var cityFile = new { generatedAtUtc = now, source = sourceLabel, cities = cityList };

    await File.WriteAllTextAsync(
      SeoDataPaths.RoutesGeneratedPath,
      JsonSerializer.Serialize(routeFile, SeoDataPaths.JsonOptions),
      ct);
    await File.WriteAllTextAsync(
      SeoDataPaths.CitiesGeneratedPath,
      JsonSerializer.Serialize(cityFile, SeoDataPaths.JsonOptions),
      ct);

    return new SyncResult(
      routeList.Count,
      cityList.Count,
      routeFile.unresolvedSlugs,
      SeoDataPaths.RoutesGeneratedPath,
      SeoDataPaths.CitiesGeneratedPath);
  }

  private static bool IsPrimaryDirection(string originFa, string destFa)
  {
    originFa = SeoSlugHelper.StripCityLabel(originFa);
    destFa = SeoSlugHelper.StripCityLabel(destFa);
    if (originFa == "تهران") return true;
    if (destFa == "تهران") return false;
    if (HubOrigins.Contains(originFa) && !HubOrigins.Contains(destFa)) return true;
    if (HubOrigins.Contains(destFa) && !HubOrigins.Contains(originFa)) return false;
    return string.CompareOrdinal(originFa, destFa) <= 0;
  }

  private static void ResolveCitySlugCollisions(Dictionary<string, GeneratedCityDto> citiesByName)
  {
    var bySlug = citiesByName.Values.GroupBy(c => c.Slug, StringComparer.OrdinalIgnoreCase);
    foreach (var g in bySlug.Where(x => x.Count() > 1))
    {
      var i = 0;
      foreach (var c in g.OrderBy(x => x.NameFa, StringComparer.Ordinal))
      {
        if (i++ == 0) continue;
        c.Slug = $"{c.Slug}-{i}";
      }
    }
  }

  private static Dictionary<string, int?> LoadTravelTimesFromDirectionsJson()
  {
    var map = new Dictionary<string, int?>(StringComparer.Ordinal);
    var path = SeoDataPaths.DirectionsJsonPath;
    if (!File.Exists(path)) return map;
    try
    {
      var json = File.ReadAllText(path);
      if (JsonNode.Parse(json) is not JsonArray arr) return map;
      foreach (var item in arr)
      {
        if (item is not JsonObject obj) continue;
        var c1 = SeoSlugHelper.StripCityLabel(obj["Cityone"]?.GetValue<string>());
        var c2 = SeoSlugHelper.StripCityLabel(obj["Citytwo"]?.GetValue<string>());
        var mins = obj["TravelTime_mins"]?.GetValue<int?>();
        if (c1.Length == 0 || c2.Length == 0 || mins is null) continue;
        map[$"{c1}|{c2}"] = mins;
        map.TryAdd($"{c2}|{c1}", mins);
      }
    }
    catch
    {
      // ignore corrupt directions file
    }
    return map;
  }

  private static async Task<List<(string Cityone, string Citytwo, int? CityoneId, int? CitytwoId)>> FetchAvailableDirectionsAsync(
    string baseUrl,
    CancellationToken ct)
  {
    using var handler = new HttpClientHandler
    {
      UseCookies = false,
      UseProxy = false,
      Proxy = null,
      SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
    };
    using var client = new HttpClient(handler)
    {
      BaseAddress = new Uri(baseUrl),
      Timeout = TimeSpan.FromSeconds(60)
    };

    using var response = await client.GetAsync("/Directions/getAvailableDirections", ct);
    var body = await response.Content.ReadAsStringAsync(ct);
    if (!response.IsSuccessStatusCode)
      throw new InvalidOperationException(
        $"ORS AvailableDirections failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

    var node = JsonNode.Parse(body);
    var list = new List<(string, string, int?, int?)>();
    if (node is not JsonArray arr)
      throw new InvalidOperationException("AvailableDirections response is not a JSON array");

    foreach (var item in arr)
    {
      if (item is not JsonObject obj) continue;

      // Nested shape (current ORS): { origin: { city_name, city_id }, destination: { ... } }
      string? c1 = null;
      string? c2 = null;
      int? id1 = null;
      int? id2 = null;

      if (obj["origin"] is JsonObject originObj)
      {
        c1 = TryGetString(originObj, "city_name", "cityName", "name", "Cityone", "city");
        id1 = TryGetInt(originObj, "city_id", "cityId", "id");
      }
      if (obj["destination"] is JsonObject destObj)
      {
        c2 = TryGetString(destObj, "city_name", "cityName", "name", "Citytwo", "city");
        id2 = TryGetInt(destObj, "city_id", "cityId", "id");
      }

      // Flat legacy shape
      c1 ??= TryGetString(obj,
        "Cityone", "cityone", "CityOne", "cityOne", "origin", "originCity", "fromCity", "from");
      c2 ??= TryGetString(obj,
        "Citytwo", "citytwo", "CityTwo", "cityTwo", "destination", "destinationCity", "toCity", "to");
      id1 ??= TryGetInt(obj, "CityoneId", "cityOneId", "CityOneId", "originCityId", "originId");
      id2 ??= TryGetInt(obj, "CitytwoId", "cityTwoId", "CityTwoId", "destinationCityId", "destinationId");

      if (string.IsNullOrWhiteSpace(c1) || string.IsNullOrWhiteSpace(c2)) continue;
      list.Add((c1!.Trim(), c2!.Trim(), id1, id2));
    }

    if (list.Count == 0)
      throw new InvalidOperationException("AvailableDirections returned zero usable OD pairs");

    return list;
  }

  private static string? TryGetString(JsonObject obj, params string[] keys)
  {
    foreach (var k in keys)
    {
      if (obj.TryGetPropertyValue(k, out var n) && n is JsonValue v)
      {
        try { return v.GetValue<string>(); }
        catch { /* ignore */ }
      }
    }
    return null;
  }

  private static int? TryGetInt(JsonObject obj, params string[] keys)
  {
    foreach (var k in keys)
    {
      if (!obj.TryGetPropertyValue(k, out var n) || n is null) continue;
      try
      {
        if (n is JsonValue v)
        {
          if (v.TryGetValue<int>(out var i)) return i;
          if (v.TryGetValue<string>(out var s) && int.TryParse(s, out var p)) return p;
        }
      }
      catch { /* ignore */ }
    }
    return null;
  }
}
