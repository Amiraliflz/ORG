using System.Globalization;
using System.Text.RegularExpressions;
using Application.Data;
using Application.Models;
using Application.Services.MrShooferORS;
using Application.Services.Neshan;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Application.Services.TravelTime;

public sealed class TravelTimeSyncResult
{
  public bool Ok { get; set; }
  public string Status { get; set; } = string.Empty;
  public string? Error { get; set; }
  public int CitiesGeocoded { get; set; }
  public int RoutesUpdated { get; set; }
  public int RoutesFailed { get; set; }
  public int RoutesSkipped { get; set; }
  public int ShamsiYear { get; set; }
  public int ShamsiMonth { get; set; }
}

public interface ITravelTimeSyncService
{
  /// <param name="force">When true and not gapsOnly, recalculate all routes.</param>
  /// <param name="gapsOnly">Only geocode missing cities and fill routes that have no row yet.</param>
  Task<TravelTimeSyncResult> SyncAsync(bool force = false, bool gapsOnly = false, CancellationToken ct = default);
  Task<bool> NeedsSyncAsync(CancellationToken ct = default);
}

public sealed class TravelTimeSyncService : ITravelTimeSyncService
{
  private readonly AppDbContext _db;
  private readonly MrShooferAPIClient _ors;
  private readonly NeshanApiClient _neshan;
  private readonly NeshanOptions _neshanOptions;
  private readonly IConfiguration _config;
  private readonly ILogger<TravelTimeSyncService> _logger;
  private static readonly SemaphoreSlim Gate = new(1, 1);

  public TravelTimeSyncService(
    AppDbContext db,
    MrShooferAPIClient ors,
    NeshanApiClient neshan,
    IOptions<NeshanOptions> neshanOptions,
    IConfiguration config,
    ILogger<TravelTimeSyncService> logger)
  {
    _db = db;
    _ors = ors;
    _neshan = neshan;
    _neshanOptions = neshanOptions.Value;
    _config = config;
    _logger = logger;
  }

  public async Task<bool> NeedsSyncAsync(CancellationToken ct = default)
  {
    var pc = new PersianCalendar();
    var now = DateTime.Now;
    var year = pc.GetYear(now);
    var month = pc.GetMonth(now);

    if (!await _db.RouteTravelTimes.AnyAsync(ct))
      return true;

    var state = await _db.TravelTimeSyncStates.OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
    if (state?.LastSyncedShamsiYear is null || state.LastSyncedShamsiMonth is null)
      return true;

    return year > state.LastSyncedShamsiYear
        || (year == state.LastSyncedShamsiYear && month > state.LastSyncedShamsiMonth);
  }

  public async Task<TravelTimeSyncResult> SyncAsync(bool force = false, bool gapsOnly = false, CancellationToken ct = default)
  {
    var result = new TravelTimeSyncResult();
    var pc = new PersianCalendar();
    var now = DateTime.Now;
    result.ShamsiYear = pc.GetYear(now);
    result.ShamsiMonth = pc.GetMonth(now);

    if (!await Gate.WaitAsync(0, ct))
    {
      result.Ok = false;
      result.Status = "busy";
      result.Error = "Sync already running";
      return result;
    }

    try
    {
      if (!_neshan.IsConfigured)
      {
        result.Ok = false;
        result.Status = "disabled";
        result.Error = "Neshan is disabled or ApiKey is missing";
        await PersistStateAsync(result, ct);
        return result;
      }

      if (!force && !gapsOnly && !await NeedsSyncAsync(ct))
      {
        result.Ok = true;
        result.Status = "skipped_current_month";
        return result;
      }

      _logger.LogInformation(
        "TravelTime sync starting for Shamsi {Year}/{Month} (force={Force}, gapsOnly={Gaps})",
        result.ShamsiYear, result.ShamsiMonth, force, gapsOnly);

      List<MrShooferAPIClient.AvaiableDirection> directions;
      try
      {
        var token = _config["MrShoofer:SellerToken"];
        if (!string.IsNullOrWhiteSpace(token))
          _ors.SetSellerApiKey(token);

        directions = await _ors.GetAvaiableOTADirectionsAsync();
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to fetch ORS available directions");
        result.Ok = false;
        result.Status = "ors_error";
        result.Error = ex.Message;
        await PersistStateAsync(result, ct);
        return result;
      }

      var pairs = directions
        .Where(d => !string.IsNullOrWhiteSpace(d.Cityone) && !string.IsNullOrWhiteSpace(d.Citytwo)
                    && d.CityoneId is > 0 && d.CitytwoId is > 0)
        .Select(d => (OriginId: d.CityoneId!.Value, DestId: d.CitytwoId!.Value,
          OriginName: d.Cityone.Trim(), DestName: d.Citytwo.Trim()))
        .Distinct()
        .ToList();

      // Unique cities
      var cities = new Dictionary<int, string>();
      foreach (var p in pairs)
      {
        cities[p.OriginId] = p.OriginName;
        cities[p.DestId] = p.DestName;
      }

      foreach (var (cityId, name) in cities)
      {
        ct.ThrowIfCancellationRequested();
        var existing = await _db.CityCoordinates.FirstOrDefaultAsync(c => c.CityId == cityId, ct);
        if (existing != null)
          continue;

        var geo = await _neshan.GeocodeAsync(name, ct);
        if (geo is null)
        {
          await DelayAsync(ct);
          geo = await _neshan.GeocodeAsync($"{name} ایران", ct);
        }
        await DelayAsync(ct);
        if (geo is null)
        {
          _logger.LogWarning("Geocode failed for city {CityId} {Name}", cityId, name);
          continue;
        }

        _db.CityCoordinates.Add(new CityCoordinate
        {
          CityId = cityId,
          NameFa = name,
          Lat = geo.Value.Lat,
          Lng = geo.Value.Lng,
          Source = "neshan",
          UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        result.CitiesGeocoded++;
      }

      var coordMap = await _db.CityCoordinates.AsNoTracking()
        .ToDictionaryAsync(c => c.CityId, ct);

      var existingRouteKeys = await _db.RouteTravelTimes.AsNoTracking()
        .Select(r => new { r.OriginCityId, r.DestinationCityId })
        .ToListAsync(ct);
      var haveRoute = existingRouteKeys
        .Select(r => (r.OriginCityId, r.DestinationCityId))
        .ToHashSet();

      foreach (var p in pairs)
      {
        ct.ThrowIfCancellationRequested();

        if (gapsOnly && haveRoute.Contains((p.OriginId, p.DestId)))
        {
          result.RoutesSkipped++;
          continue;
        }

        if (!coordMap.TryGetValue(p.OriginId, out var oCoord) ||
            !coordMap.TryGetValue(p.DestId, out var dCoord))
        {
          result.RoutesSkipped++;
          continue;
        }

        try
        {
          var dist = await _neshan.GetDistanceAsync(
            oCoord.Lat, oCoord.Lng, dCoord.Lat, dCoord.Lng, ct);
          await DelayAsync(ct);

          if (dist is null || dist.Value.DurationSeconds <= 0)
          {
            result.RoutesFailed++;
            continue;
          }

          var mins = (int)Math.Max(1, Math.Round(dist.Value.DurationSeconds / 60.0));
          var row = await _db.RouteTravelTimes.FirstOrDefaultAsync(
            r => r.OriginCityId == p.OriginId && r.DestinationCityId == p.DestId, ct);

          if (row is null)
          {
            row = new RouteTravelTime
            {
              OriginCityId = p.OriginId,
              DestinationCityId = p.DestId
            };
            _db.RouteTravelTimes.Add(row);
          }

          // Per-row upsert — only overwrite on success (previous ETA stays until here)
          row.OriginNameFa = p.OriginName;
          row.DestinationNameFa = p.DestName;
          row.TravelTimeMins = mins;
          row.DistanceMeters = dist.Value.DistanceMeters;
          row.Source = "neshan";
          row.ShamsiYear = result.ShamsiYear;
          row.ShamsiMonth = result.ShamsiMonth;
          row.UpdatedAt = DateTime.UtcNow;
          await _db.SaveChangesAsync(ct);
          haveRoute.Add((p.OriginId, p.DestId));
          result.RoutesUpdated++;
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "Route sync failed {Origin}→{Dest}", p.OriginName, p.DestName);
          foreach (var entry in _db.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;
          result.RoutesFailed++;
          await DelayAsync(ct);
          await DelayAsync(ct);
        }
      }

      result.Ok = true;
      result.Status = "ok";
      await PersistStateAsync(result, ct);
      _logger.LogInformation(
        "TravelTime sync done: geocoded={Geo} updated={Up} failed={Fail} skipped={Skip}",
        result.CitiesGeocoded, result.RoutesUpdated, result.RoutesFailed, result.RoutesSkipped);
      return result;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "TravelTime sync crashed");
      result.Ok = false;
      result.Status = "error";
      result.Error = ex.Message;
      try { await PersistStateAsync(result, ct); }
      catch (Exception persistEx)
      {
        _logger.LogWarning(persistEx, "Could not persist sync state after crash");
      }
      return result;
    }
    finally
    {
      Gate.Release();
    }
  }

  private async Task PersistStateAsync(TravelTimeSyncResult result, CancellationToken ct)
  {
    for (var attempt = 1; attempt <= 3; attempt++)
    {
      try
      {
        foreach (var entry in _db.ChangeTracker.Entries().ToList())
          entry.State = EntityState.Detached;

        var state = await _db.TravelTimeSyncStates.OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
        if (state is null)
        {
          state = new TravelTimeSyncState();
          _db.TravelTimeSyncStates.Add(state);
        }

        state.LastRunAt = DateTime.UtcNow;
        state.LastStatus = result.Status;
        state.LastError = result.Error;
        state.LastUpdatedRoutes = result.RoutesUpdated;
        state.LastFailedRoutes = result.RoutesFailed;

        if (result.Ok && result.Status == "ok")
        {
          state.LastSyncedShamsiYear = result.ShamsiYear;
          state.LastSyncedShamsiMonth = result.ShamsiMonth;
        }

        await _db.SaveChangesAsync(ct);
        return;
      }
      catch (Exception ex) when (attempt < 3)
      {
        _logger.LogWarning(ex, "PersistState attempt {Attempt} failed, retrying", attempt);
        await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
      }
    }
  }

  private async Task DelayAsync(CancellationToken ct)
  {
    var ms = Math.Max(0, _neshanOptions.DelayMsBetweenCalls);
    if (ms > 0)
      await Task.Delay(ms, ct);
  }

  public static string NormalizeCityName(string? s)
  {
    if (string.IsNullOrWhiteSpace(s)) return string.Empty;
    var str = s.Trim();
    var idx = str.IndexOf('(');
    if (idx >= 0) str = str[..idx];
    str = Regex.Replace(str, "[\u200C\u200F\u200E\u0610-\u061A\u064B-\u065F\u0670\u06D6-\u06ED]", string.Empty);
    str = str.Replace('\u064A', '\u06CC').Replace('\u0643', '\u06A9');
    str = str.Replace('\u0629', '\u0647');
    str = Regex.Replace(str, "\u0020+", " ").ToLowerInvariant();
    return str;
  }
}
