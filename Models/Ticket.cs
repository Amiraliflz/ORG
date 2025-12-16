using System;
namespace Application.Models
{
  public class Ticket
  {

    public int Id { get; set; }
    public string Tripcode { get; set; }
    public string TicketCode { get; set; }
    public string Firstname { get; set; }
    public string Lastname { get; set; }
    public string Gender { set; get; }
    public string PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string NaCode { get; set; }
    public DateTime DOB { get; set; }
    public int TicketOriginalPrice { get; set; }
    public int TicketFinalPrice { get; set; }
    public string TripOrigin { get; set; }
    public string TripDestination { get; set; }
    public DateTime RegisteredAt { get; set; }
    public bool IsCancelled { get; set; }

    public string ServiceName { set; get; }
    public string CarName { get; set; }

    // Payment Information
    public bool IsPaid { get; set; } = false;
    public string? PaymentAuthority { get; set; }
    public string? PaymentRefId { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? CardPan { get; set; }

    // nav prop
    public Agency Agency { get; set; }
  }
}
