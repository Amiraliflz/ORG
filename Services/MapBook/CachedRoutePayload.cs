namespace Application.Services.MapBook;

/// <summary>Cached JSON-serializable route response for MapBook OsrmRoute.</summary>
public sealed class CachedRoutePayload
{
  public required string Code { get; init; }
  public required string Source { get; init; }
  public required object[] Routes { get; init; }
}
