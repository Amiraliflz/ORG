using Application.Data;
using Application.Models;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Ops
{
    public class HealthSnapshotWorker : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<HealthSnapshotWorker> _logger;

        public HealthSnapshotWorker(IServiceProvider services, ILogger<HealthSnapshotWorker> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CaptureAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Health snapshot failed");
                }

                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
        }

        private async Task CaptureAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var beats = new List<SystemHeartbeat>();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // App / process
            beats.Add(new SystemHeartbeat
            {
                Component = "app",
                IsHealthy = true,
                ResponseMs = 0,
                Details = $"uptime={Environment.TickCount64 / 1000}s pid={Environment.ProcessId}"
            });

            // Database
            sw.Restart();
            try
            {
                await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
                beats.Add(new SystemHeartbeat
                {
                    Component = "database",
                    IsHealthy = true,
                    ResponseMs = (int)sw.ElapsedMilliseconds
                });
            }
            catch (Exception ex)
            {
                beats.Add(new SystemHeartbeat
                {
                    Component = "database",
                    IsHealthy = false,
                    ResponseMs = (int)sw.ElapsedMilliseconds,
                    Details = ex.Message
                });
            }

            // Disk
            try
            {
                var drive = DriveInfo.GetDrives()
                    .FirstOrDefault(d => d.IsReady && d.Name == Path.GetPathRoot(AppContext.BaseDirectory));
                if (drive is not null)
                {
                    var freeGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                    var totalGb = drive.TotalSize / (1024.0 * 1024 * 1024);
                    beats.Add(new SystemHeartbeat
                    {
                        Component = "disk",
                        IsHealthy = freeGb > 1,
                        Details = $"{freeGb:F1}GB free / {totalGb:F1}GB"
                    });
                }
            }
            catch (Exception ex)
            {
                beats.Add(new SystemHeartbeat
                {
                    Component = "disk",
                    IsHealthy = false,
                    Details = ex.Message
                });
            }

            db.SystemHeartbeats.AddRange(beats);
            await db.SaveChangesAsync(ct);

            // Trim old heartbeats (keep 7 days)
            var cutoff = DateTime.UtcNow.AddDays(-7);
            await db.SystemHeartbeats
                .Where(h => h.CheckedAt < cutoff)
                .ExecuteDeleteAsync(ct);
        }
    }
}
