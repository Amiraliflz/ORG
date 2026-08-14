namespace Application.Services.Homepage;

public interface IHomepageCatalogCache
{
  IReadOnlyList<string> GetSupportedCities();

  IReadOnlyList<string> GetPopularOrigins();

  long? GetRouteStartingPrice(string slug);

  string GetVersionToken();

  DateTime? GetLastRefreshedUtc();

  Task EnsureFreshAsync(CancellationToken cancellationToken = default);

  Task RefreshAsync(CancellationToken cancellationToken = default);
}
