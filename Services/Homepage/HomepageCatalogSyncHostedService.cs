namespace Application.Services.Homepage;

/// <summary>
/// Keeps ordered OD pairs near-live with one server-side poll, while expensive price
/// probes remain on the slower catalog schedule. Browser searches never poll ORS.
/// </summary>
public sealed class HomepageCatalogSyncHostedService : BackgroundService
{
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly ILogger<HomepageCatalogSyncHostedService> _logger;
  private static readonly TimeSpan DirectionsInterval = TimeSpan.FromMinutes(10);
  private static readonly TimeSpan FullCatalogInterval = TimeSpan.FromHours(3);

  public HomepageCatalogSyncHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<HomepageCatalogSyncHostedService> logger)
  {
    _scopeFactory = scopeFactory;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    // Warm OD pairs ASAP so the hero origin/destination pickers are not cold.
    try
    {
      await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
      using (var scope = _scopeFactory.CreateScope())
      {
        var cache = scope.ServiceProvider.GetRequiredService<IHomepageCatalogCache>();
        await cache.EnsureDirectionsAsync(stoppingToken);
      }
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
      return;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Homepage directions warm-up failed");
    }

    var nextFullCatalogRefresh = DateTime.UtcNow;
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        using var scope = _scopeFactory.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IHomepageCatalogCache>();
        if (DateTime.UtcNow >= nextFullCatalogRefresh)
        {
          await cache.RefreshAsync(stoppingToken);
          nextFullCatalogRefresh = DateTime.UtcNow.Add(FullCatalogInterval);
        }
        else
        {
          await cache.RefreshDirectionsAsync(stoppingToken);
        }
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Homepage catalog hosted sync failed");
      }

      try { await Task.Delay(DirectionsInterval, stoppingToken); }
      catch (OperationCanceledException) { break; }
    }
  }
}
