using Microsoft.Extensions.Caching.Memory;

namespace Application.Services.MapBook;

/// <summary>
/// In-memory cache for MapBook geocoding / routing / snap responses.
/// Reduces repeated Neshan and OSRM calls for nearby coordinates.
/// </summary>
public sealed class MapBookGeoCache
{
  private readonly IMemoryCache _cache;

  public MapBookGeoCache(IMemoryCache cache) => _cache = cache;

  public async Task<T?> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<Task<T?>> factory)
    where T : class
  {
    if (_cache.TryGetValue(key, out T? hit) && hit != null)
      return hit;

    var value = await factory();
    if (value != null)
      _cache.Set(key, value, ttl);
    return value;
  }

  public static string ReverseKey(double lat, double lng) =>
    $"mb:rev:{Math.Round(lat, 3)}:{Math.Round(lng, 3)}";

  public static string NearestKey(double lat, double lng) =>
    $"mb:nr:{Math.Round(lat, 3)}:{Math.Round(lng, 3)}";

  public static string RouteKey(double oLat, double oLng, double dLat, double dLng) =>
    $"mb:rt:{Math.Round(oLat, 4)}:{Math.Round(oLng, 4)}:{Math.Round(dLat, 4)}:{Math.Round(dLng, 4)}";
}
