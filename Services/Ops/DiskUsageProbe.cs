using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Application.Services.Ops
{
    /// <summary>
    /// Accurate disk usage for the volume that hosts the app (via <c>df</c>).
    /// Avoids DriveInfo quirks on APFS (sealed system volume / wrong 460GB root).
    /// </summary>
    public static class DiskUsageProbe
    {
        public readonly record struct DiskUsage(double FreeGb, double TotalGb, double UsedPct, string Mount);

        public static DiskUsage? TryGet(string? path = null)
        {
            path ??= Directory.GetCurrentDirectory();
            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
                path = "/";
            }

            var fromDf = TryDf(path);
            if (fromDf is not null) return fromDf;

            return TryDriveInfo(path);
        }

        private static DiskUsage? TryDf(string path)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "df",
                    // -k = 1K blocks, -P = POSIX (one line per mount)
                    Arguments = $"-kP {Quote(path)}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc is null) return null;
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);
                if (proc.ExitCode != 0) return null;

                // Filesystem 1024-blocks Used Available Capacity Mounted on
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (lines.Length < 2) return null;
                var parts = Regex.Split(lines[^1].Trim(), @"\s+");
                if (parts.Length < 6) return null;

                if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalK)) return null;
                if (!long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var availK)) return null;

                var totalGb = totalK / (1024.0 * 1024.0);
                var freeGb = availK / (1024.0 * 1024.0);
                if (totalGb <= 0) return null;
                var usedPct = Math.Clamp((1 - freeGb / totalGb) * 100, 0, 100);
                var mount = parts[^1];
                return new DiskUsage(freeGb, totalGb, usedPct, mount);
            }
            catch
            {
                return null;
            }
        }

        private static DiskUsage? TryDriveInfo(string path)
        {
            try
            {
                // Walk parents until a DriveInfo matches (handles /System/Volumes/Data on macOS)
                var full = Path.GetFullPath(path);
                DriveInfo? best = null;
                foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady && x.TotalSize > 0))
                {
                    var name = d.Name.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (full.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                        || full.Equals(d.Name.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    {
                        if (best is null || d.Name.Length > best.Name.Length)
                            best = d;
                    }
                }

                if (best is null) return null;
                var freeGb = best.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                var totalGb = best.TotalSize / (1024.0 * 1024 * 1024);
                if (totalGb <= 0) return null;
                var usedPct = (1 - freeGb / totalGb) * 100;
                return new DiskUsage(freeGb, totalGb, usedPct, best.Name);
            }
            catch
            {
                return null;
            }
        }

        private static string Quote(string s) =>
            "'" + s.Replace("'", "'\\''") + "'";

        public static string FormatFa(DiskUsage u) =>
            $"{u.FreeGb:F1}GB آزاد از {u.TotalGb:F1}GB ({u.UsedPct:F0}% پر · {u.Mount})";

        public static string FormatEn(DiskUsage u) =>
            $"{u.FreeGb:F1}GB free / {u.TotalGb:F1}GB ({u.UsedPct:F0}% used · {u.Mount})";
    }
}
