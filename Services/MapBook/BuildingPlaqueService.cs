using System.Collections.Concurrent;
using Application.Services.Neshan;

namespace Application.Services.MapBook;

/// <summary>
/// Fetches real building plaque (پلاک) coordinates via Neshan Geocoding Plus inside a map viewport.
/// Caches by street so panning along the same road does not re-hit Neshan.
/// </summary>
public sealed class BuildingPlaqueService
{
  private readonly NeshanApiClient _neshan;
  private readonly ILogger<BuildingPlaqueService> _logger;

  private static readonly ConcurrentDictionary<string, (DateTime At, string City, string Street)> ReverseCache = new();
  private static readonly ConcurrentDictionary<string, (DateTime At, IReadOnlyList<BuildingPlaque> Items)> StreetCache = new();
  private static readonly TimeSpan ReverseCacheTtl = TimeSpan.FromHours(2);
  private static readonly TimeSpan StreetCacheTtl = TimeSpan.FromHours(24);
  private const int StreetFetchLimit = 16;

  public BuildingPlaqueService(NeshanApiClient neshan, ILogger<BuildingPlaqueService> logger)
  {
    _neshan = neshan;
    _logger = logger;
  }

  public async Task<BuildingPlaqueResponse?> GetPlaquesAsync(
    double centerLat,
    double centerLng,
    double minLat,
    double minLng,
    double maxLat,
    double maxLng,
    int maxPlaques,
    CancellationToken ct = default)
  {
    if (!RoadSnapMath.IsValidIran(centerLat, centerLng)) return null;

    var streetInfo = await ResolveStreetAsync(centerLat, centerLng, ct);
    if (streetInfo == null) return null;

    var (city, street) = streetInfo.Value;
    var streetKey = StreetCacheKey(city, street);
    var limit = Math.Clamp(maxPlaques, 4, StreetFetchLimit);

    IReadOnlyList<BuildingPlaque> allPlaques;
    if (TryGetStreetCache(streetKey, out allPlaques))
    {
      _logger.LogDebug("Building plaques cache hit for {Street}, {City}", street, city);
    }
    else
    {
      allPlaques = await FetchPlaquesFromNeshanAsync(
        city, street, minLat, minLng, maxLat, maxLng, ct);
      if (allPlaques.Count == 0) return null;
      StreetCache[streetKey] = (DateTime.UtcNow, allPlaques);
    }

    var inView = FilterToView(allPlaques, minLat, minLng, maxLat, maxLng, limit);
    if (inView.Count == 0) return null;

    return new BuildingPlaqueResponse(city, street, inView);
  }

  private async Task<(string City, string Street)?> ResolveStreetAsync(
    double centerLat, double centerLng, CancellationToken ct)
  {
    var reverseKey = ReverseCacheKey(centerLat, centerLng);
    if (ReverseCache.TryGetValue(reverseKey, out var cached) &&
        DateTime.UtcNow - cached.At < ReverseCacheTtl &&
        !string.IsNullOrWhiteSpace(cached.City) &&
        !string.IsNullOrWhiteSpace(cached.Street))
    {
      return (cached.City, cached.Street);
    }

    var rev = await _neshan.ReverseAsync(centerLat, centerLng, ct);
    var city = rev?.City?.Trim();
    var street = rev?.Route?.Trim();
    if (string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(street))
      return null;

    ReverseCache[reverseKey] = (DateTime.UtcNow, city, street);
    return (city, street);
  }

  private async Task<IReadOnlyList<BuildingPlaque>> FetchPlaquesFromNeshanAsync(
    string city,
    string street,
    double minLat,
    double minLng,
    double maxLat,
    double maxLng,
    CancellationToken ct)
  {
    var plaques = new List<BuildingPlaque>(StreetFetchLimit);

    // Wide bbox on first fetch — cache the whole street segment, filter per viewport later.
    var padLat = Math.Max((maxLat - minLat) * 0.85, 0.004);
    var padLng = Math.Max((maxLng - minLng) * 0.85, 0.004);
    var extMinLat = minLat - padLat;
    var extMaxLat = maxLat + padLat;
    var extMinLng = minLng - padLng;
    var extMaxLng = maxLng + padLng;

    for (var n = 1; n <= StreetFetchLimit; n++)
    {
      ct.ThrowIfCancellationRequested();
      try
      {
        var hit = await _neshan.GeocodePlusPlaqueAsync(
          city, street, n, extMinLat, extMinLng, extMaxLat, extMaxLng, ct);
        if (hit is not { } p) continue;

        plaques.Add(new BuildingPlaque(n, p.Lat, p.Lng));
      }
      catch (Exception ex)
      {
        _logger.LogDebug(ex, "Plaque geocode failed for {Street} پلاک {N}", street, n);
      }

      if (n < StreetFetchLimit)
        await Task.Delay(400, ct);
    }

    return plaques.Count == 0 ? Array.Empty<BuildingPlaque>() : DedupePlaques(plaques);
  }

  private static bool TryGetStreetCache(string streetKey, out IReadOnlyList<BuildingPlaque> items)
  {
    items = Array.Empty<BuildingPlaque>();
    if (!StreetCache.TryGetValue(streetKey, out var cached)) return false;
    if (DateTime.UtcNow - cached.At >= StreetCacheTtl) return false;
    items = cached.Items;
    return items.Count > 0;
  }

  private static IReadOnlyList<BuildingPlaque> FilterToView(
    IReadOnlyList<BuildingPlaque> plaques,
    double minLat, double minLng, double maxLat, double maxLng,
    int limit)
  {
    var outList = new List<BuildingPlaque>(Math.Min(limit, plaques.Count));
    foreach (var p in plaques)
    {
      if (!IsInView(p.Lat, p.Lng, minLat, minLng, maxLat, maxLng)) continue;
      outList.Add(p);
      if (outList.Count >= limit) break;
    }
    return outList;
  }

  private static string ReverseCacheKey(double lat, double lng) =>
    $"{Math.Round(lat, 3):F3}|{Math.Round(lng, 3):F3}";

  private static string StreetCacheKey(string city, string street) =>
    $"{city.Trim().ToLowerInvariant()}|{street.Trim().ToLowerInvariant()}";

  private static IReadOnlyList<BuildingPlaque> DedupePlaques(List<BuildingPlaque> plaques)
  {
    var seen = new HashSet<string>();
    var outList = new List<BuildingPlaque>();
    foreach (var p in plaques)
    {
      var key = $"{Math.Round(p.Lat, 4)}:{Math.Round(p.Lng, 4)}";
      if (!seen.Add(key)) continue;
      outList.Add(p);
    }
    return outList;
  }

  private static bool IsInView(
    double lat, double lng,
    double minLat, double minLng, double maxLat, double maxLng)
  {
    const double pad = 0.006;
    return lat >= minLat - pad && lat <= maxLat + pad &&
           lng >= minLng - pad && lng <= maxLng + pad;
  }
}

public sealed record BuildingPlaque(int Number, double Lat, double Lng);

public sealed record BuildingPlaqueResponse(
  string City,
  string Street,
  IReadOnlyList<BuildingPlaque> Plaques);
