namespace Application.Services.Payment
{
    /// <summary>
    /// Payment service interface following Dependency Inversion Principle
    /// Allows easy implementation of different payment gateways (Zarinpal, IDPay, Sep, etc.)
    /// </summary>
    public interface IPaymentService
    {
        /// <summary>
        /// Request payment from the payment gateway
        /// </summary>
        /// <param name="amount">Amount in Rials</param>
        /// <param name="description">Payment description</param>
        /// <param name="mobile">Customer mobile number</param>
        /// <param name="email">Customer email (optional)</param>
        /// <returns>Success status, payment authority/token, and message</returns>
        Task<(bool Success, string Authority, string Message)> RequestPaymentAsync(
            int amount,
            string description,
            string mobile,
            string? email = null);

        /// <summary>
        /// Verify payment after user returns from payment gateway
        /// </summary>
        /// <param name="authority">Payment authority/token from callback</param>
        /// <param name="amount">Amount in Rials (must match original amount)</param>
        /// <returns>Success status, reference ID, card info, and message</returns>
        Task<(bool Success, long RefId, string CardPan, string Message)> VerifyPaymentAsync(
            string authority,
            int amount);

        /// <summary>
        /// Get the payment gateway URL to redirect user
        /// </summary>
        /// <param name="authority">Payment authority/token</param>
        /// <returns>Full URL to redirect user to payment gateway</returns>
        string GetPaymentGatewayUrl(string authority);
    }
}
