using Application.Data;
using Application.Models;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Ops
{
    public class OpsStatusService
    {
        private readonly AppDbContext _db;
        private readonly IServiceRestarter _restarter;
        private readonly PlatformAnalyticsService _analytics;
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpFactory;

        public OpsStatusService(
            AppDbContext db,
            IServiceRestarter restarter,
            PlatformAnalyticsService analytics,
            IConfiguration config,
            IHttpClientFactory httpFactory)
        {
            _db = db;
            _restarter = restarter;
            _analytics = analytics;
            _config = config;
            _httpFactory = httpFactory;
        }

        public async Task<OpsStatusDto> GetStatusAsync(CancellationToken ct = default)
        {
            var components = new List<ComponentStatus>();

            // App heartbeat
            var lastApp = await _db.SystemHeartbeats.AsNoTracking()
                .Where(h => h.Component == "app")
                .OrderByDescending(h => h.CheckedAt)
                .FirstOrDefaultAsync(ct);
            components.Add(new ComponentStatus
            {
                Name = "app",
                Label = "Application",
                IsHealthy = lastApp?.IsHealthy ?? true,
                Details = lastApp?.Details,
                CheckedAt = lastApp?.CheckedAt
            });

            // Database
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await _db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
                components.Add(new ComponentStatus
                {
                    Name = "database",
                    Label = "Database",
                    IsHealthy = true,
                    ResponseMs = (int)sw.ElapsedMilliseconds,
                    CheckedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                components.Add(new ComponentStatus
                {
                    Name = "database",
                    Label = "Database",
                    IsHealthy = false,
                    Details = ex.Message,
                    CheckedAt = DateTime.UtcNow
                });
            }

            // MrShoofer API
            sw.Restart();
            try
            {
                var client = _httpFactory.CreateClient("OpsHealthCheck");
                var baseUrl = _config["MrShoofer:ApiBaseUrl"] ?? "https://ors.shoofer.taxi";
                var resp = await client.GetAsync(baseUrl, ct);
                components.Add(new ComponentStatus
                {
                    Name = "mrshoofer_api",
                    Label = "MrShoofer API",
                    IsHealthy = resp.IsSuccessStatusCode,
                    ResponseMs = (int)sw.ElapsedMilliseconds,
                    Details = $"HTTP {(int)resp.StatusCode}",
                    CheckedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                components.Add(new ComponentStatus
                {
                    Name = "mrshoofer_api",
                    Label = "MrShoofer API",
                    IsHealthy = false,
                    Details = ex.Message,
                    CheckedAt = DateTime.UtcNow
                });
            }

            // Disk from last snapshot
            var lastDisk = await _db.SystemHeartbeats.AsNoTracking()
                .Where(h => h.Component == "disk")
                .OrderByDescending(h => h.CheckedAt)
                .FirstOrDefaultAsync(ct);
            components.Add(new ComponentStatus
            {
                Name = "disk",
                Label = "Disk",
                IsHealthy = lastDisk?.IsHealthy ?? true,
                Details = lastDisk?.Details,
                CheckedAt = lastDisk?.CheckedAt
            });

            // Systemd service
            var serviceActive = await _restarter.IsServiceActiveAsync(ct);
            components.Add(new ComponentStatus
            {
                Name = "systemd",
                Label = "Service",
                IsHealthy = serviceActive,
                Details = serviceActive ? "active" : "inactive",
                CheckedAt = DateTime.UtcNow
            });

            var uptime = await _analytics.GetUptimePercent24hAsync(ct);
            var overallHealthy = components.All(c => c.IsHealthy);

            return new OpsStatusDto
            {
                IsHealthy = overallHealthy,
                CheckedAt = DateTime.UtcNow,
                UptimePercent24h = uptime,
                Components = components
            };
        }
    }

    public class OpsStatusDto
    {
        public bool IsHealthy { get; set; }
        public DateTime CheckedAt { get; set; }
        public double UptimePercent24h { get; set; }
        public List<ComponentStatus> Components { get; set; } = new();
    }

    public class ComponentStatus
    {
        public string Name { get; set; } = "";
        public string Label { get; set; } = "";
        public bool IsHealthy { get; set; }
        public int? ResponseMs { get; set; }
        public string? Details { get; set; }
        public DateTime? CheckedAt { get; set; }
    }
}
