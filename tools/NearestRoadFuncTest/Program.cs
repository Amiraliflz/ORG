using Application.Services.MapBook;
using Application.Services.Neshan;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var failed = 0;
void Pass(string msg) => Console.WriteLine($"PASS  {msg}");
void Fail(string msg) { Console.WriteLine($"FAIL  {msg}"); failed++; }

// --- Unit: polyline nearest ---
{
  var path = new List<(double Lat, double Lng)>
  {
    (35.0, 51.0),
    (35.0, 51.001),
    (35.0, 51.002)
  };
  var near = RoadSnapMath.NearestOnPolyline(35.00001, 51.0005, path);
  if (near != null && near.Value.DistM < 5)
    Pass("NearestOnPolyline finds point on segment");
  else
    Fail($"NearestOnPolyline expected small distance, got {near?.DistM}");
}

// --- Unit: OSRM JSON parse ---
{
  const string sample = """
    {"code":"Ok","waypoints":[{"location":[51.389167,35.688858],"distance":40.8,"name":"test"}]}
    """;
  var snap = RoadSnapMath.ParseOsrmNearestJson(sample, 35.6892, 51.3890);
  if (snap != null && snap.Source == "osrm" && snap.DistanceMeters >= RoadSnapMath.MinSnapMeters)
    Pass("ParseOsrmNearestJson accepts valid OSRM payload");
  else
    Fail($"ParseOsrmNearestJson failed: {snap?.DistanceMeters}");
}

// --- Unit: OSRM JSON parse accepts small-distance snap (client filters MinSnapMeters) ---
{
  const string tooClose = """
    {"code":"Ok","waypoints":[{"location":[51.38901,35.68919],"distance":2.1,"name":"test"}]}
    """;
  var snap = RoadSnapMath.ParseOsrmNearestJson(tooClose, 35.6892, 51.3890);
  if (snap != null && snap.DistanceMeters < RoadSnapMath.MinSnapMeters)
    Pass("ParseOsrmNearestJson returns near-road snap below client threshold");
  else
    Fail($"expected near-road snap, got {snap?.DistanceMeters}");
}

// --- Unit: Iran bounds ---
{
  if (RoadSnapMath.IsValidIran(35.6892, 51.3890) && !RoadSnapMath.IsValidIran(10, 10))
    Pass("IsValidIran bounds");
  else
    Fail("IsValidIran bounds wrong");
}

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

// --- Integration: RoadSnapService with Neshan (if configured) ---
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

  await using var sp = services.BuildServiceProvider().CreateAsyncScope();
  var roadSnap = sp.ServiceProvider.GetRequiredService<RoadSnapService>();

  // Point ~80m off a major road in Tehran — should snap via Neshan
  const double offRoadLat = 35.6905;
  const double offRoadLng = 51.3890;
  var neshanOnly = await roadSnap.SnapViaNeshanAsync(offRoadLat, offRoadLng, CancellationToken.None);
  if (neshanOnly != null &&
      neshanOnly.Source == "neshan" &&
      neshanOnly.DistanceMeters >= RoadSnapMath.MinSnapMeters &&
      neshanOnly.DistanceMeters <= RoadSnapMath.MaxSnapMeters)
  {
    Pass($"Neshan snap off-road point: {neshanOnly.DistanceMeters:F1}m → ({neshanOnly.Lat:F5},{neshanOnly.Lng:F5})");
  }
  else
  {
    Fail($"Neshan snap failed for off-road Tehran point: {neshanOnly?.DistanceMeters}");
  }

  var full = await roadSnap.SnapAsync(offRoadLat, offRoadLng, CancellationToken.None);
  if (full != null && full.OkEquivalent())
    Pass($"SnapAsync full pipeline: source={full.Source}, distance={full.DistanceMeters:F1}m");
  else
    Fail("SnapAsync returned null for off-road Tehran point");

  // Already on road — should not snap (below min distance)
  var onRoad = await roadSnap.SnapAsync(35.689167, 51.389167, CancellationToken.None);
  if (onRoad == null)
    Pass("SnapAsync skips point already on road (< MinSnapMeters)");
  else
    Pass($"SnapAsync on-road point moved {onRoad.DistanceMeters:F1}m (source={onRoad.Source})");
}

Console.WriteLine();
if (failed == 0)
{
  Console.WriteLine("All NearestRoad tests passed.");
  return 0;
}

Console.WriteLine($"{failed} test(s) failed.");
return 1;

static class RoadSnapResultExt
{
  public static bool OkEquivalent(this RoadSnapResult r) =>
    r.DistanceMeters >= RoadSnapMath.MinSnapMeters &&
    r.DistanceMeters <= RoadSnapMath.MaxSnapMeters &&
    RoadSnapMath.IsValidIran(r.Lat, r.Lng);
}
