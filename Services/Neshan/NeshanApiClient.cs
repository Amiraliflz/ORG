using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Application.Services.Neshan;

public sealed class NeshanApiClient
{
  private readonly HttpClient _http;
  private readonly NeshanOptions _options;
  private readonly ILogger<NeshanApiClient> _logger;

  public NeshanApiClient(HttpClient http, IOptions<NeshanOptions> options, ILogger<NeshanApiClient> logger)
  {
    _http = http;
    _options = options.Value;
    _logger = logger;
  }

  public bool IsConfigured =>
    _options.Enabled && !string.IsNullOrWhiteSpace(_options.ApiKey);

  public async Task<(double Lat, double Lng)?> GeocodeAsync(string address, CancellationToken ct = default)
  {
    if (!IsConfigured || string.IsNullOrWhiteSpace(address))
      return null;

    var query = Uri.EscapeDataString(address.Trim());
    return await SendWithRetryAsync(async token =>
    {
      using var req = new HttpRequestMessage(HttpMethod.Get, $"v4/geocoding?address={query}");
      req.Headers.TryAddWithoutValidation("Api-Key", _options.ApiKey);

      using var resp = await _http.SendAsync(req, token);
      var body = await resp.Content.ReadAsStringAsync(token);
      if (!resp.IsSuccessStatusCode)
      {
        if (IsTransientStatus(resp.StatusCode))
          throw new NeshanTransientException((int)resp.StatusCode, Truncate(body));
        _logger.LogWarning("Neshan geocode failed ({Status}) for {Address}: {Body}",
          (int)resp.StatusCode, address, Truncate(body));
        return ((double, double)?)null;
      }

      using var doc = JsonDocument.Parse(body);
      var root = doc.RootElement;
      if (root.TryGetProperty("status", out var st) &&
          string.Equals(st.GetString(), "NO_RESULT", StringComparison.OrdinalIgnoreCase))
        return null;

      if (root.TryGetProperty("location", out var loc) && TryReadLatLng(loc, out var lat, out var lng))
        return (lat, lng);
      if (TryReadLatLng(root, out var lat2, out var lng2))
        return (lat2, lng2);

      _logger.LogWarning("Neshan geocode: no location for {Address}: {Body}", address, Truncate(body));
      return null;
    }, ct);
  }

  public async Task<(int DurationSeconds, int DistanceMeters)?> GetDistanceAsync(
    double originLat, double originLng,
    double destLat, double destLng,
    CancellationToken ct = default)
  {
    if (!IsConfigured)
      return null;

    var origins = FormattableString.Invariant($"{originLat},{originLng}");
    var destinations = FormattableString.Invariant($"{destLat},{destLng}");
    var url =
      $"v1/distance-matrix?type=car&origins={Uri.EscapeDataString(origins)}&destinations={Uri.EscapeDataString(destinations)}";

    return await SendWithRetryAsync(async token =>
    {
      using var req = new HttpRequestMessage(HttpMethod.Get, url);
      req.Headers.TryAddWithoutValidation("Api-Key", _options.ApiKey);

      using var resp = await _http.SendAsync(req, token);
      var body = await resp.Content.ReadAsStringAsync(token);
      if (!resp.IsSuccessStatusCode)
      {
        if (IsTransientStatus(resp.StatusCode))
          throw new NeshanTransientException((int)resp.StatusCode, Truncate(body));
        _logger.LogWarning("Neshan distance-matrix failed ({Status}): {Body}",
          (int)resp.StatusCode, Truncate(body));
        return ((int, int)?)null;
      }

      using var doc = JsonDocument.Parse(body);
      var root = doc.RootElement;
      if (!root.TryGetProperty("rows", out var rows) || rows.GetArrayLength() == 0)
        return null;

      var elements = rows[0].GetProperty("elements");
      if (elements.GetArrayLength() == 0)
        return null;

      var el = elements[0];
      var status = el.TryGetProperty("status", out var st) ? st.GetString() : null;
      if (!string.IsNullOrEmpty(status) &&
          !status.Equals("Ok", StringComparison.OrdinalIgnoreCase) &&
          !status.Equals("OK", StringComparison.OrdinalIgnoreCase))
      {
        _logger.LogWarning("Neshan distance-matrix element status={Status}", status);
        return null;
      }

      var durationSec = ReadNestedInt(el, "duration", "value");
      var distanceM = ReadNestedInt(el, "distance", "value");
      if (durationSec is null)
        return null;

      return (durationSec.Value, distanceM ?? 0);
    }, ct);
  }

  private async Task<T?> SendWithRetryAsync<T>(Func<CancellationToken, Task<T?>> action, CancellationToken ct)
  {
    const int maxAttempts = 4;
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
      try
      {
        return await action(ct);
      }
      catch (Exception ex) when (IsTransient(ex) && attempt < maxAttempts)
      {
        var delay = TimeSpan.FromMilliseconds(800 * Math.Pow(2, attempt - 1));
        _logger.LogWarning(ex, "Neshan transient error attempt {Attempt}/{Max}, backoff {DelayMs}ms",
          attempt, maxAttempts, delay.TotalMilliseconds);
        await Task.Delay(delay, ct);
      }
      catch (Exception ex) when (IsTransient(ex))
      {
        _logger.LogWarning(ex, "Neshan failed after {Max} attempts", maxAttempts);
        return default;
      }
    }

    return default;
  }

  private static bool IsTransientStatus(HttpStatusCode code) =>
    code is HttpStatusCode.BadGateway
      or HttpStatusCode.ServiceUnavailable
      or HttpStatusCode.GatewayTimeout
      or HttpStatusCode.TooManyRequests
      or HttpStatusCode.RequestTimeout;

  private static bool IsTransient(Exception ex) =>
    ex is NeshanTransientException
      or HttpRequestException
      or TaskCanceledException
      or IOException
      or SocketException
      || ex.InnerException is SocketException or IOException;

  private static int? ReadNestedInt(JsonElement el, string objName, string valueName)
  {
    if (!el.TryGetProperty(objName, out var obj))
      return null;
    if (obj.ValueKind == JsonValueKind.Number)
      return obj.GetInt32();
    if (obj.TryGetProperty(valueName, out var v) && v.ValueKind == JsonValueKind.Number)
      return v.GetInt32();
    return null;
  }

  private static bool TryReadLatLng(JsonElement el, out double lat, out double lng)
  {
    lat = 0;
    lng = 0;
    if (el.TryGetProperty("y", out var y) && el.TryGetProperty("x", out var x))
    {
      lat = ReadDouble(y);
      lng = ReadDouble(x);
      return lat != 0 || lng != 0;
    }
    if (el.TryGetProperty("lat", out var latEl) && el.TryGetProperty("lng", out var lngEl))
    {
      lat = ReadDouble(latEl);
      lng = ReadDouble(lngEl);
      return true;
    }
    if (el.TryGetProperty("latitude", out var lat2) && el.TryGetProperty("longitude", out var lng2))
    {
      lat = ReadDouble(lat2);
      lng = ReadDouble(lng2);
      return true;
    }
    return false;
  }

  private static double ReadDouble(JsonElement el) =>
    el.ValueKind == JsonValueKind.String
      ? double.Parse(el.GetString()!, CultureInfo.InvariantCulture)
      : el.GetDouble();

  private static string Truncate(string s) =>
    string.IsNullOrEmpty(s) ? s : (s.Length <= 400 ? s : s[..400] + "…");

  private sealed class NeshanTransientException : Exception
  {
    public NeshanTransientException(int status, string body)
      : base($"HTTP {status}: {body}") { }
  }
}
