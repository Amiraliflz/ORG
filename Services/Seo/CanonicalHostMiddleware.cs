using Microsoft.Extensions.Options;

namespace Application.Services.Seo;

/// <summary>
/// Crawl hygiene for GSC: 301 legacy hosts to the canonical origin, 410 dead CMS/hosting
/// URLs, keep the payment host out of the index, strip /otapanel.
/// </summary>
public sealed class CanonicalHostMiddleware
{
  private static readonly string[] PaymentHosts =
  [
    "pay.mrshoofer.ir",
    "payment.mrshoofer.ir",
  ];

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
    var path = request.Path.Value ?? "/";
    var host = request.Host.Host;

    if (IsGonePath(path))
      return Gone(context);

    if (IsPaymentHost(host))
      return HandlePaymentHost(context, path);

    var canonicalHost = GetCanonicalHost();
    if (!string.IsNullOrEmpty(canonicalHost)
        && !host.Equals(canonicalHost, StringComparison.OrdinalIgnoreCase)
        && IsLegacyHost(host))
    {
      return PermanentRedirect(context, CanonicalUrl(request, canonicalHost, RewriteLegacyPath(path)));
    }

    // Never HTTP→HTTPS here. Arvan terminates TLS; origin is HTTP, so IsHttps is
    // often false and a 301 to https://{same-host}/ loops (ERR_TOO_MANY_REDIRECTS).

    if (path.Equals("/otapanel", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/otapanel/", StringComparison.OrdinalIgnoreCase))
    {
      return PermanentRedirect(context, CanonicalUrl(request, canonicalHost, RewriteLegacyPath(path)));
    }

    return _next(context);
  }

  private Task HandlePaymentHost(HttpContext context, string path)
  {
    if (path.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase))
    {
      context.Response.StatusCode = StatusCodes.Status200OK;
      context.Response.ContentType = "text/plain; charset=utf-8";
      context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
      context.Response.Headers["Cache-Control"] = "public,max-age=3600";
      return context.Response.WriteAsync(
        "User-agent: *\nDisallow: /\n");
    }

    // Payment root is not a public landing — send Google to the canonical site.
    if (path == "/" || path.Length == 0)
    {
      var canonicalHost = GetCanonicalHost();
      if (!string.IsNullOrEmpty(canonicalHost))
        return PermanentRedirect(context, $"https://{canonicalHost}/");
    }

    context.Response.OnStarting(() =>
    {
      context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
      return Task.CompletedTask;
    });
    return _next(context);
  }

  private static Task PermanentRedirect(HttpContext context, string target)
  {
    context.Response.Headers["Cache-Control"] = "no-store";
    context.Response.Redirect(target, permanent: true);
    return Task.CompletedTask;
  }

  private static Task Gone(HttpContext context)
  {
    context.Response.StatusCode = StatusCodes.Status410Gone;
    context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    context.Response.Headers["Cache-Control"] = "public,max-age=86400";
    context.Response.ContentType = "text/plain; charset=utf-8";
    return context.Response.WriteAsync("Gone");
  }

  internal static bool IsGonePath(string path)
  {
    if (string.IsNullOrEmpty(path) || path == "/") return false;
    var p = path.ToLowerInvariant();

    if (p.StartsWith("/index.php", StringComparison.Ordinal)
        || p.StartsWith("/cgi-sys/", StringComparison.Ordinal)
        || p.StartsWith("/cgi-bin/", StringComparison.Ordinal)
        || p.StartsWith("/wp-", StringComparison.Ordinal)
        || p.Equals("/xmlrpc.php", StringComparison.Ordinal)
        || p.EndsWith("/xmlrpc.php", StringComparison.Ordinal)
        || p.StartsWith("/wordpress", StringComparison.Ordinal)
        || p.Contains("/feed/", StringComparison.Ordinal)
        || p.EndsWith("/feed", StringComparison.Ordinal))
      return true;

    if (p.Contains(".php", StringComparison.Ordinal) && !p.StartsWith("/farsi-fonts", StringComparison.Ordinal))
      return true;

    return false;
  }

  internal static string RewriteLegacyPath(string path)
  {
    if (path.Equals("/otapanel", StringComparison.OrdinalIgnoreCase))
      return "/";

    if (path.StartsWith("/otapanel/", StringComparison.OrdinalIgnoreCase))
    {
      var remainder = path["/otapanel".Length..];
      if (string.IsNullOrEmpty(remainder)) remainder = "/";
      if (remainder.Equals("/Auth/Login", StringComparison.OrdinalIgnoreCase)
          || remainder.Equals("/Auth/Login/", StringComparison.OrdinalIgnoreCase))
        return "/Auth/Login";
      return remainder;
    }

    return path;
  }

  private string CanonicalUrl(HttpRequest request, string canonicalHost, string path)
  {
    if (string.IsNullOrEmpty(canonicalHost))
      canonicalHost = request.Host.Host;
    var pathBase = request.PathBase.HasValue ? request.PathBase.Value! : "";
    return $"https://{canonicalHost}{pathBase}{path}{request.QueryString}";
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

  private static bool IsPaymentHost(string host) =>
    PaymentHosts.Any(h => host.Equals(h, StringComparison.OrdinalIgnoreCase));
}
