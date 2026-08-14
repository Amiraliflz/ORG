using Application.Services.MrShooferORS;

namespace Application.Services.Homepage;

/// <summary>Lowest bookable economy (non-VIP) fare — matches homepage "starting price".</summary>
public static class EcoTripPricing
{
  public static bool IsVipTrip(SearchedTrip trip)
  {
    if (trip.taxiSupervisorID is 7 or 8) return true;

    var supervisor = NormalizeForMatch(trip.taxiSupervisorName);
    var car = NormalizeForMatch(trip.carModelName);
    var hay = $"{supervisor} {car}".ToLowerInvariant();
    return hay.Contains("vip")
      || hay.Contains("وی‌آی‌پی")
      || hay.Contains("وی ای پی")
      || hay.Contains("ویایپی")
      || hay.Contains("تشریفات");
  }

  /// <summary>Minimum after-discount price among upcoming non-VIP trips (ECO/base tier).</summary>
  public static int? GetEcoStartingPriceToman(IEnumerable<SearchedTrip> trips, DateTime? now = null)
  {
    var cutoff = (now ?? DateTime.Now).AddMinutes(45);
    var eligible = trips
      .Where(t => t.afterdiscticketprice > 0)
      .Where(t => t.startingDateTime > cutoff)
      .Where(t => !IsVipTrip(t))
      .ToList();

    if (eligible.Count == 0) return null;
    return eligible.Min(t => t.afterdiscticketprice);
  }

  private static string NormalizeForMatch(string? s)
  {
    if (string.IsNullOrWhiteSpace(s)) return string.Empty;
    return s
      .Replace('\u064A', '\u06CC')
      .Replace('\u0643', '\u06A9')
      .Replace('\u200C', ' ')
      .Trim();
  }
}
