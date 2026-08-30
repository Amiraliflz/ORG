using Application.Services.MapBook;
using Application.Services.Neshan;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var failed = 0;
void Pass(string msg) => Console.WriteLine($"PASS  {msg}");
void Fail(string msg) { Console.WriteLine($"FAIL  {msg}"); failed++; }

static string FindAppSettingsPath()
{
  var dir = AppContext.BaseDirectory;
  for (var i = 0; i < 10 && dir != null; i++)
  {
    var candidate = Path.Combine(dir, "appsettings.Development.json");
    if (File.Exists(candidate)) return candidate;
    dir = Directory.GetParent(dir)?.FullName;
  }
  return "";
}

static double PinTipOffsetMeters(double lat, double zoom, double mapHeightPx, double anchorY)
{
  // Screen Y diff between map center (0.5) and pin tip (anchorY) in pixels
  var dyPx = (0.5 - anchorY) * mapHeightPx;
  var mPerPx = 156543.03392 * Math.Cos(lat * Math.PI / 180) / Math.Pow(2, zoom);
  return dyPx * mPerPx;
}

// --- Pin tip vs center offset at zoom levels (must be > 3m at z>=14 for snap to matter) ---
{
  const double anchorY = 0.42;
  const double mapH = 700;
  const double lat = 35.6892;
  foreach (var zoom in new[] { 12.0, 14.0, 15.0, 16.0, 17.0, 18.0, 19.0, 20.0 })
  {
    var offM = PinTipOffsetMeters(lat, zoom, mapH, anchorY);
    if (offM >= 3)
      Pass($"Pin tip offset at z{zoom}: {offM:F1}m (>3m, snap geometry meaningful)");
    else
      Fail($"Pin tip offset at z{zoom}: {offM:F1}m (too small — wrong anchor breaks snap)");
  }
}

var configPath = FindAppSettingsPath();

if (string.IsNullOrEmpty(configPath))
{
  Fail("appsettings.Development.json not found (walk up from test output dir)");
}
else
{
  var config = new ConfigurationBuilder()
    .AddJsonFile(configPath, optional: false)
    .AddEnvironmentVariables()
    .Build();

  var services = new ServiceCollection();
  services.AddSingleton<IConfiguration>(config);
  services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
  services.Configure<NeshanOptions>(config.GetSection(NeshanOptions.SectionName));
  services.AddHttpClient<NeshanApiClient>((sp, client) =>
  {
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NeshanOptions>>().Value;
    var baseUrl = string.IsNullOrWhiteSpace(opts.BaseUrl) ? "https://api.neshan.org" : opts.BaseUrl.TrimEnd('/') + "/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
  });
  services.AddHttpClient();
  services.AddSingleton<RoadSnapService>();
  services.AddSingleton<BuildingPlaqueService>();

  await using var sp = services.BuildServiceProvider().CreateAsyncScope();
  var roadSnap = sp.ServiceProvider.GetRequiredService<RoadSnapService>();
  var plaques = sp.ServiceProvider.GetRequiredService<BuildingPlaqueService>();

  // Off-road points at different zoom contexts (same lat/lng — server snap is zoom-independent)
  var snapPoints = new (string Name, double Lat, double Lng)[]
  {
    ("Tehran Imam Khomeini off-road", 35.6905, 51.3890),
    ("Tehran Daryan No area", 35.7219, 51.3347),
    ("Tehran Valiasr side street", 35.6965, 51.4105)
  };

  foreach (var pt in snapPoints)
  {
    var snap = await roadSnap.SnapAsync(pt.Lat, pt.Lng, CancellationToken.None);
    const double clientMinM = 1.0;
    if (snap != null &&
        snap.DistanceMeters >= clientMinM &&
        snap.DistanceMeters <= RoadSnapMath.MaxSnapMeters)
      Pass($"{pt.Name}: snap {snap.DistanceMeters:F1}m ({snap.Source})");
    else
      Fail($"{pt.Name}: snap failed ({snap?.DistanceMeters})");
  }

  // Building plaques in a Tehran viewport
  var plaqueResult = await plaques.GetPlaquesAsync(
    35.6892, 51.3890,
    35.688, 51.387, 35.691, 51.392,
    8,
    CancellationToken.None);

  if (plaqueResult != null && plaqueResult.Plaques.Count >= 2)
    Pass($"Building plaques: {plaqueResult.Plaques.Count} on {plaqueResult.Street}, {plaqueResult.City}");
  else
    Fail($"Building plaques: expected >=2, got {plaqueResult?.Plaques.Count ?? 0}");
}

Console.WriteLine();
if (failed == 0)
{
  Console.WriteLine("All MapBook snap/plaque tests passed.");
  return 0;
}

Console.WriteLine($"{failed} test(s) failed.");
return 1;
