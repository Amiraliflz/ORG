namespace Application.Services.Homepage;

/// <summary>Refreshes homepage route prices and supported cities from ORS every few hours.</summary>
public sealed class HomepageCatalogSyncHostedService : BackgroundService
{
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly ILogger<HomepageCatalogSyncHostedService> _logger;
  private static readonly TimeSpan Interval = TimeSpan.FromHours(3);

  public HomepageCatalogSyncHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<HomepageCatalogSyncHostedService> logger)
  {
    _scopeFactory = scopeFactory;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    try { await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken); }
    catch (OperationCanceledException) { return; }

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        using var scope = _scopeFactory.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IHomepageCatalogCache>();
        await cache.RefreshAsync(stoppingToken);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Homepage catalog hosted sync failed");
      }

      try { await Task.Delay(Interval, stoppingToken); }
      catch (OperationCanceledException) { break; }
    }
  }
}
