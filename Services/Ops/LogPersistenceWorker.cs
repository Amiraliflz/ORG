using Application.Data;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Ops
{
    public class LogPersistenceWorker : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly LogBufferService _buffer;
        private readonly ILogger<LogPersistenceWorker> _logger;

        public LogPersistenceWorker(
            IServiceProvider services,
            LogBufferService buffer,
            ILogger<LogPersistenceWorker> logger)
        {
            _services = services;
            _buffer = buffer;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var batch = new List<Application.Models.AppLogEntry>(100);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    batch.Clear();
                    while (batch.Count < 100 && await _buffer.Reader.WaitToReadAsync(stoppingToken))
                    {
                        while (batch.Count < 100 && _buffer.Reader.TryRead(out var entry))
                            batch.Add(entry);
                    }

                    if (batch.Count == 0)
                    {
                        await Task.Delay(500, stoppingToken);
                        continue;
                    }

                    using var scope = _services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    db.AppLogEntries.AddRange(batch);
                    await db.SaveChangesAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to persist log batch");
                    await Task.Delay(2000, stoppingToken);
                }
            }
        }
    }
}
