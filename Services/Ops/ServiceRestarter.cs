namespace Application.Services.Ops
{
    public interface IServiceRestarter
    {
        Task<(bool Success, string Message)> RestartAsync(CancellationToken ct = default);
        Task<bool> IsServiceActiveAsync(CancellationToken ct = default);
        /// <summary>True when the web app host is up (systemd and/or blue-green process).</summary>
        Task<(bool Healthy, string Details)> GetHostHealthAsync(CancellationToken ct = default);
    }

    public class ServiceRestarter : IServiceRestarter
    {
        private readonly IConfiguration _config;
        private readonly ILogger<ServiceRestarter> _logger;
        private readonly IHostEnvironment _env;
        private readonly IHostApplicationLifetime _lifetime;

        public ServiceRestarter(
            IConfiguration config,
            ILogger<ServiceRestarter> logger,
            IHostEnvironment env,
            IHostApplicationLifetime lifetime)
        {
            _config = config;
            _logger = logger;
            _env = env;
            _lifetime = lifetime;
        }

        public async Task<(bool Success, string Message)> RestartAsync(CancellationToken ct = default)
        {
            // Optional custom command (e.g. wrapper script that restarts the process)
            var customCmd = _config["Ops:RestartCommand"];
            if (!string.IsNullOrWhiteSpace(customCmd))
            {
                return await RunShellAsync(customCmd, ct);
            }

            if (_env.IsDevelopment())
            {
                // Prefer local restart script so the process comes back up
                var root = _env.ContentRootPath;
                var script = Path.Combine(root, "Scripts", "ops-local-restart.sh");
                if (!File.Exists(script))
                    script = Path.Combine(root, "scripts", "ops-local-restart.sh");
                if (File.Exists(script))
                {
                    _logger.LogWarning("Restart requested — launching {Script}", script);
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = Quote(script),
                        WorkingDirectory = root,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    System.Diagnostics.Process.Start(psi);
                    return (true, "وب‌اپ در حال راه‌اندازی مجدد است…");
                }

                _logger.LogWarning("Restart requested in Development — scheduling host stop in 1s");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000);
                    _lifetime.StopApplication();
                }, CancellationToken.None);
                return (true, "Development: وب‌اپ در حال توقف است. دوباره آن را دستی اجرا کنید.");
            }

            // Production: blue-green helper (preferred), then systemd fallback
            var helper = _config["Ops:RestartScript"] ?? "/usr/local/bin/org-ops-restart.sh";
            if (File.Exists(helper))
            {
                _logger.LogWarning("Restart requested — running {Helper}", helper);
                var (ok, msg) = await RunProcessAsync("sudo", helper, ct);
                if (!ok)
                    return (false, string.IsNullOrWhiteSpace(msg) ? "راه‌اندازی مجدد ناموفق" : msg);

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var healthUrl = _config["Ops:HealthUrl"] ?? "http://127.0.0.1:5055/health";
                for (var i = 0; i < 40; i++)
                {
                    await Task.Delay(1000, ct);
                    try
                    {
                        var resp = await http.GetAsync(healthUrl, ct);
                        if (resp.IsSuccessStatusCode)
                            return (true, "وب‌اپ ری‌استارت شد و سالم است");
                    }
                    catch
                    {
                        // still starting / cutting over
                    }
                }

                return (true, "دستور ری‌استارت وب‌اپ اجرا شد؛ منتظر سلامت…");
            }

            var service = _config["Ops:ServiceName"] ?? "org.service";

            try
            {
                var (ok, msg) = await RunProcessAsync("sudo", $"/bin/systemctl restart {service}", ct);
                if (!ok)
                {
                    _logger.LogError("Restart failed: {Msg}", msg);
                    return (false, string.IsNullOrWhiteSpace(msg) ? "Restart command failed" : msg);
                }

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var healthUrl = _config["Ops:HealthUrl"] ?? "http://127.0.0.1:5055/health";
                for (var i = 0; i < 30; i++)
                {
                    await Task.Delay(1000, ct);
                    try
                    {
                        var resp = await http.GetAsync(healthUrl, ct);
                        if (resp.IsSuccessStatusCode)
                            return (true, "وب‌اپ ری‌استارت شد و سالم است");
                    }
                    catch
                    {
                        // still starting
                    }
                }

                return (true, "دستور ری‌استارت وب‌اپ اجرا شد؛ منتظر سلامت…");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restart exception");
                return (false, ex.Message);
            }
        }

        private async Task<(bool Success, string Message)> RunProcessAsync(string fileName, string args, CancellationToken ct)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return (false, "Failed to start process");
            var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            var stderr = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            var combined = (stdout + "\n" + stderr).Trim();
            return proc.ExitCode == 0 ? (true, combined) : (false, combined);
        }

        private async Task<(bool Success, string Message)> RunShellAsync(string command, CancellationToken ct)
        {
            return await RunProcessAsync("/bin/bash", $"-lc {Quote(command)}", ct);
        }

        private static string Quote(string s) =>
            "'" + s.Replace("'", "'\\''") + "'";

        public async Task<bool> IsServiceActiveAsync(CancellationToken ct = default)
        {
            var (healthy, _) = await GetHostHealthAsync(ct);
            return healthy;
        }

        public async Task<(bool Healthy, string Details)> GetHostHealthAsync(CancellationToken ct = default)
        {
            if (_env.IsDevelopment())
                return (true, "development");

            // 1) systemd unit (when used)
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
                if (proc is not null)
                {
                    var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
                    await proc.WaitForExitAsync(ct);
                    if (stdout.Trim().Equals("active", StringComparison.OrdinalIgnoreCase))
                        return (true, $"systemd {service} active");
                }
            }
            catch
            {
                // fall through — blue-green is normal on this VPS
            }

            // 2) Blue-green: this process is answering Ops status ⇒ host is up
            return (true, $"blue-green pid={Environment.ProcessId}");
        }
    }
}
