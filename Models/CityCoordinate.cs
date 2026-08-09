namespace Application.Models;

public class CityCoordinate
{
  public int Id { get; set; }
  public int CityId { get; set; }
  public string NameFa { get; set; } = string.Empty;
  public double Lat { get; set; }
  public double Lng { get; set; }
  public string Source { get; set; } = "neshan";
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
