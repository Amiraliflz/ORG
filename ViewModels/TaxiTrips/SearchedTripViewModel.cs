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
    public string taxiSupervisorName { get; set; }
    public int taxiSupervisorID { get; set; }
    public string originalPrice { get; set; }
    public string afterdiscount { get; set; }
    public string carModelName { get; set; }
    public string Image { get; set; }
  }
}
