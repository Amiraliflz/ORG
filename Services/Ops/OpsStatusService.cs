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
        private readonly IHostEnvironment _env;

        public OpsStatusService(
            AppDbContext db,
            IServiceRestarter restarter,
            PlatformAnalyticsService analytics,
            IConfiguration config,
            IHttpClientFactory httpFactory,
            IHostEnvironment env)
        {
            _db = db;
            _restarter = restarter;
            _analytics = analytics;
            _config = config;
            _httpFactory = httpFactory;
            _env = env;
        }

        public async Task<OpsStatusDto> GetStatusAsync(CancellationToken ct = default)
        {
            var components = new List<ComponentStatus>();

            // App — live: this process is answering
            components.Add(new ComponentStatus
            {
                Name = "app",
                Label = "وب‌اپ",
                IsHealthy = true,
                Critical = true,
                Details = $"pid={Environment.ProcessId}",
                CheckedAt = DateTime.UtcNow
            });

            // Database
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await _db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
                components.Add(new ComponentStatus
                {
                    Name = "database",
                    Label = "دیتابیس",
                    IsHealthy = true,
                    Critical = true,
                    ResponseMs = (int)sw.ElapsedMilliseconds,
                    CheckedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                components.Add(new ComponentStatus
                {
                    Name = "database",
                    Label = "دیتابیس",
                    IsHealthy = false,
                    Critical = true,
                    Details = Truncate(ex.Message, 120),
                    CheckedAt = DateTime.UtcNow
                });
            }

            // MrShoofer API (informational — 2xx/3xx = ok)
            sw.Restart();
            try
            {
                var client = _httpFactory.CreateClient("OpsHealthCheck");
                var baseUrl = _config["MrShoofer:ApiBaseUrl"] ?? "https://ors.shoofer.taxi";
                using var resp = await client.GetAsync(baseUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                var code = (int)resp.StatusCode;
                var ok = code is >= 200 and < 400;
                components.Add(new ComponentStatus
                {
                    Name = "mrshoofer_api",
                    Label = "API مسترشوفر",
                    IsHealthy = ok,
                    Critical = false,
                    ResponseMs = (int)sw.ElapsedMilliseconds,
                    Details = $"HTTP {code}",
                    CheckedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                components.Add(new ComponentStatus
                {
                    Name = "mrshoofer_api",
                    Label = "API مسترشوفر",
                    IsHealthy = false,
                    Critical = false,
                    Details = Truncate(ex.Message, 120),
                    CheckedAt = DateTime.UtcNow
                });
            }

            // Disk — live reading
            components.Add(ReadDiskStatus());

            // Application service (systemd) — only critical in Production
            if (!_env.IsDevelopment())
            {
                var serviceActive = await _restarter.IsServiceActiveAsync(ct);
                components.Add(new ComponentStatus
                {
                    Name = "systemd",
                    Label = "سرویس اپ",
                    IsHealthy = serviceActive,
                    Critical = true,
                    Details = serviceActive ? "active" : "inactive",
                    CheckedAt = DateTime.UtcNow
                });
            }

            var uptime = await _analytics.GetUptimePercent24hAsync(ct);
            // Overall = critical components only (disk / external API don't force DOWN)
            var overallHealthy = components.Where(c => c.Critical).All(c => c.IsHealthy);

            return new OpsStatusDto
            {
                IsHealthy = overallHealthy,
                CheckedAt = DateTime.UtcNow,
                UptimePercent24h = uptime,
                Components = components
            };
        }

        private ComponentStatus ReadDiskStatus()
        {
            try
            {
                var usage = DiskUsageProbe.TryGet(_env.ContentRootPath);
                if (usage is null)
                {
                    return new ComponentStatus
                    {
                        Name = "disk",
                        Label = "دیسک",
                        IsHealthy = true,
                        Critical = false,
                        Details = "نامشخص",
                        CheckedAt = DateTime.UtcNow
                    };
                }

                return new ComponentStatus
                {
                    Name = "disk",
                    Label = "دیسک",
                    IsHealthy = usage.Value.FreeGb > 2,
                    Critical = false,
                    Details = DiskUsageProbe.FormatFa(usage.Value),
                    CheckedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                return new ComponentStatus
                {
                    Name = "disk",
                    Label = "دیسک",
                    IsHealthy = true,
                    Critical = false,
                    Details = Truncate(ex.Message, 80),
                    CheckedAt = DateTime.UtcNow
                };
            }
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max] + "…");
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
        /// <summary>If true, failure marks overall status DOWN.</summary>
        public bool Critical { get; set; }
        public int? ResponseMs { get; set; }
        public string? Details { get; set; }
        public DateTime? CheckedAt { get; set; }
    }
}
