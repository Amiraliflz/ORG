namespace Application.Services.Neshan;

public class NeshanOptions
{
  public const string SectionName = "Neshan";

  public string ApiKey { get; set; } = string.Empty;
  /// <summary>Client/map SDK key (web.*) — not used for server ETA calls.</summary>
  public string WebApiKey { get; set; } = string.Empty;
  public string BaseUrl { get; set; } = "https://api.neshan.org";
  public bool Enabled { get; set; } = true;
  /// <summary>Delay between Neshan HTTP calls to avoid rate limits.</summary>
  public int DelayMsBetweenCalls { get; set; } = 600;
  public int MaxRetries { get; set; } = 4;
}
