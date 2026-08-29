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

  /// <summary>v5 geocoding — up to several candidate points for one address string.</summary>
  public async Task<IReadOnlyList<(double Lat, double Lng)>> GeocodeCandidatesAsync(
    string address, int limit = 5, CancellationToken ct = default)
  {
    var empty = Array.Empty<(double, double)>();
    if (!IsConfigured || string.IsNullOrWhiteSpace(address))
      return empty;

    var query = Uri.EscapeDataString(address.Trim());
    var list = await SendWithRetryAsync(async token =>
    {
      using var req = new HttpRequestMessage(HttpMethod.Get, $"v5/geocoding?address={query}");
      req.Headers.TryAddWithoutValidation("Api-Key", _options.ApiKey);

      using var resp = await _http.SendAsync(req, token);
      var body = await resp.Content.ReadAsStringAsync(token);
      if (!resp.IsSuccessStatusCode)
      {
        if (IsTransientStatus(resp.StatusCode))
          throw new NeshanTransientException((int)resp.StatusCode, Truncate(body));
        _logger.LogWarning("Neshan v5 geocode failed ({Status}): {Body}", (int)resp.StatusCode, Truncate(body));
        return (IReadOnlyList<(double, double)>?)null;
      }

      using var doc = JsonDocument.Parse(body);
      var root = doc.RootElement;
      var results = new List<(double Lat, double Lng)>();
      if (root.ValueKind == JsonValueKind.Array)
      {
        foreach (var el in root.EnumerateArray())
        {
          if (el.TryGetProperty("location", out var loc) && TryReadLatLng(loc, out var lat, out var lng))
            results.Add((lat, lng));
          if (results.Count >= limit) break;
        }
      }
      return results;
    }, ct);

    return list ?? empty;
  }

  public async Task<NeshanReverseResult?> ReverseAsync(double lat, double lng, CancellationToken ct = default)
  {
    if (!IsConfigured || !IsValidIran(lat, lng))
      return null;

    var url = FormattableString.Invariant($"v4/reverse?lat={lat}&lng={lng}");
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
        _logger.LogWarning("Neshan reverse failed ({Status}): {Body}", (int)resp.StatusCode, Truncate(body));
        return (NeshanReverseResult?)null;
      }

      using var doc = JsonDocument.Parse(body);
      var root = doc.RootElement;
      if (root.TryGetProperty("status", out var st) &&
          string.Equals(st.GetString(), "NO_RESULT", StringComparison.OrdinalIgnoreCase))
        return null;

      var neighbourhood = GetStr(root, "neighbourhood");
      var route = GetStr(root, "route_name") ?? GetStr(root, "address");
      var place = GetStr(root, "place");
      var city = GetStr(root, "city");
      var state = GetStr(root, "state");
      // Prefer readable street/district for the pin (Snapp-style), not a random nearby POI.
      var title = FirstNonEmpty(neighbourhood, route, place) ?? "موقعیت انتخاب‌شده";
      var subParts = new List<string>();
      if (!string.IsNullOrWhiteSpace(route) &&
          !string.Equals(route, title, StringComparison.Ordinal))
        subParts.Add(route!);
      if (!string.IsNullOrWhiteSpace(neighbourhood) &&
          !string.Equals(neighbourhood, title, StringComparison.Ordinal) &&
          !subParts.Contains(neighbourhood!))
        subParts.Add(neighbourhood!);
      if (!string.IsNullOrWhiteSpace(city))
        subParts.Add(city!);
      var subtitle = string.Join("، ", subParts);
      var inTraffic = root.TryGetProperty("in_traffic_zone", out var tz) && tz.ValueKind == JsonValueKind.True;
      var inOddEven = root.TryGetProperty("in_odd_even_zone", out var oz) && oz.ValueKind == JsonValueKind.True;
      return new NeshanReverseResult(title, subtitle, neighbourhood, route, city, state, lat, lng, inTraffic, inOddEven);
    }, ct);
  }

  /// <summary>Driving route along actual roads (step polylines preferred over overview).</summary>
  public async Task<NeshanRouteResult?> GetDrivingRouteAsync(
    double originLat, double originLng,
    double destLat, double destLng,
    CancellationToken ct = default)
  {
    if (!IsConfigured || !IsValidIran(originLat, originLng) || !IsValidIran(destLat, destLng))
      return null;

    var url = FormattableString.Invariant(
      $"v4/direction?type=car&origin={originLat},{originLng}&destination={destLat},{destLng}&alternative=false");

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
        _logger.LogWarning("Neshan direction failed ({Status}): {Body}", (int)resp.StatusCode, Truncate(body));
        return (NeshanRouteResult?)null;
      }

      using var doc = JsonDocument.Parse(body);
      if (!doc.RootElement.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
        return null;

      var route = routes[0];
      var coords = new List<(double Lat, double Lng)>();

      if (route.TryGetProperty("legs", out var legs))
      {
        foreach (var leg in legs.EnumerateArray())
        {
          if (!leg.TryGetProperty("steps", out var steps)) continue;
          foreach (var step in steps.EnumerateArray())
          {
            if (!step.TryGetProperty("polyline", out var pl)) continue;
            var encoded = pl.GetString();
            if (string.IsNullOrWhiteSpace(encoded)) continue;
            var decoded = DecodePolyline(encoded);
            if (decoded.Count == 0) continue;
            if (coords.Count > 0) decoded.RemoveAt(0);
            coords.AddRange(decoded);
          }
        }
      }

      if (coords.Count < 2 &&
          route.TryGetProperty("overview_polyline", out var overview) &&
          overview.TryGetProperty("points", out var pointsEl))
      {
        coords = DecodePolyline(pointsEl.GetString() ?? "");
      }

      if (coords.Count < 2) return null;

      double distance = 0;
      double duration = 0;
      if (route.TryGetProperty("legs", out var legs2))
      {
        foreach (var leg in legs2.EnumerateArray())
        {
          if (leg.TryGetProperty("distance", out var distObj) &&
              distObj.TryGetProperty("value", out var distVal) &&
              distVal.ValueKind == JsonValueKind.Number)
            distance += distVal.GetDouble();
          if (leg.TryGetProperty("duration", out var durObj) &&
              durObj.TryGetProperty("value", out var durVal) &&
              durVal.ValueKind == JsonValueKind.Number)
            duration += durVal.GetDouble();
        }
      }

      return new NeshanRouteResult(coords, distance, duration, "neshan");
    }, ct);
  }

  /// <summary>Google-encoded polyline (precision 1e-5) → lat/lng list.</summary>
  public static List<(double Lat, double Lng)> DecodePolyline(string encoded)
  {
    var coords = new List<(double, double)>();
    if (string.IsNullOrEmpty(encoded)) return coords;

    int index = 0, lat = 0, lng = 0;
    while (index < encoded.Length)
    {
      lat += DecodePolylineValue(encoded, ref index);
      lng += DecodePolylineValue(encoded, ref index);
      coords.Add((lat / 1e5, lng / 1e5));
    }
    return coords;
  }

  private static int DecodePolylineValue(string encoded, ref int index)
  {
    int result = 0, shift = 0, b;
    do
    {
      b = encoded[index++] - 63;
      result |= (b & 0x1f) << shift;
      shift += 5;
    } while (b >= 0x20 && index < encoded.Length);

    return (result & 1) != 0 ? ~(result >> 1) : (result >> 1);
  }

  private static bool IsValidIran(double lat, double lng) =>
    lat is >= 24 and <= 41 && lng is >= 43 and <= 64;

  private static string? GetStr(JsonElement el, string name) =>
    el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
      ? p.GetString()
      : null;

  private static string? FirstNonEmpty(params string?[] values) =>
    values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

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

public sealed record NeshanReverseResult(
  string Title,
  string Subtitle,
  string? Neighbourhood,
  string? Route,
  string? City,
  string? State,
  double Lat,
  double Lng,
  bool InTrafficZone = false,
  bool InOddEvenZone = false);

public sealed record NeshanRouteResult(
  IReadOnlyList<(double Lat, double Lng)> Coordinates,
  double DistanceMeters,
  double DurationSeconds,
  string Source);
