using Application.Models.Payment;
using System.Text;
using System.Text.Json;

namespace Application.Services.Payment
{
    public class ZarinpalService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ZarinpalService> _logger;
        private readonly string _merchantId;
        private readonly string _paymentUrl;
        private readonly string _verifyUrl;
        private readonly string _gatewayUrl;
        private readonly string _callbackUrl;
        private readonly bool _useMockPayment;
        private readonly bool _forceShowSandboxGateway;

        public ZarinpalService(
            HttpClient httpClient, 
            IConfiguration configuration,
            ILogger<ZarinpalService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            
            _merchantId = configuration["Zarinpal:MerchantId"];

            // Read explicit URLs from configuration (may be empty in some envs)
            var configuredPaymentUrl = configuration["Zarinpal:PaymentUrl"];
            var configuredVerifyUrl = configuration["Zarinpal:VerifyUrl"];
            var configuredGatewayUrl = configuration["Zarinpal:PaymentGatewayUrl"];

            // Sandbox flag (use sandbox endpoints when true)
            var isSandbox = configuration.GetValue<bool>("Zarinpal:IsSandbox", false);

            if (isSandbox)
            {
                // Use sandbox defaults when not provided in configuration
                _paymentUrl = string.IsNullOrWhiteSpace(configuredPaymentUrl)
                    ? "https://sandbox.zarinpal.com/pg/v4/payment/request.json"
                    : configuredPaymentUrl;

                _verifyUrl = string.IsNullOrWhiteSpace(configuredVerifyUrl)
                    ? "https://sandbox.zarinpal.com/pg/v4/payment/verify.json"
                    : configuredVerifyUrl;

                _gatewayUrl = string.IsNullOrWhiteSpace(configuredGatewayUrl)
                    ? "https://sandbox.zarinpal.com/pg/StartPay/"
                    : configuredGatewayUrl;

                _logger.LogInformation("Zarinpal: running in SANDBOX mode. PaymentUrl={Url}, VerifyUrl={Verify}, Gateway={Gateway}", _paymentUrl, _verifyUrl, _gatewayUrl);
            }
            else
            {
                // Use production defaults when not provided
                _paymentUrl = string.IsNullOrWhiteSpace(configuredPaymentUrl)
                    ? "https://payment.zarinpal.com/pg/v4/payment/request.json"
                    : configuredPaymentUrl;

                _verifyUrl = string.IsNullOrWhiteSpace(configuredVerifyUrl)
                    ? "https://payment.zarinpal.com/pg/v4/payment/verify.json"
                    : configuredVerifyUrl;

                _gatewayUrl = string.IsNullOrWhiteSpace(configuredGatewayUrl)
                    ? "https://payment.zarinpal.com/pg/StartPay/"
                    : configuredGatewayUrl;
            }

            _callbackUrl = configuration["Zarinpal:CallbackUrl"];
            _useMockPayment = configuration.GetValue<bool>("Zarinpal:UseMockPayment", false);
            // Dev-only flag: when true and running in mock mode, still perform a real request to sandbox/payment endpoint
            // and redirect user to the real gateway URL. Useful when you want to see Zarinpal sandbox UI locally.
            _forceShowSandboxGateway = configuration.GetValue<bool>("Zarinpal:ForceShowSandboxGateway", false);

            if (_useMockPayment)
            {
                _logger.LogWarning("⚠️ MOCK PAYMENT MODE ENABLED - No real payments will be processed!");
                if (_forceShowSandboxGateway)
                {
                    _logger.LogWarning("⚠️ ForceShowSandboxGateway is enabled - will call Zarinpal endpoints but verification may still be mocked.");
                }
            }
        }

        /// <summary>
        /// Request payment from Zarinpal
        /// </summary>
        /// <param name="amount">Amount in Rials (Toman * 10)</param>
        /// <param name="description">Payment description</param>
        /// <param name="mobile">Customer mobile</param>
        /// <param name="email">Customer email (optional)</param>
        /// <returns>Payment authority and gateway URL</returns>
        public async Task<(bool Success, string Authority, string Message)> RequestPaymentAsync(
            int amount, 
            string description, 
            string mobile, 
            string email = null)
        {
            // If mock mode is enabled but developer requested to show the real sandbox gateway,
            // perform the real request to the configured payment endpoint. This allows showing
            // the sandbox StartPay page while keeping verification mocked (useful for local dev).
            if (_useMockPayment && !_forceShowSandboxGateway)
            {
                var mockAuthority = $"MOCK-{Guid.NewGuid():N}".Substring(0, 36);
                _logger.LogInformation("🧪 MOCK: Payment request created. Authority: {Authority}, Amount: {Amount}", 
                    mockAuthority, amount);
                return (true, mockAuthority, "درخواست پرداخت تستی با موفقیت ثبت شد");
            }

            try
            {
                var request = new ZarinpalPaymentRequest
                {
                    MerchantId = _merchantId,
                    Amount = amount,
                    Description = description,
                    CallbackUrl = _callbackUrl,
                    Mobile = mobile,
                    Email = email
                };

                var json = JsonSerializer.Serialize(request);
                _logger.LogInformation("Zarinpal Payment Request JSON: {Json}", json);
                _logger.LogInformation("Zarinpal Payment URL: {Url}", _paymentUrl);
                _logger.LogInformation("Zarinpal Callback URL: {CallbackUrl}", _callbackUrl);
                
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_paymentUrl, content);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                _logger.LogInformation("Zarinpal HTTP Status: {StatusCode}", response.StatusCode);
                _logger.LogInformation("Zarinpal Payment Response (first 500 chars): {Response}", 
                    responseContent.Length > 500 ? responseContent.Substring(0, 500) : responseContent);

                // Check if response is HTML (error page)
                if (responseContent.TrimStart().StartsWith("<") || responseContent.TrimStart().StartsWith("<!DOCTYPE"))
                {
                    _logger.LogError("Zarinpal returned HTML instead of JSON. Response: {Response}", responseContent);
                    return (false, null, "خطا در ارتباط با درگاه پرداخت. لطفاً بعداً مجدداً تلاش کنید.");
                }

                // Check HTTP status
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Zarinpal HTTP error. Status: {Status}, Response: {Response}", 
                        response.StatusCode, responseContent);
                    return (false, null, $"خطا در ارتباط با درگاه پرداخت (کد: {response.StatusCode})");
                }

                ZarinpalPaymentResponse result;
                try
                {
                    result = JsonSerializer.Deserialize<ZarinpalPaymentResponse>(responseContent);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to parse Zarinpal response. Response: {Response}", responseContent);
                    return (false, null, "خطا در پردازش پاسخ درگاه پرداخت");
                }

                // Status codes for API v4:
                // 100 = Success
                // 101 = Already verified
                // Negative codes = Error
                if (result?.Data != null && result.Data.Code == 100 && !string.IsNullOrEmpty(result.Data.Authority))
                {
                    _logger.LogInformation("Zarinpal payment request successful. Authority: {Authority}", result.Data.Authority);
                    return (true, result.Data.Authority, "درخواست پرداخت با موفقیت ثبت شد");
                }
                else if (result?.Errors != null)
                {
                    var errorMessage = result.Errors.Message ?? GetErrorMessage(result.Errors.Code);
                    _logger.LogError("Zarinpal payment request failed. Code: {Code}, Error: {Error}", 
                        result.Errors.Code, errorMessage);
                    return (false, null, errorMessage);
                }
                else
                {
                    _logger.LogError("Zarinpal returned unexpected response format");
                    return (false, null, "خطای نامشخص در درگاه پرداخت");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error during Zarinpal payment request");
                return (false, null, "خطا در ارتباط با سرور درگاه پرداخت. لطفاً اتصال اینترنت خود را بررسی کنید.");
            }
            //catch (TaskCanceledException ex)
            //{
            //    _logger.LogError(ex, "Timeout during Zarinpal payment request");
            //    return (false, null, "زمان اتصال به درگاه پرداخت به پایان رسید. لطفاً دوباره تلاش کنید.");
            //}
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during Zarinpal payment request");
                return (false, null, $"خطا در ارسال درخواست: {ex.Message}");
            }
        }

        /// <summary>
        /// Verify payment with Zarinpal
        /// </summary>
        /// <param name="authority">Payment authority from callback</param>
        /// <param name="amount">Amount in Rials (must match original amount)</param>
        /// <returns>Verification result with RefId and card info</returns>
        public async Task<(bool Success, long RefId, string CardPan, string Message)> VerifyPaymentAsync(
            string authority, 
            int amount)
        {
            // 🧪 MOCK PAYMENT MODE for localhost testing
            if (_useMockPayment && !_forceShowSandboxGateway)
            {
                var mockRefId = new Random().NextInt64(100000000, 999999999);
                _logger.LogInformation("🧪 MOCK: Payment verified. RefId: {RefId}, Authority: {Authority}", 
                    mockRefId, authority);
                return (true, mockRefId, "6219-86**-****-1234", "پرداخت تستی با موفقیت انجام شد");
            }

            try
            {
                var request = new ZarinpalVerifyRequest
                {
                    MerchantId = _merchantId,
                    Amount = amount,
                    Authority = authority
                };

                var json = JsonSerializer.Serialize(request);
                _logger.LogInformation("Zarinpal Verify Request JSON: {Json}", json);
                
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_verifyUrl, content);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                _logger.LogInformation("Zarinpal Verify Response: {Response}", responseContent);

                ZarinpalVerifyResponse result;
                try
                {
                    result = JsonSerializer.Deserialize<ZarinpalVerifyResponse>(responseContent);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to parse Zarinpal verify response. Response: {Response}", responseContent);
                    return (false, 0, null, "خطا در پردازش پاسخ تایید پرداخت");
                }

                // Status codes for API v4:
                // 100 = Success
                // 101 = Already verified
                if (result?.Data != null && (result.Data.Code == 100 || result.Data.Code == 101))
                {
                    _logger.LogInformation("Payment verified successfully. RefId: {RefId}, CardPan: {CardPan}", 
                        result.Data.RefId, result.Data.CardPan);
                    return (true, result.Data.RefId, result.Data.CardPan, "پرداخت با موفقیت انجام شد");
                }
                else if (result?.Errors != null)
                {
                    var errorMessage = result.Errors.Message ?? GetErrorMessage(result.Errors.Code);
                    _logger.LogError("Zarinpal verification failed. Code: {Code}, Error: {Error}", 
                        result.Errors.Code, errorMessage);
                    return (false, 0, null, errorMessage);
                }
                else
                {
                    _logger.LogError("Zarinpal verify returned unexpected response format");
                    return (false, 0, null, "خطای نامشخص در تایید پرداخت");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during Zarinpal payment verification");
                return (false, 0, null, $"خطا در تایید پرداخت: {ex.Message}");
            }
        }

        /// <summary>
        /// Get payment gateway URL
        /// </summary>
        public string GetPaymentGatewayUrl(string authority)
        {
            // If mock mode is enabled but developer requested to show real gateway, return gateway URL
            if (_useMockPayment && _forceShowSandboxGateway)
            {
                return $"{_gatewayUrl}{authority}";
            }

            // 🧪 MOCK PAYMENT MODE - redirect to local mock payment page
            if (_useMockPayment)
            {
                return $"/Payment/MockGateway?authority={authority}";
            }
            
            return $"{_gatewayUrl}{authority}";
        }

        /// <summary>
        /// Check if mock payment is enabled
        /// </summary>
        public bool IsMockPaymentEnabled => _useMockPayment;

        /// <summary>
        /// Get Persian error message based on Zarinpal error code
        /// </summary>
        private string GetErrorMessage(int status)
        {
            return status switch
            {
                -1 => "اطلاعات ارسال شده ناقص است",
                -2 => "IP یا مرچنت کد پذیرنده صحیح نیست",
                -3 => "با توجه به محدودیت‌های شاپرک امکان پرداخت با رقم درخواست شده میسر نمی‌باشد",
                -4 => "سطح تایید پذیرنده پایین‌تر از سطح نقره‌ای است",
                -11 => "درخواست مورد نظر یافت نشد",
                -12 => "امکان ویرایش درخواست میسر نمی‌باشد",
                -14 => "دامنه callback با دامنه ثبت شده مطابقت ندارد",
                -21 => "هیچ نوع عملیات مالی برای این تراکنش یافت نشد",
                -22 => "تراکنش ناموفق می‌باشد",
                -33 => "رقم تراکنش با رقم پرداخت شده مطابقت ندارد",
                -34 => "سقف تقسیم تراکنش از لحاظ تعداد یا رقم عبور نموده است",
                -40 => "اجازه دسترسی به متد مربوطه وجود ندارد",
                -41 => "اطلاعات ارسال شده مربوط به AdditionalData غیرمعتبر می‌باشد",
                -42 => "مدت زمان معتبر طول عمر شناسه پرداخت باید بین 30 دقیقه تا 45 روز می‌باشد",
                -54 => "درخواست مورد نظر آرشیو شده است",
                100 => "عملیات با موفقیت انجام شد",
                101 => "عملیات پرداخت موفق بوده و قبلاً تایید شده است",
                _ => $"خطای نامشخص (کد: {status})"
            };
        }
    }
}
