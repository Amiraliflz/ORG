namespace Application.Services.Seo;

public sealed class GeneratedRouteDto
{
  public string OriginFa { get; set; } = "";
  public string DestinationFa { get; set; } = "";
  public string Slug { get; set; } = "";
  public int? OriginId { get; set; }
  public int? DestinationId { get; set; }
  public int? TravelTimeMins { get; set; }
  public bool IsPrimary { get; set; }
}

public sealed class GeneratedCityDto
{
  public string NameFa { get; set; } = "";
  public string Slug { get; set; } = "";
  public int? CityId { get; set; }
}

public sealed class GeneratedSeoCatalogDto
{
  public string GeneratedAtUtc { get; set; } = "";
  public string Source { get; set; } = "";
  public List<GeneratedRouteDto> Routes { get; set; } = new();
  public List<GeneratedCityDto> Cities { get; set; } = new();
  public List<string> UnresolvedSlugs { get; set; } = new();
}
