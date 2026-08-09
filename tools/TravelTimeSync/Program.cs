using System.Net;
using System.Net.Sockets;
using Application.Data;
using Application.Services.MrShooferORS;
using Application.Services.Neshan;
using Application.Services.TravelTime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Usage:
//   dotnet run --project tools/TravelTimeSync -- --gaps     # fill missing only (safe resume)
//   dotnet run --project tools/TravelTimeSync -- --force    # full recalculate

var repoRoot = FindRepoRoot();
var force = args.Any(a => a is "--force" or "-f");
var gapsOnly = args.Any(a => a is "--gaps" or "-g") || !force;

var config = new ConfigurationBuilder()
  .SetBasePath(repoRoot)
  .AddJsonFile("appsettings.json", optional: true)
  .AddJsonFile("appsettings.Development.json", optional: true)
  .AddEnvironmentVariables()
  .Build();

var host = Host.CreateDefaultBuilder(args)
  .ConfigureLogging(l =>
  {
    l.ClearProviders();
    l.AddSimpleConsole(o => o.SingleLine = true);
    l.SetMinimumLevel(LogLevel.Information);
  })
  .ConfigureServices(services =>
  {
    services.AddSingleton<IConfiguration>(config);
    services.Configure<NeshanOptions>(config.GetSection(NeshanOptions.SectionName));

    var conn = config.GetConnectionString("development")
      ?? config.GetConnectionString("production")
      ?? throw new InvalidOperationException("No connection string");
    if (!conn.Contains("Timeout=", StringComparison.OrdinalIgnoreCase))
      conn += ";Timeout=60;Command Timeout=120";

    services.AddDbContext<AppDbContext>(o => o.UseNpgsql(conn));

    static HttpMessageHandler CreateHandler() => new SocketsHttpHandler
    {
      UseCookies = false,
      UseProxy = false,
      Proxy = null,
      PooledConnectionLifetime = TimeSpan.FromMinutes(2),
      MaxConnectionsPerServer = 2,
      ConnectCallback = async (context, cancellationToken) =>
      {
        // Prefer IPv4 — avoids macOS "Can't assign requested address" with flaky IPv6
        var entry = await Dns.GetHostEntryAsync(context.DnsEndPoint.Host, cancellationToken);
        var ipv4 = entry.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                   ?? entry.AddressList.First();
        var socket = new Socket(ipv4.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(ipv4, context.DnsEndPoint.Port, cancellationToken);
        return new NetworkStream(socket, ownsSocket: true);
      }
    };

    services.AddHttpClient<MrShooferAPIClient>((_, client) =>
    {
      var baseUrl = config["MrShoofer:ApiBaseUrl"] ?? "https://ors.shoofer.taxi";
      client.BaseAddress = new Uri(baseUrl);
      client.Timeout = TimeSpan.FromSeconds(60);
    }).ConfigurePrimaryHttpMessageHandler(CreateHandler);

    services.AddHttpClient<NeshanApiClient>((sp, client) =>
    {
      var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NeshanOptions>>().Value;
      var baseUrl = string.IsNullOrWhiteSpace(opts.BaseUrl) ? "https://api.neshan.org" : opts.BaseUrl.TrimEnd('/') + "/";
      client.BaseAddress = new Uri(baseUrl);
      client.Timeout = TimeSpan.FromSeconds(60);
    }).ConfigurePrimaryHttpMessageHandler(CreateHandler);

    services.AddScoped<ITravelTimeSyncService, TravelTimeSyncService>();
  })
  .Build();

Console.WriteLine($"Repo: {repoRoot}");
Console.WriteLine($"Mode: {(gapsOnly ? "gaps-only" : "full")} force={force}");
Console.WriteLine("Starting Neshan travel-time sync…");

using var scope = host.Services.CreateScope();
var sync = scope.ServiceProvider.GetRequiredService<ITravelTimeSyncService>();
var result = await sync.SyncAsync(force: true, gapsOnly: gapsOnly, CancellationToken.None);

Console.WriteLine($"Status: {result.Status} Ok={result.Ok}");
Console.WriteLine($"Shamsi: {result.ShamsiYear}/{result.ShamsiMonth}");
Console.WriteLine($"Geocoded cities: {result.CitiesGeocoded}");
Console.WriteLine($"Routes updated: {result.RoutesUpdated}");
Console.WriteLine($"Routes failed:  {result.RoutesFailed}");
Console.WriteLine($"Routes skipped: {result.RoutesSkipped}");
if (!string.IsNullOrEmpty(result.Error))
  Console.WriteLine($"Error: {result.Error}");

return result.Ok ? 0 : 1;

static string FindRepoRoot()
{
  var dir = new DirectoryInfo(AppContext.BaseDirectory);
  while (dir != null)
  {
    if (File.Exists(Path.Combine(dir.FullName, "Application.csproj")))
      return dir.FullName;
    dir = dir.Parent;
  }
  return Directory.GetCurrentDirectory();
}
