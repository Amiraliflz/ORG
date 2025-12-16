using System.Text.Json.Serialization;

namespace Application.Models.Payment
{
    public partial class ZarinpalPaymentResponse
    {
        [JsonPropertyName("data")]
        public ZarinpalPaymentData Data { get; set; }

        [JsonPropertyName("errors")]
        public ZarinpalErrorData Errors { get; set; }
    }

    public class ZarinpalPaymentData
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("authority")]
        public string Authority { get; set; }

        [JsonPropertyName("fee_type")]
        public string FeeType { get; set; }

        [JsonPropertyName("fee")]
        public int Fee { get; set; }
    }

    public class ZarinpalErrorData
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("validations")]
        public object[] Validations { get; set; }
    }

    // For backward compatibility
    public partial class ZarinpalPaymentResponse
    {
        // Legacy properties for old code
        public int Status => Data?.Code ?? Errors?.Code ?? 0;
        public string Authority => Data?.Authority;
    }
}
