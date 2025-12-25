namespace Application.Services.MrShooferORS
{
  public class TicketConfirmationResponse
  {
    public string message { get; set; }
    public string ticketCode { get; set; }

    // Optional webapp token returned by ORS when present
    public string? webappToken { get; set; }

    public decimal remainAccountBalance { set { remainAccountBalance_int = (int)value; } }
    public int remainAccountBalance_int { set; get; }

    public int paid_total_fee_tomans { get; set; }
    public string seatnumber { get; set; }
    public bool isprivateTrip { get; set; }
  }
}
