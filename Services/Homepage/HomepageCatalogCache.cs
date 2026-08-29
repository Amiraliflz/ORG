using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Application.Services.MrShooferORS;
using Application.Services.Seo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Application.Services.Homepage;

public sealed class HomepageCatalogCache : IHomepageCatalogCache
{
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly IConfiguration _configuration;
  private readonly ILogger<HomepageCatalogCache> _logger;
  private readonly SemaphoreSlim _refreshLock = new(1, 1);

  private Snapshot _snapshot = Snapshot.Empty;
  private volatile bool _hasRefreshed;
  private volatile bool _refreshScheduled;

  private sealed record Snapshot(
    IReadOnlyList<string> SupportedCities,
    IReadOnlyList<string> PopularOrigins,
    IReadOnlyList<MrShooferAPIClient.AvaiableDirection> AvailableDirections,
    IReadOnlyDictionary<string, long?> RoutePrices,
    string VersionToken,
    DateTime? RefreshedUtc)
  {
    public static Snapshot Empty { get; } = new(
      Array.Empty<string>(),
      SeoDefaults.HomepagePopularOriginCities.ToList(),
      Array.Empty<MrShooferAPIClient.AvaiableDirection>(),
      new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase),
      "0",
      null);
  }

  public HomepageCatalogCache(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<HomepageCatalogCache> logger)
  {
    _scopeFactory = scopeFactory;
    _configuration = configuration;
    _logger = logger;
  }

  public IReadOnlyList<string> GetSupportedCities() => _snapshot.SupportedCities;

  public IReadOnlyList<string> GetPopularOrigins() => _snapshot.PopularOrigins;

  public IReadOnlyList<MrShooferAPIClient.AvaiableDirection> GetAvailableDirections() =>
    _snapshot.AvailableDirections;

  public long? GetRouteStartingPrice(string slug)
  {
    if (string.IsNullOrWhiteSpace(slug)) return null;
    slug = slug.Trim();
    if (_snapshot.RoutePrices.TryGetValue(slug, out var live) && live is > 0)
      return live;
    return SeoDefaults.HomepageRouteStartingPrice(slug);
  }

  public string GetVersionToken() => _snapshot.VersionToken;

  public DateTime? GetLastRefreshedUtc() => _snapshot.RefreshedUtc;

  public Task EnsureFreshAsync(CancellationToken cancellationToken = default)
  {
    if (_hasRefreshed) return Task.CompletedTask;
    if (_refreshScheduled) return Task.CompletedTask;

    _refreshScheduled = true;
    _ = Task.Run(async () =>
    {
      try
      {
        await RefreshAsync(CancellationToken.None);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Background homepage catalog refresh failed");
        _refreshScheduled = false;
      }
    }, CancellationToken.None);

    return Task.CompletedTask;
  }

  public async Task EnsureDirectionsAsync(CancellationToken cancellationToken = default)
  {
    if (_snapshot.AvailableDirections.Count > 0) return;

    // Another refresh may already be loading directions — wait briefly for it.
    for (var i = 0; i < 40 && _snapshot.AvailableDirections.Count == 0; i++)
    {
      if (await _refreshLock.WaitAsync(0, cancellationToken))
      {
        try
        {
          if (_snapshot.AvailableDirections.Count > 0) return;
          await WarmDirectionsUnderLockAsync(cancellationToken);
          return;
        }
        finally
        {
          _refreshLock.Release();
        }
      }

      await Task.Delay(50, cancellationToken);
      if (_snapshot.AvailableDirections.Count > 0) return;
    }

    if (_snapshot.AvailableDirections.Count > 0) return;

    await _refreshLock.WaitAsync(cancellationToken);
    try
    {
      if (_snapshot.AvailableDirections.Count > 0) return;
      await WarmDirectionsUnderLockAsync(cancellationToken);
    }
    finally
    {
      _refreshLock.Release();
    }
  }

  private async Task WarmDirectionsUnderLockAsync(CancellationToken cancellationToken)
  {
    using var scope = _scopeFactory.CreateScope();
    var api = scope.ServiceProvider.GetRequiredService<MrShooferAPIClient>();
    var token = _configuration["MrShoofer:SellerToken"];
    if (!string.IsNullOrWhiteSpace(token))
      api.SetSellerApiKey(token);

    var directions = await api.GetAvaiableOTADirectionsAsync();
    PublishDirectionsSnapshot(scope, directions);
  }

  public async Task RefreshDirectionsAsync(CancellationToken cancellationToken = default)
  {
    await _refreshLock.WaitAsync(cancellationToken);
    try
    {
      await WarmDirectionsUnderLockAsync(cancellationToken);
      _hasRefreshed = _snapshot.AvailableDirections.Count > 0;
    }
    finally
    {
      _refreshLock.Release();
    }
  }

  private void PublishDirectionsSnapshot(IServiceScope scope, List<MrShooferAPIClient.AvaiableDirection> directions)
  {
    var cityMap = BuildCityMap(directions);
    var supportedCities = cityMap.Values
      .Select(pair => pair.Display)
      .Distinct(StringComparer.Ordinal)
      .OrderBy(c => c, StringComparer.Ordinal)
      .ToList();

    if (supportedCities.Count == 0)
      supportedCities = directionsRepositoryFallback(scope);

    var popularOrigins = BuildPopularOrigins(supportedCities, cityMap);

    _snapshot = new Snapshot(
      supportedCities,
      popularOrigins,
      directions,
      _snapshot.RoutePrices,
      _snapshot.VersionToken == "0" || _snapshot.VersionToken == "fallback" ? "dirs" : _snapshot.VersionToken,
      DateTime.UtcNow);

    _logger.LogInformation(
      "Homepage directions warmed: {CityCount} cities, {DirectionCount} pairs",
      supportedCities.Count,
      directions.Count);
  }

  public async Task RefreshAsync(CancellationToken cancellationToken = default)
  {
    if (!await _refreshLock.WaitAsync(0, cancellationToken))
    {
      // Another refresh is running; wait for directions to appear, then exit.
      for (var i = 0; i < 100 && _snapshot.AvailableDirections.Count == 0; i++)
        await Task.Delay(50, cancellationToken);
      return;
    }

    List<MrShooferAPIClient.AvaiableDirection> directions;
    Dictionary<string, (string Display, int Id)> cityMap;
    List<string> supportedCities;
    IReadOnlyList<string> popularOrigins;

    try
    {
      using var scope = _scopeFactory.CreateScope();
      var api = scope.ServiceProvider.GetRequiredService<MrShooferAPIClient>();
      var token = _configuration["MrShoofer:SellerToken"];
      if (!string.IsNullOrWhiteSpace(token))
        api.SetSellerApiKey(token);

      directions = await api.GetAvaiableOTADirectionsAsync();
      cityMap = BuildCityMap(directions);
      supportedCities = cityMap.Values
        .Select(pair => pair.Display)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(c => c, StringComparer.Ordinal)
        .ToList();

      if (supportedCities.Count == 0)
        supportedCities = directionsRepositoryFallback(scope);

      popularOrigins = BuildPopularOrigins(supportedCities, cityMap);

      _snapshot = new Snapshot(
        supportedCities,
        popularOrigins,
        directions,
        _snapshot.RoutePrices,
        _snapshot.VersionToken == "0" || _snapshot.VersionToken == "fallback" ? "dirs" : _snapshot.VersionToken,
        DateTime.UtcNow);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Homepage catalog refresh failed");
      if (!_hasRefreshed)
      {
        _snapshot = _snapshot with
        {
          PopularOrigins = BuildPopularOrigins(_snapshot.SupportedCities, new Dictionary<string, (string Display, int Id)>()),
          VersionToken = "fallback"
        };
      }
      return;
    }
    finally
    {
      _refreshLock.Release();
    }

    // Price probes outside the lock so AvailableDirections stays fast.
    try
    {
      using var scope = _scopeFactory.CreateScope();
      var api = scope.ServiceProvider.GetRequiredService<MrShooferAPIClient>();
      var token = _configuration["MrShoofer:SellerToken"];
      if (!string.IsNullOrWhiteSpace(token))
        api.SetSellerApiKey(token);

      var routePrices = await FetchRoutePricesAsync(api, cityMap, directions, cancellationToken);
      var version = BuildVersionToken(supportedCities, routePrices);

      await _refreshLock.WaitAsync(cancellationToken);
      try
      {
        _snapshot = new Snapshot(
          supportedCities,
          popularOrigins,
          directions,
          routePrices,
          version,
          DateTime.UtcNow);
        _hasRefreshed = true;
        _refreshScheduled = true;
      }
      finally
      {
        _refreshLock.Release();
      }

      _logger.LogInformation(
        "Homepage catalog refreshed: {CityCount} cities, {DirectionCount} pairs, {RouteCount} route prices",
        supportedCities.Count,
        directions.Count,
        routePrices.Count(kvp => kvp.Value is > 0));
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Homepage catalog price refresh failed");
      _hasRefreshed = _snapshot.AvailableDirections.Count > 0;
      _refreshScheduled = true;
    }
  }

  private static List<string> directionsRepositoryFallback(IServiceScope scope)
  {
    var repo = scope.ServiceProvider.GetRequiredService<DirectionsRepository>();
    return repo.GetDirections().Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
  }

  private static IReadOnlyList<string> BuildPopularOrigins(
    IReadOnlyList<string> supportedCities,
    Dictionary<string, (string Display, int Id)> cityMap)
  {
    var supported = new HashSet<string>(
      supportedCities.Select(NormalizeCity),
      StringComparer.Ordinal);

    var curated = new List<string>();
    foreach (var city in SeoDefaults.HomepagePopularOriginCities)
    {
      var key = NormalizeCity(city);
      if (supported.Contains(key))
        curated.Add(cityMap.TryGetValue(key, out var pair) ? pair.Display : city);
    }

    if (curated.Count > 0) return curated;
    return supportedCities.Take(10).ToList();
  }

  private static Dictionary<string, (string Display, int Id)> BuildCityMap(
    List<MrShooferAPIClient.AvaiableDirection> directions)
  {
    var map = new Dictionary<string, (string Display, int Id)>(StringComparer.Ordinal);
    foreach (var d in directions)
    {
      if (!string.IsNullOrWhiteSpace(d.Cityone) && d.CityoneId is > 0)
      {
        var key = NormalizeCity(d.Cityone);
        if (!map.ContainsKey(key))
          map[key] = (d.Cityone.Trim(), d.CityoneId.Value);
      }
      if (!string.IsNullOrWhiteSpace(d.Citytwo) && d.CitytwoId is > 0)
      {
        var key = NormalizeCity(d.Citytwo);
        if (!map.ContainsKey(key))
          map[key] = (d.Citytwo.Trim(), d.CitytwoId.Value);
      }
    }
    return map;
  }

  private async Task<IReadOnlyDictionary<string, long?>> FetchRoutePricesAsync(
    MrShooferAPIClient api,
    Dictionary<string, (string Display, int Id)> cityMap,
    List<MrShooferAPIClient.AvaiableDirection> directions,
    CancellationToken cancellationToken)
  {
    var prices = new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase);
    var today = DateTime.Today;
    var end = today.AddDays(7);

    foreach (var slug in SeoDefaults.HomepagePopularRouteSlugs)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var route = RouteCatalog.FindBySlug(slug);
      if (route is null) continue;

      var originId = ResolveCityId(route.OriginFa, cityMap, directions);
      var destId = ResolveCityId(route.DestinationFa, cityMap, directions);
      if (originId == 0 || destId == 0)
      {
        prices[slug] = SeoDefaults.HomepageRouteStartingPrice(slug);
        continue;
      }

      try
      {
        var trips = await api.SearchTrips(today, end, originId, destId);
        var min = EcoTripPricing.GetEcoStartingPriceToman(trips ?? Array.Empty<SearchedTrip>());
        prices[slug] = min ?? SeoDefaults.HomepageRouteStartingPrice(slug);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to fetch ECO price for route {Slug}", slug);
        prices[slug] = SeoDefaults.HomepageRouteStartingPrice(slug);
      }
    }

    return prices;
  }

  private static int ResolveCityId(
    string cityFa,
    Dictionary<string, (string Display, int Id)> cityMap,
    List<MrShooferAPIClient.AvaiableDirection> directions)
  {
    var key = NormalizeCity(cityFa);
    if (cityMap.TryGetValue(key, out var pair)) return pair.Id;

    foreach (var d in directions)
    {
      if (NormalizeCity(d.Cityone) == key && d.CityoneId is > 0) return d.CityoneId.Value;
      if (NormalizeCity(d.Citytwo) == key && d.CitytwoId is > 0) return d.CitytwoId.Value;
    }

    return CityCatalog.FindCityId(cityFa) ?? 0;
  }

  private static string BuildVersionToken(
    IReadOnlyList<string> supportedCities,
    IReadOnlyDictionary<string, long?> routePrices)
  {
    var sb = new StringBuilder();
    sb.Append(supportedCities.Count).Append('|');
    foreach (var city in supportedCities.Take(12))
      sb.Append(city).Append(',');
    sb.Append('|');
    foreach (var slug in SeoDefaults.HomepagePopularRouteSlugs)
    {
      routePrices.TryGetValue(slug, out var price);
      sb.Append(slug).Append('=').Append(price ?? 0).Append(';');
    }

    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
    return Convert.ToHexString(hash)[..12].ToLowerInvariant();
  }

  private static string NormalizeCity(string? s)
  {
    if (string.IsNullOrWhiteSpace(s)) return string.Empty;
    var str = s.Trim();
    var idx = str.IndexOf('(');
    if (idx >= 0) str = str[..idx];
    str = Regex.Replace(str, "[\u200C\u200F\u200E\u0610-\u061A\u064B-\u065F\u0670\u06D6-\u06ED]", string.Empty);
    str = str.Replace('\u064A', '\u06CC').Replace('\u0643', '\u06A9');
    str = str.Replace('\u0629', '\u0647');
    str = Regex.Replace(str, "\u0020+", " ").ToLowerInvariant();
    return str;
  }
}
