using System.Text.Json.Serialization;

namespace Application.Models.Payment
{
    public class ZarinpalPaymentRequest
    {
        [JsonPropertyName("merchant_id")]
        public string MerchantId { get; set; } = string.Empty;

        [JsonPropertyName("amount")]
        public int Amount { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("callback_url")]
        public string? CallbackUrl { get; set; }

        [JsonPropertyName("metadata")]
        public ZarinpalMetadata? Metadata { get; set; }
    }

    public class ZarinpalMetadata
    {
        [JsonPropertyName("mobile")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Mobile { get; set; }

        [JsonPropertyName("email")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Email { get; set; }
    }
}
