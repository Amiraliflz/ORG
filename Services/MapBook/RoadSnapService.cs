using Application.Services.Neshan;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Application.Services.MapBook;

/// <summary>
/// Snaps a map pin to the nearest drivable road inside Iran.
/// Picks the closest road point (OSRM nearest + Neshan micro-routes), not a distant arterial.
/// </summary>
public sealed class RoadSnapService
{
  private readonly NeshanApiClient _neshan;
  private readonly IHttpClientFactory _httpFactory;
  private readonly IConfiguration _configuration;
  private readonly ILogger<RoadSnapService> _logger;
  private readonly MapBookGeoCache _geoCache;

  private static readonly TimeSpan SnapCacheTtl = TimeSpan.FromMinutes(15);

  // Short bearings (~150–450 m) to find the closest local road, not a far main highway.
  private static readonly (double DLat, double DLng)[] SnapBearings =
  {
    (0.0015, 0),
    (0, 0.0015),
    (-0.0015, 0),
    (0, -0.0015),
    (0.0011, 0.0011),
    (0.0011, -0.0011),
    (-0.0011, 0.0011),
    (-0.0011, -0.0011),
    (0.003, 0),
    (0, 0.003),
    (-0.003, 0),
    (0, -0.003)
  };

  public RoadSnapService(
    NeshanApiClient neshan,
    IHttpClientFactory httpFactory,
    IConfiguration configuration,
    ILogger<RoadSnapService> logger,
    MapBookGeoCache geoCache)
  {
    _neshan = neshan;
    _httpFactory = httpFactory;
    _configuration = configuration;
    _logger = logger;
    _geoCache = geoCache;
  }

  public Task<RoadSnapResult?> SnapAsync(double lat, double lng, CancellationToken ct = default)
  {
    if (!RoadSnapMath.IsValidIran(lat, lng)) return Task.FromResult<RoadSnapResult?>(null);

    var key = MapBookGeoCache.NearestKey(lat, lng);
    return _geoCache.GetOrCreateAsync(key, SnapCacheTtl, () => SnapUncachedAsync(lat, lng, ct));
  }

  private async Task<RoadSnapResult?> SnapUncachedAsync(double lat, double lng, CancellationToken ct)
  {
    var neshanTask = SnapViaNeshanAsync(lat, lng, ct);
    var osrmTask = SnapViaOsrmNearestAsync(lat, lng, ct);
    await Task.WhenAll(neshanTask, osrmTask);

    var neshan = neshanTask.Result;
    var osrm = osrmTask.Result;
    if (neshan == null) return osrm;
    if (osrm == null) return neshan;
    return neshan.DistanceMeters <= osrm.DistanceMeters ? neshan : osrm;
  }

  public async Task<RoadSnapResult?> SnapViaNeshanAsync(double lat, double lng, CancellationToken ct)
  {
    if (!_neshan.IsConfigured) return null;

    var cosLat = Math.Cos(lat * Math.PI / 180);
    var tasks = SnapBearings.Select(bearing => TryNeshanBearingAsync(lat, lng, cosLat, bearing, ct));
    var results = await Task.WhenAll(tasks);

    RoadSnapResult? best = null;
    var bestDist = double.MaxValue;
    foreach (var snap in results)
    {
      if (snap == null || snap.DistanceMeters >= bestDist) continue;
      bestDist = snap.DistanceMeters;
      best = snap;
    }

    return best;
  }

  private async Task<RoadSnapResult?> TryNeshanBearingAsync(
    double lat,
    double lng,
    double cosLat,
    (double DLat, double DLng) bearing,
    CancellationToken ct)
  {
    var (dLat, dLng) = bearing;
    var destLat = lat + dLat;
    var destLng = lng + (dLng / Math.Max(cosLat, 0.2));
    if (!RoadSnapMath.IsValidIran(destLat, destLng)) return null;

    try
    {
      var route = await _neshan.GetDrivingRouteAsync(lat, lng, destLat, destLng, ct);
      if (route == null || route.Coordinates.Count < 2) return null;

      var near = RoadSnapMath.NearestOnPolyline(lat, lng, route.Coordinates);
      if (near == null) return null;

      return RoadSnapMath.AcceptSnap(near.Value.Lat, near.Value.Lng, near.Value.DistM, "neshan");
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Neshan road snap bearing failed for {Lat},{Lng}", lat, lng);
      return null;
    }
  }

  public async Task<RoadSnapResult?> SnapViaOsrmNearestAsync(double lat, double lng, CancellationToken ct)
  {
    var localBase = _configuration["Osrm:BaseUrl"]?.TrimEnd('/');
    var urls = new List<string>();
    if (!string.IsNullOrWhiteSpace(localBase))
      urls.Add(RoadSnapMath.BuildOsrmNearestUrl(localBase, lat, lng));
    urls.Add(RoadSnapMath.BuildOsrmNearestUrl("https://router.project-osrm.org", lat, lng));

    var client = _httpFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(5);

    foreach (var url in urls)
    {
      try
      {
        using var res = await client.GetAsync(url, ct);
        if (!res.IsSuccessStatusCode) continue;
        var json = await res.Content.ReadAsStringAsync(ct);
        var snap = RoadSnapMath.ParseOsrmNearestJson(json, lat, lng);
        if (snap != null) return snap;
      }
      catch (Exception ex)
      {
        _logger.LogDebug(ex, "OSRM nearest failed: {Url}", url);
      }
    }

    return null;
  }
}
