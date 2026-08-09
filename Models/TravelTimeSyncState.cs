namespace Application.Models;

public class TravelTimeSyncState
{
  public int Id { get; set; }
  public int? LastSyncedShamsiYear { get; set; }
  public int? LastSyncedShamsiMonth { get; set; }
  public DateTime? LastRunAt { get; set; }
  public string LastStatus { get; set; } = "never";
  public string? LastError { get; set; }
  public int LastUpdatedRoutes { get; set; }
  public int LastFailedRoutes { get; set; }
}
