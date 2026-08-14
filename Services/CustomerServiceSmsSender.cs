using IPE.SmsIrClient;
using IPE.SmsIrClient.Models.Requests;
using System.Text;
using System.Text.Json;

namespace Application.Services
{
    public class CustomerServiceSmsSender
    {
        private readonly SmsIr _smsIr;
        private readonly string _apiKey;
        private readonly long _lineNumber;
        private readonly HttpClient _http;

        public CustomerServiceSmsSender(IConfiguration configuration, HttpClient httpClient)
        {
            _apiKey = configuration["smsirapikey"] ?? string.Empty;
            _smsIr = new SmsIr(_apiKey);
            _lineNumber = configuration.GetValue<long>("SmsIr:LineNumber", 300028288561L);
            _http = httpClient;
            _http.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", _apiKey);
        }

        public async Task SendCustomerTicket_issued(string firstname, string lastname, string reference, string link, string numberphone)
        {
            static string Truncate(string? value, int maxLength = 25)
            {
                if (string.IsNullOrEmpty(value)) return string.Empty;
                if (value.Length <= maxLength) return value;
                Console.WriteLine($"[SMS] Truncating parameter from {value.Length} to {maxLength} chars.");
                return value[..maxLength];
            }

            VerifySendParameter[] parameters =
            [
                new VerifySendParameter("FIRSTNAME", Truncate(firstname)),
                new VerifySendParameter("LASTNAME",  Truncate(lastname)),
                new VerifySendParameter("TRIP",      Truncate(link)),
                new VerifySendParameter("REFERENCE", Truncate(reference)),
            ];

            try
            {
                var response = await _smsIr.VerifySendAsync(numberphone, 782252, parameters);
                if (response == null)
                    Console.WriteLine("[SMS] VerifySend returned null response.");
            }
            catch (IPE.SmsIrClient.Exceptions.LogicalException lex)
            {
                Console.WriteLine($"[SMS] LogicalException: {lex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SMS] Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends a free-text bulk SMS. Reuses the injected HttpClient (connection pooled).
        /// Batches in groups of 100 as required by sms.ir API.
        /// </summary>
        public async Task SendBulk(string messageText, IEnumerable<string> mobiles)
        {
            var mobileList = mobiles.ToList();
            if (mobileList.Count == 0) return;

            foreach (var batch in mobileList.Chunk(100))
            {
                try
                {
                    var payload = new
                    {
                        lineNumber = _lineNumber,
                        messageText,
                        mobiles = batch,
                        sendDateTime = (long?)null
                    };

                    var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    var response = await _http.PostAsync("https://api.sms.ir/v1/send/bulk", content);

                    if (!response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"[SMS] Bulk failed. Status={response.StatusCode} Body={body}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SMS] Bulk exception: {ex.Message}");
                }
            }
        }

        public string BuildDiscountMessage(string discountCode, int percent)
        {
            return $"کد تخفیف اختصاصی شما از مستر‌شوفر:\n{discountCode}\n\nمیزان تخفیف: {percent}٪\n\nبرای رزرو سفر از این کد استفاده کنید.\n{Application.Services.Seo.SeoDefaults.PublicSiteHost}";
        }

        public async Task SendDiscountCode(string discountCode, int percent, IEnumerable<string> mobiles)
        {
            await SendBulk(BuildDiscountMessage(discountCode, percent), mobiles);
        }
    }
}
