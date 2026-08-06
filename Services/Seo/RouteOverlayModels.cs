namespace Application.Services.Seo;

/// <summary>Optional hand-authored fields for a money route — merge over generated RouteContent.</summary>
public sealed class RouteOverlayDto
{
  public string? H1 { get; set; }
  public string? MetaDescription { get; set; }
  public string? Intro { get; set; }
  public List<RouteOverlayLabeledTextDto>? AboutBlocks { get; set; }
  public string? TravelInfo { get; set; }
  public string? TipsHeading { get; set; }
  public List<string>? Tips { get; set; }
  public string? HowToHeading { get; set; }
  public List<RouteOverlayHowToDto>? HowToSteps { get; set; }
  public List<RouteOverlayFaqDto>? Faqs { get; set; }
  public string? WhyHeading { get; set; }
  public string? WhyBody { get; set; }
}

public sealed class RouteOverlayLabeledTextDto
{
  public string Label { get; set; } = "";
  public string Text { get; set; } = "";
}

public sealed class RouteOverlayHowToDto
{
  public string Title { get; set; } = "";
  public string Text { get; set; } = "";
}

public sealed class RouteOverlayFaqDto
{
  public string Question { get; set; } = "";
  public string Answer { get; set; } = "";
}
