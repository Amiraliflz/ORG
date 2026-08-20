namespace Application.Services.Ops
{
    public interface IServiceRestarter
    {
        Task<(bool Success, string Message)> RestartAsync(CancellationToken ct = default);
        Task<bool> IsServiceActiveAsync(CancellationToken ct = default);
    }

    public class ServiceRestarter : IServiceRestarter
    {
        private readonly IConfiguration _config;
        private readonly ILogger<ServiceRestarter> _logger;
        private readonly IHostEnvironment _env;

        public ServiceRestarter(
            IConfiguration config,
            ILogger<ServiceRestarter> logger,
            IHostEnvironment env)
        {
            _config = config;
            _logger = logger;
            _env = env;
        }

        public async Task<(bool Success, string Message)> RestartAsync(CancellationToken ct = default)
        {
            if (_env.IsDevelopment())
            {
                _logger.LogWarning("Restart requested in Development — stopping host");
                return (true, "Development mode: host stop requested (systemd not available locally)");
            }

            var service = _config["Ops:ServiceName"] ?? "org.service";

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "sudo",
                    Arguments = $"/bin/systemctl restart {service}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc is null)
                    return (false, "Failed to start restart process");

                var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
                var stderr = await proc.StandardError.ReadToEndAsync(ct);
                await proc.WaitForExitAsync(ct);

                if (proc.ExitCode != 0)
                {
                    _logger.LogError("Restart failed: {Stderr}", stderr);
                    return (false, string.IsNullOrWhiteSpace(stderr) ? "Restart command failed" : stderr.Trim());
                }

                // Poll /health until up or timeout
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var healthUrl = _config["Ops:HealthUrl"] ?? "http://127.0.0.1:5055/health";
                for (var i = 0; i < 30; i++)
                {
                    await Task.Delay(1000, ct);
                    try
                    {
                        var resp = await http.GetAsync(healthUrl, ct);
                        if (resp.IsSuccessStatusCode)
                            return (true, "Service restarted and healthy");
                    }
                    catch
                    {
                        // still starting
                    }
                }

                return (true, "Restart issued; health check pending");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restart exception");
                return (false, ex.Message);
            }
        }

        public async Task<bool> IsServiceActiveAsync(CancellationToken ct = default)
        {
            if (_env.IsDevelopment()) return true;

            var service = _config["Ops:ServiceName"] ?? "org.service";
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "sudo",
                    Arguments = $"/bin/systemctl is-active {service}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc is null) return false;
                var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
                await proc.WaitForExitAsync(ct);
                return stdout.Trim().Equals("active", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
