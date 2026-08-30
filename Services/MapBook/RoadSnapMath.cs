using System.Globalization;
using System.Text.Json;

namespace Application.Services.MapBook;

public static class RoadSnapMath
{
  public const double MinSnapMeters = 4;
  public const double MaxSnapMeters = 2000;

  public static bool IsValidIran(double lat, double lng) =>
    lat is >= 24 and <= 41 && lng is >= 43 and <= 64;

  public static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
  {
    const double R = 6371000;
    var dLat = (lat2 - lat1) * Math.PI / 180;
    var dLon = (lon2 - lon1) * Math.PI / 180;
    var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
      + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
      * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
    return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
  }

  /// <summary>Closest point on a lat/lng polyline to a query point.</summary>
  public static (double Lat, double Lng, double DistM)? NearestOnPolyline(
    double lat,
    double lng,
    IReadOnlyList<(double Lat, double Lng)> path)
  {
    if (path == null || path.Count < 2) return null;

    double bestLat = path[0].Lat;
    double bestLng = path[0].Lng;
    var bestDist = HaversineMeters(lat, lng, bestLat, bestLng);

    for (var i = 0; i < path.Count - 1; i++)
    {
      var a = path[i];
      var b = path[i + 1];
      var proj = ProjectPointOnSegment(lat, lng, a.Lat, a.Lng, b.Lat, b.Lng);
      var d = HaversineMeters(lat, lng, proj.Lat, proj.Lng);
      if (d < bestDist)
      {
        bestDist = d;
        bestLat = proj.Lat;
        bestLng = proj.Lng;
      }
    }

    return (bestLat, bestLng, bestDist);
  }

  public static (double Lat, double Lng) ProjectPointOnSegment(
    double lat, double lng,
    double lat1, double lng1, double lat2, double lng2)
  {
    // Match map-book.js: lat is treated as x, lng as y along the segment.
    var dx = lat2 - lat1;
    var dy = lng2 - lng1;
    var len2 = dx * dx + dy * dy;
    if (len2 < 1e-14) return (lat1, lng1);

    var t = ((lat - lat1) * dx + (lng - lng1) * dy) / len2;
    t = Math.Max(0, Math.Min(1, t));
    return (lat1 + t * dx, lng1 + t * dy);
  }

  public static RoadSnapResult? ParseOsrmNearestJson(string json, double lat, double lng)
  {
    if (string.IsNullOrWhiteSpace(json)) return null;

    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;
    if (!root.TryGetProperty("waypoints", out var wps) || wps.GetArrayLength() < 1)
      return null;

    var wp = wps[0];
    if (!wp.TryGetProperty("location", out var loc) || loc.GetArrayLength() < 2)
      return null;

    var snapLng = loc[0].GetDouble();
    var snapLat = loc[1].GetDouble();
    var dist = wp.TryGetProperty("distance", out var dEl)
      ? dEl.GetDouble()
      : HaversineMeters(lat, lng, snapLat, snapLng);

    return AcceptSnap(snapLat, snapLng, dist, "osrm");
  }

  public static RoadSnapResult? AcceptSnap(double snapLat, double snapLng, double distM, string source)
  {
    // MinSnapMeters is enforced client-side; server returns any in-range snap.
    if (distM > MaxSnapMeters) return null;
    if (!IsValidIran(snapLat, snapLng)) return null;
    return new RoadSnapResult(snapLat, snapLng, distM, source);
  }

  public static string BuildOsrmNearestUrl(string baseUrl, double lat, double lng)
  {
    var lngStr = lng.ToString(CultureInfo.InvariantCulture);
    var latStr = lat.ToString(CultureInfo.InvariantCulture);
    return $"{baseUrl.TrimEnd('/')}/nearest/v1/driving/{lngStr},{latStr}?number=1";
  }
}
