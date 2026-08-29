using Application.Services.MrShooferORS;

namespace Application.Services.Homepage;

public interface IHomepageCatalogCache
{
  IReadOnlyList<string> GetSupportedCities();

  IReadOnlyList<string> GetPopularOrigins();

  /// <summary>Cached OD city pairs for the hero/search pickers. Empty until first successful refresh.</summary>
  IReadOnlyList<MrShooferAPIClient.AvaiableDirection> GetAvailableDirections();

  long? GetRouteStartingPrice(string slug);

  string GetVersionToken();

  DateTime? GetLastRefreshedUtc();

  Task EnsureFreshAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Ensures directions are loaded (blocking on first miss). Prefer this for AvailableDirections.
  /// </summary>
  Task EnsureDirectionsAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Refreshes only ordered origin/destination pairs without running route-price probes.
  /// Intended for the single server-side synchronization worker.
  /// </summary>
  Task RefreshDirectionsAsync(CancellationToken cancellationToken = default);

  Task RefreshAsync(CancellationToken cancellationToken = default);
}
