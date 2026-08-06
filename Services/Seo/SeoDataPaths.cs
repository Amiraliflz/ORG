using System.Text.Json;

namespace Application.Services.Seo;

/// <summary>Resolves wwwroot path for generated SEO JSON (set from Program.cs).</summary>
public static class SeoDataPaths
{
  private static string? _webRoot;

  public static void Configure(string webRootPath) =>
    _webRoot = string.IsNullOrWhiteSpace(webRootPath) ? null : webRootPath;

  public static string WebRoot
  {
    get
    {
      if (!string.IsNullOrWhiteSpace(_webRoot) && Directory.Exists(_webRoot))
        return _webRoot!;

      foreach (var c in CandidateWwwRoots())
      {
        if (Directory.Exists(c))
        {
          _webRoot = c;
          return c;
        }
      }

      return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
    }
  }

  public static string RoutesGeneratedPath =>
    Path.Combine(WebRoot, "json", "Seo", "routes.generated.json");

  public static string CitiesGeneratedPath =>
    Path.Combine(WebRoot, "json", "Seo", "cities.generated.json");

  public static string CatalogGeneratedPath =>
    Path.Combine(WebRoot, "json", "Seo", "catalog.generated.json");

  public static string RoutesOverlaysPath =>
    Path.Combine(WebRoot, "json", "Seo", "routes.overlays.json");

  public static string DirectionsJsonPath =>
    Path.Combine(WebRoot, "json", "Directions", "Directions.json");

  private static IEnumerable<string> CandidateWwwRoots()
  {
    yield return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
    yield return Path.Combine(AppContext.BaseDirectory, "wwwroot");
    yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"));
    yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "wwwroot"));
  }

  public static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
  };
}
