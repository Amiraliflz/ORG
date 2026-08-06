using System.Text.Json;

namespace Application.Services.Seo;

/// <summary>Loads and merges hand-authored route copy overlays over generated bundles.</summary>
public static class RouteContentOverlays
{
  private static readonly Lazy<IReadOnlyDictionary<string, RouteOverlayDto>> LazyMap = new(Load);

  public static RouteOverlayDto? Find(string? slug)
  {
    if (string.IsNullOrWhiteSpace(slug)) return null;
    return LazyMap.Value.TryGetValue(slug.Trim().ToLowerInvariant(), out var o) ? o : null;
  }

  public static RouteContent.Bundle Apply(RouteContent.Bundle generated, RouteOverlayDto overlay)
  {
    var aboutBlocks = generated.AboutBlocks;
    var aboutCorridor = generated.AboutCorridor;
    if (overlay.AboutBlocks is { Count: > 0 })
    {
      aboutBlocks = overlay.AboutBlocks
        .Where(b => !string.IsNullOrWhiteSpace(b.Label) && !string.IsNullOrWhiteSpace(b.Text))
        .Select(b => (b.Label.Trim(), b.Text.Trim()))
        .ToList();
      aboutCorridor = string.Join(" ", aboutBlocks.Select(b => b.Text));
    }

    var tips = overlay.Tips is { Count: > 0 }
      ? (IReadOnlyList<string>)overlay.Tips.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList()
      : generated.Tips;

    var howTo = overlay.HowToSteps is { Count: > 0 }
      ? (IReadOnlyList<(string, string)>)overlay.HowToSteps
          .Where(s => !string.IsNullOrWhiteSpace(s.Title) && !string.IsNullOrWhiteSpace(s.Text))
          .Select(s => (s.Title.Trim(), s.Text.Trim()))
          .ToList()
      : generated.HowToSteps;

    var faqs = overlay.Faqs is { Count: > 0 }
      ? (IReadOnlyList<(string, string)>)overlay.Faqs
          .Where(f => !string.IsNullOrWhiteSpace(f.Question) && !string.IsNullOrWhiteSpace(f.Answer))
          .Select(f => (f.Question.Trim(), f.Answer.Trim()))
          .ToList()
      : generated.Faqs;

    var meta = Pick(overlay.MetaDescription, generated.MetaDescription);
    if (meta.Length > 165)
      meta = meta[..162] + "…";

    return new RouteContent.Bundle(
      H1: Pick(overlay.H1, generated.H1),
      MetaDescription: meta,
      Intro: Pick(overlay.Intro, generated.Intro),
      AboutCorridor: aboutCorridor,
      AboutBlocks: aboutBlocks,
      TravelInfo: Pick(overlay.TravelInfo, generated.TravelInfo),
      TipsHeading: Pick(overlay.TipsHeading, generated.TipsHeading),
      Tips: tips,
      HowToHeading: Pick(overlay.HowToHeading, generated.HowToHeading),
      HowToSteps: howTo,
      Faqs: faqs,
      WhyHeading: Pick(overlay.WhyHeading, generated.WhyHeading),
      WhyBody: Pick(overlay.WhyBody, generated.WhyBody),
      ApproxKm: generated.ApproxKm);
  }

  private static string Pick(string? overlay, string generated) =>
    string.IsNullOrWhiteSpace(overlay) ? generated : overlay.Trim();

  private static IReadOnlyDictionary<string, RouteOverlayDto> Load()
  {
    var path = SeoDataPaths.RoutesOverlaysPath;
    if (!File.Exists(path))
      return new Dictionary<string, RouteOverlayDto>(StringComparer.OrdinalIgnoreCase);

    try
    {
      var json = File.ReadAllText(path);
      var map = JsonSerializer.Deserialize<Dictionary<string, RouteOverlayDto>>(json, SeoDataPaths.JsonOptions);
      if (map is null || map.Count == 0)
        return new Dictionary<string, RouteOverlayDto>(StringComparer.OrdinalIgnoreCase);

      return new Dictionary<string, RouteOverlayDto>(map, StringComparer.OrdinalIgnoreCase);
    }
    catch
    {
      return new Dictionary<string, RouteOverlayDto>(StringComparer.OrdinalIgnoreCase);
    }
  }
}
