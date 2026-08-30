using System.Text.Json;
using System.Text.Json.Serialization;

namespace Application.Services.MapBook;

/// <summary>
/// Curated public venues (airports, hospitals, malls) with entrance pickers — Snapp-style.
/// </summary>
public sealed class PublicVenueService
{
  private readonly IWebHostEnvironment _env;
  private readonly ILogger<PublicVenueService> _logger;
  private IReadOnlyList<PublicVenue>? _venues;
  private readonly object _lock = new();

  public PublicVenueService(IWebHostEnvironment env, ILogger<PublicVenueService> logger)
  {
    _env = env;
    _logger = logger;
  }

  public IReadOnlyList<PublicVenue> GetAll()
  {
    EnsureLoaded();
    return _venues ?? Array.Empty<PublicVenue>();
  }

  public PublicVenue? FindAt(double lat, double lng)
  {
    EnsureLoaded();
    if (_venues == null) return null;
    foreach (var v in _venues)
    {
      if (PointInPolygon(lng, lat, v.Polygon))
        return v;
    }
    return null;
  }

  public IReadOnlyList<PublicVenueSearchHit> Search(string query, string? city, int limit = 8)
  {
    EnsureLoaded();
    if (_venues == null || string.IsNullOrWhiteSpace(query)) return Array.Empty<PublicVenueSearchHit>();

    var q = query.Trim();
    var cityNorm = city?.Trim() ?? "";
    var hits = new List<PublicVenueSearchHit>();

    foreach (var v in _venues)
    {
      if (!string.IsNullOrWhiteSpace(cityNorm) &&
          !v.City.Contains(cityNorm, StringComparison.OrdinalIgnoreCase) &&
          !cityNorm.Contains(v.City, StringComparison.OrdinalIgnoreCase))
        continue;

      var score = 0;
      if (v.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) score += 10;
      if (v.ShortName.Contains(q, StringComparison.OrdinalIgnoreCase)) score += 8;
      foreach (var kw in v.Keywords)
      {
        if (kw.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            q.Contains(kw, StringComparison.OrdinalIgnoreCase))
          score += 6;
      }
      if (score <= 0) continue;

      hits.Add(new PublicVenueSearchHit(v, score));
    }

    return hits
      .OrderByDescending(h => h.Score)
      .Take(limit)
      .ToList();
  }

  private void EnsureLoaded()
  {
    if (_venues != null) return;
    lock (_lock)
    {
      if (_venues != null) return;
      try
      {
        var path = Path.Combine(_env.WebRootPath, "data", "iran", "public-venues.json");
        if (!File.Exists(path))
        {
          _venues = Array.Empty<PublicVenue>();
          return;
        }

        var json = File.ReadAllText(path);
        var doc = JsonSerializer.Deserialize<PublicVenuesFile>(json, JsonOptions);
        _venues = doc?.Venues ?? new List<PublicVenue>();
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to load public-venues.json");
        _venues = Array.Empty<PublicVenue>();
      }
    }
  }

  private static bool PointInPolygon(double x, double y, IReadOnlyList<double[]> ring)
  {
    if (ring.Count < 3) return false;
    var inside = false;
    for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
    {
      var xi = ring[i][0];
      var yi = ring[i][1];
      var xj = ring[j][0];
      var yj = ring[j][1];
      if (((yi > y) != (yj > y)) &&
          (x < (xj - xi) * (y - yi) / (yj - yi + 1e-14) + xi))
        inside = !inside;
    }
    return inside;
  }

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
  };
}

public sealed class PublicVenuesFile
{
  public List<PublicVenue> Venues { get; set; } = new();
}

public sealed class PublicVenue
{
  public string Id { get; set; } = "";
  public string Name { get; set; } = "";
  public string ShortName { get; set; } = "";
  public string Type { get; set; } = "";
  public string City { get; set; } = "";
  public List<string> Keywords { get; set; } = new();
  public VenueCenter Center { get; set; } = new();
  public List<double[]> Polygon { get; set; } = new();
  public List<VenueEntrance> Entrances { get; set; } = new();
}

public sealed class VenueCenter
{
  public double Lat { get; set; }
  public double Lng { get; set; }
}

public sealed class VenueEntrance
{
  public string Id { get; set; } = "";
  public string Label { get; set; } = "";
  public double Lat { get; set; }
  public double Lng { get; set; }
}

public sealed record PublicVenueSearchHit(PublicVenue Venue, int Score);
