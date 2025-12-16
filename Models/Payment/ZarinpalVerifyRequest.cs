using System.Text.Json.Serialization;

namespace Application.Models.Payment
{
    public class ZarinpalVerifyRequest
    {
        [JsonPropertyName("merchant_id")]
        public string MerchantId { get; set; }

        [JsonPropertyName("amount")]
        public int Amount { get; set; }

        [JsonPropertyName("authority")]
        public string Authority { get; set; }
    }
}
