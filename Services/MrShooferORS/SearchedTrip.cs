using System.Text.Json.Serialization;

namespace Application.Services.MrShooferORS
{
  public enum TripType { OnlyPrivate, OnlyMultiPassenger, PrivateAndMultiPassenger }

  public class SearchedTrip
  {
    public string tripPlanCode { get; set; }
    public DateTime startingDateTime { get; set; }
    public string startingDateTimeShamsi { get; set; }
    public int originalTicketprice { get; set; }
    public int afterdiscticketprice { get; set; }
    public string tripType { get; set; }
    public TripType tripTypeID { get; set; }
    public string originPovinceName { get; set; }
    public string originCityName { get; set; }
    public string destinationPovinceName { get; set; }
    public string destinationCityName { get; set; }
    public int[] availableSeatsnumber { get; set; }
    public string taxiSupervisorName { get; set; }
    public int taxiSupervisorID { get; set; }
    public string carModelName { get; set; }
    public int carModelID { get; set; }
    public int originLocationID { get; set; }
    public string oringinLocationName { get; set; }
    public int destinationLocationID { get; set; }
    public string destinationLocationName { get; set; }
    public int? availableTicketsCount { get; set; }
    
    [JsonPropertyName("image")]
    public string Image { get; set; }
  }
}
