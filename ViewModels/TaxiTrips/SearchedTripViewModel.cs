using Org.BouncyCastle.Asn1.Cms;

namespace Application.ViewModels.TaxiTrips
{
  public class SearchedTripViewModel
  {
    public string tripcode { get; set; }
    public string origin { get; set; }
    public string destination { get; set; }
    public string startingDateTime { get; set; }
    public string arrivalDateTime { get; set; }
    /// <summary>True when estimated arrival falls on the calendar day after departure.</summary>
    public bool arrivesNextDay { get; set; }
    public string taxiSupervisorName { get; set; }
    public int taxiSupervisorID { get; set; }
    public string originalPrice { get; set; }
    public string afterdiscount { get; set; }
    public string carModelName { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("image")]
    public string Image { get; set; }
    /// <summary>Human-readable duration, e.g. «۵ ساعت و ۲۵ دقیقه».</summary>
    public string travelDuration { get; set; }
  }
}
