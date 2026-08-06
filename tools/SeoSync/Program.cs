using Application.Services.Seo;
using Microsoft.Extensions.Configuration;

// Usage:
//   dotnet run --project tools/SeoSync
//   dotnet run --project tools/SeoSync -- --from-directions
//   dotnet run --project tools/SeoSync -- --api https://ors.shoofer.taxi

var repoRoot = FindRepoRoot();
var webRoot = Path.Combine(repoRoot, "wwwroot");
SeoDataPaths.Configure(webRoot);

var config = new ConfigurationBuilder()
  .SetBasePath(repoRoot)
  .AddJsonFile("appsettings.json", optional: true)
  .AddJsonFile("appsettings.Development.json", optional: true)
  .AddEnvironmentVariables()
  .Build();

var api = config["MrShoofer:ApiBaseUrl"] ?? "https://ors.shoofer.taxi";
var fromDirections = args.Contains("--from-directions", StringComparer.OrdinalIgnoreCase);
for (var i = 0; i < args.Length - 1; i++)
{
  if (args[i] is "--api" or "-a")
    api = args[i + 1];
}

Console.WriteLine($"WebRoot: {webRoot}");
Console.WriteLine(fromDirections
  ? "Source: wwwroot/json/Directions/Directions.json (fallback)"
  : $"Source: {api}/Directions/getAvailableDirections");

try
{
  var result = fromDirections
    ? await SeoCatalogSync.RunFromDirectionsJsonAsync(webRoot)
    : await SeoCatalogSync.RunAsync(api, webRoot);

  Console.WriteLine($"Wrote {result.RouteCount} routes → {result.RoutesPath}");
  Console.WriteLine($"Wrote {result.CityCount} cities → {result.CitiesPath}");
  if (result.UnresolvedSlugs.Count > 0)
  {
    Console.WriteLine($"Unresolved slug transliterations ({result.UnresolvedSlugs.Count}) — review:");
    foreach (var u in result.UnresolvedSlugs)
      Console.WriteLine($"  - {u}");
  }
  else
  {
    Console.WriteLine("All city slugs resolved from known map or transliteration.");
  }

  return 0;
}
catch (Exception ex)
{
  Console.Error.WriteLine($"SEO sync failed: {ex.Message}");
  if (!fromDirections)
  {
    Console.Error.WriteLine("Retrying from Directions.json fallback…");
    try
    {
      var result = await SeoCatalogSync.RunFromDirectionsJsonAsync(webRoot);
      Console.WriteLine($"Fallback wrote {result.RouteCount} routes, {result.CityCount} cities.");
      return 0;
    }
    catch (Exception ex2)
    {
      Console.Error.WriteLine($"Fallback also failed: {ex2.Message}");
    }
  }
  return 1;
}

static string FindRepoRoot()
{
  var dir = new DirectoryInfo(AppContext.BaseDirectory);
  while (dir is not null)
  {
    if (File.Exists(Path.Combine(dir.FullName, "Application.csproj")) &&
        Directory.Exists(Path.Combine(dir.FullName, "wwwroot")))
      return dir.FullName;
    dir = dir.Parent;
  }
  // tools/SeoSync → repo root
  return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
