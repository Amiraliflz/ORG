using System.Text.Json;
using Application.Data;
using Application.Services.TravelTime;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.MrShooferORS
{
  public class DirectionsRepository
  {
    // CityIDS dictionary
    private Dictionary<string, int> DirectionsDictionary = new Dictionary<string, int>
    {
      { "تهران" ,360},
      { "شیراز",758 },
      {"رشت", 1031 },
      {"اصفهان",170 },
      {"لاهیجان", 1066 },
      {"چالوس", 1123 },
      {"نوشهر", 1156 },
      {"رامسر", 1126 },
      {"ساری", 1129 },
      {"گرگان", 1015 },
      {"تبریز", 45 },
      {"زنجان", 647 },
      {"کرمانشاه", 967 },
      {"کاشان", 234 },
      {"همدان", 1256 },
      {"قم", 841 },
      {"شهرکرد", 407 },
      {"سنندج", 861 }
    };

    public Dictionary<string, int> GetDirections()
    {
      return DirectionsDictionary;
    }
  }

  /// <summary>
  /// Prefer DB RouteTravelTimes (Neshan), then Directions.json, then 0.
  /// Scoped so it can use AppDbContext.
  /// </summary>
  public class DirectionsTravelTimeCalculator
  {
    private readonly AppDbContext _db;
    private readonly JsonElement _documentRoot;
    private readonly bool _hasJson;

    public DirectionsTravelTimeCalculator(AppDbContext db, IWebHostEnvironment env)
    {
      _db = db;
      _hasJson = false;
      _documentRoot = default;

      string jsonFilePath = Path.Combine(env.WebRootPath, "json", "Directions", "Directions.json");
      if (File.Exists(jsonFilePath))
      {
        var jsonData = File.ReadAllText(jsonFilePath);
        var document = JsonDocument.Parse(jsonData);
        _documentRoot = document.RootElement.Clone();
        _hasJson = true;
      }
    }

    public int GetTravelMins(string originCity, string destinationCity) =>
      GetTravelMins(null, null, originCity, destinationCity);

    public int GetTravelMins(int? originCityId, int? destinationCityId, string? originCity, string? destinationCity)
    {
      try
      {
        if (originCityId is > 0 && destinationCityId is > 0)
        {
          var byId = _db.RouteTravelTimes.AsNoTracking()
            .Where(r =>
              (r.OriginCityId == originCityId && r.DestinationCityId == destinationCityId) ||
              (r.OriginCityId == destinationCityId && r.DestinationCityId == originCityId))
            .Select(r => (int?)r.TravelTimeMins)
            .FirstOrDefault();
          if (byId is > 0)
            return byId.Value;
        }

        var oName = (originCity ?? string.Empty).Trim();
        var dName = (destinationCity ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(oName) && !string.IsNullOrEmpty(dName))
        {
          var byExactName = _db.RouteTravelTimes.AsNoTracking()
            .Where(r =>
              (r.OriginNameFa == oName && r.DestinationNameFa == dName) ||
              (r.OriginNameFa == dName && r.DestinationNameFa == oName))
            .Select(r => (int?)r.TravelTimeMins)
            .FirstOrDefault();
          if (byExactName is > 0)
            return byExactName.Value;

          // Normalized Persian compare (small set until Neshan fill completes)
          var oNorm = TravelTimeSyncService.NormalizeCityName(oName);
          var dNorm = TravelTimeSyncService.NormalizeCityName(dName);
          var nameHit = _db.RouteTravelTimes.AsNoTracking()
            .Select(r => new { r.OriginNameFa, r.DestinationNameFa, r.TravelTimeMins })
            .AsEnumerable()
            .FirstOrDefault(r =>
            {
              var a = TravelTimeSyncService.NormalizeCityName(r.OriginNameFa);
              var b = TravelTimeSyncService.NormalizeCityName(r.DestinationNameFa);
              return (a == oNorm && b == dNorm) || (a == dNorm && b == oNorm);
            });
          if (nameHit != null && nameHit.TravelTimeMins > 0)
            return nameHit.TravelTimeMins;
        }

        return GetFromJson(originCity, destinationCity);
      }
      catch
      {
        return GetFromJson(originCity, destinationCity);
      }
    }

    private int GetFromJson(string? originCity, string? destinationCity)
    {
      if (!_hasJson || string.IsNullOrWhiteSpace(originCity) || string.IsNullOrWhiteSpace(destinationCity))
        return 0;

      try
      {
        var match = _documentRoot.EnumerateArray()
          .FirstOrDefault(element =>
            (element.GetProperty("Cityone").GetString() == originCity &&
             element.GetProperty("Citytwo").GetString() == destinationCity) ||
            (element.GetProperty("Citytwo").GetString() == originCity &&
             element.GetProperty("Cityone").GetString() == destinationCity));

        if (match.ValueKind == JsonValueKind.Undefined)
          return 0;

        return match.GetProperty("TravelTime_mins").GetInt32();
      }
      catch
      {
        return 0;
      }
    }
  }
}
