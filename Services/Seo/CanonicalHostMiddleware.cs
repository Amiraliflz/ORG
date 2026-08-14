using Microsoft.Extensions.Options;

namespace Application.Services.Seo;

/// <summary>
/// 301 legacy hosts → canonical origin; strip /otapanel prefix (old OTA panel URLs).
/// </summary>
public sealed class CanonicalHostMiddleware
{
  private readonly RequestDelegate _next;
  private readonly IHostEnvironment _env;
  private readonly SeoOptions _seo;

  public CanonicalHostMiddleware(
    RequestDelegate next,
    IHostEnvironment env,
    IOptions<SeoOptions> seo)
  {
    _next = next;
    _env = env;
    _seo = seo.Value;
  }

  public Task InvokeAsync(HttpContext context)
  {
    var request = context.Request;
    var host = request.Host.Host;
    var canonicalHost = GetCanonicalHost();

    if (!string.IsNullOrEmpty(canonicalHost)
        && !host.Equals(canonicalHost, StringComparison.OrdinalIgnoreCase)
        && IsLegacyHost(host))
    {
      var target = $"{ResolveScheme(request)}://{canonicalHost}{request.PathBase}{request.Path}{request.QueryString}";
      context.Response.Headers["Cache-Control"] = "no-store";
      context.Response.Redirect(target, permanent: true);
      return Task.CompletedTask;
    }

    var path = request.Path.Value ?? "/";
    if (path.Equals("/otapanel", StringComparison.OrdinalIgnoreCase))
    {
      context.Response.Redirect("/" + request.QueryString, permanent: true);
      return Task.CompletedTask;
    }

    if (path.StartsWith("/otapanel/", StringComparison.OrdinalIgnoreCase))
    {
      var remainder = path["/otapanel".Length..];
      if (string.IsNullOrEmpty(remainder)) remainder = "/";
      context.Response.Redirect(remainder + request.QueryString, permanent: true);
      return Task.CompletedTask;
    }

    return _next(context);
  }

  private string GetCanonicalHost()
  {
    if (string.IsNullOrWhiteSpace(_seo.PreferredOrigin)) return string.Empty;
    return Uri.TryCreate(_seo.PreferredOrigin, UriKind.Absolute, out var uri)
      ? uri.Host
      : string.Empty;
  }

  private bool IsLegacyHost(string host)
  {
    if (_seo.LegacyHosts is not { Length: > 0 }) return false;
    return _seo.LegacyHosts.Any(h =>
      host.Equals(h, StringComparison.OrdinalIgnoreCase));
  }

  private string ResolveScheme(HttpRequest request)
  {
    if (request.IsHttps) return "https";
    return _env.IsDevelopment() ? request.Scheme : "https";
  }
}
