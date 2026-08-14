namespace Application.Services.MrShooferORS
{
  public sealed class CancelTicketResult
  {
    public bool Success { get; init; }
    public decimal RefundAmount { get; init; }
    public string? ErrorMessage { get; init; }

    public static CancelTicketResult Ok(decimal refundAmount) => new()
    {
      Success = true,
      RefundAmount = refundAmount
    };

    public static CancelTicketResult Fail(string errorMessage) => new()
    {
      Success = false,
      ErrorMessage = errorMessage
    };
  }
}
