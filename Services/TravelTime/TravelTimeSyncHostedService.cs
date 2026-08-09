namespace Application.Services.TravelTime;

/// <summary>
/// Checks every few hours whether the Shamsi month advanced (or table is empty)
/// and refreshes Neshan ETAs. Old route times stay live until each row is upserted.
/// </summary>
public sealed class TravelTimeSyncHostedService : BackgroundService
{
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly ILogger<TravelTimeSyncHostedService> _logger;
  private static readonly TimeSpan Interval = TimeSpan.FromHours(3);

  public TravelTimeSyncHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<TravelTimeSyncHostedService> logger)
  {
    _scopeFactory = scopeFactory;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    // Small delay so the app finishes booting before first check
    try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
    catch (OperationCanceledException) { return; }

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        using var scope = _scopeFactory.CreateScope();
        var sync = scope.ServiceProvider.GetRequiredService<ITravelTimeSyncService>();
        if (await sync.NeedsSyncAsync(stoppingToken))
        {
          _logger.LogInformation("TravelTime hosted sync triggered (empty table or new Shamsi month)");
          await sync.SyncAsync(force: true, gapsOnly: false, stoppingToken);
        }
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "TravelTime hosted sync failed");
      }

      try { await Task.Delay(Interval, stoppingToken); }
      catch (OperationCanceledException) { break; }
    }
  }
}
