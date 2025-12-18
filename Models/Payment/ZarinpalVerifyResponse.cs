using System.Text.Json.Serialization;

namespace Application.Models.Payment
{
    public partial class ZarinpalVerifyResponse
    {
        [JsonPropertyName("data")]
        public ZarinpalVerifyData? Data { get; set; }

        [JsonPropertyName("errors")]
        public object? Errors { get; set; } // Can be array [] or object
        
        // Helper to get typed error data
        public ZarinpalErrorData? GetErrorData()
        {
            if (Errors is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                return System.Text.Json.JsonSerializer.Deserialize<ZarinpalErrorData>(jsonElement.GetRawText());
            }
            return null;
        }
    }

    public class ZarinpalVerifyData
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("card_hash")]
        public string CardHash { get; set; }

        [JsonPropertyName("card_pan")]
        public string CardPan { get; set; }

        [JsonPropertyName("ref_id")]
        public long RefId { get; set; }

        [JsonPropertyName("fee_type")]
        public string FeeType { get; set; }

        [JsonPropertyName("fee")]
        public int Fee { get; set; }
    }

    // For backward compatibility
    public partial class ZarinpalVerifyResponse
    {
        // Legacy properties for old code
        public int Status => Data?.Code ?? GetErrorData()?.Code ?? 0;
        public long RefId => Data?.RefId ?? 0;
        public string? CardPan => Data?.CardPan;
    }
}
