namespace Application.Services.Seo;

public sealed class SeoOptions
{
  public const string SectionName = "Seo";

  /// <summary>Canonical public origin, e.g. https://mrshoofer.com</summary>
  public string PreferredOrigin { get; set; } = "https://mrshoofer.com";

  /// <summary>Hosts that 301 to PreferredOrigin (apex .com).</summary>
  public string[] LegacyHosts { get; set; } =
  [
    "mrshoofer.ir",
    "www.mrshoofer.ir",
    "www.mrshoofer.com",
  ];

  /// <summary>Short domain for SMS / marketing copy (no scheme).</summary>
  public string PublicSiteHost { get; set; } = "mrshoofer.com";
}
