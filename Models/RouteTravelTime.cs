namespace Application.Models;

public class RouteTravelTime
{
  public int Id { get; set; }
  public int OriginCityId { get; set; }
  public int DestinationCityId { get; set; }
  public string OriginNameFa { get; set; } = string.Empty;
  public string DestinationNameFa { get; set; } = string.Empty;
  public int TravelTimeMins { get; set; }
  public int? DistanceMeters { get; set; }
  public string Source { get; set; } = "neshan";
  public int ShamsiYear { get; set; }
  public int ShamsiMonth { get; set; }
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
