using Application.Models.Payment;
using System.Text;
using System.Text.Json;

namespace Application.Services.Payment
{
  /// <summary>
  /// Zarinpal payment gateway implementation
  /// Supports both production and sandbox environments
  /// </summary>
  public class ZarinpalService : IPaymentService
  {
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ZarinpalService> _logger;
    private readonly string _merchantId;
    private readonly string _paymentUrl;
    private readonly string _verifyUrl;
    private readonly string _gatewayUrl;
    private readonly string? _callbackUrl;
    private readonly bool _isSandbox;
    private readonly bool _forceAuthorityOnError;

    public ZarinpalService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ZarinpalService> logger)
    {
      _httpClient = httpClient;
      _configuration = configuration;
      _logger = logger;

      _merchantId = configuration["Zarinpal:MerchantId"] ?? string.Empty;

      // Validate merchant ID
      if (string.IsNullOrWhiteSpace(_merchantId))
      {
        _logger.LogError("❌ Zarinpal MerchantId is not configured! Payment service will not work properly.");
        throw new InvalidOperationException("Zarinpal MerchantId is required but not configured in appsettings");
      }

      // Validate merchant ID format (should be 36 characters UUID)
      if (_merchantId.Length != 36)
      {
        _logger.LogWarning("⚠️ Zarinpal MerchantId format may be incorrect. Expected 36 characters UUID, got {Length} characters", _merchantId.Length);
      }

      // Read explicit URLs from configuration (may be empty in some envs)
      var configuredPaymentUrl = configuration["Zarinpal:PaymentUrl"];
      var configuredVerifyUrl = configuration["Zarinpal:VerifyUrl"];
      var configuredGatewayUrl = configuration["Zarinpal:PaymentGatewayUrl"];

      // Sandbox flag (use sandbox endpoints when true)
      _isSandbox = configuration.GetValue<bool>("Zarinpal:IsSandbox", false);

      if (_isSandbox)
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

        _logger.LogInformation("Zarinpal: running in PRODUCTION mode. Gateway={Gateway}", _gatewayUrl);
      }

      _callbackUrl = configuration["Zarinpal:CallbackUrl"];

      if (string.IsNullOrWhiteSpace(_callbackUrl))
      {
        _logger.LogWarning("⚠️ Zarinpal CallbackUrl is not configured. Payment verification may fail.");
      }

      // For local testing you can enable forcing a test authority when gateway returns rate-limit errors
      _forceAuthorityOnError = configuration.GetValue<bool>("Zarinpal:ForceAuthorityOnError", false);
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
        string? email = null,
        string? callbackUrl = null)
    {
      const int maxAttempts = 3;

      var request = new ZarinpalPaymentRequest
      {
        MerchantId = _merchantId,
        Amount = amount,
        Description = description,
        CallbackUrl = callbackUrl ?? _callbackUrl,
        Metadata = new ZarinpalMetadata
        {
          Mobile = string.IsNullOrWhiteSpace(mobile) ? null : mobile,
          Email = string.IsNullOrWhiteSpace(email) ? null : email
        }
      };

      var json = JsonSerializer.Serialize(request);
      _logger.LogInformation("Zarinpal payment request. MerchantId={MerchantId}, Amount={Amount}", _merchantId, amount);

      for (int attempt = 1; attempt <= maxAttempts; attempt++)
      {
        try
        {
          var content = new StringContent(json, Encoding.UTF8, "application/json");
          var response = await _httpClient.PostAsync(_paymentUrl, content);
          var responseContent = await response.Content.ReadAsStringAsync();

          _logger.LogInformation("Zarinpal attempt {Attempt}: HTTP {StatusCode}", attempt, response.StatusCode);

          // HTML response = gateway error page (sandbox down or blocked)
          if (responseContent.TrimStart().StartsWith("<"))
          {
            _logger.LogWarning("Zarinpal returned HTML on attempt {Attempt}", attempt);
            if (_isSandbox || _forceAuthorityOnError)
            {
              var testAuth = "TEST-" + Guid.NewGuid().ToString()[..31];
              return (true, testAuth, "تست: درگاه شبیه‌ساز در دسترس نیست — authority آزمایشی ایجاد شد");
            }
            // Transient — retry on 5xx, give up on 4xx
            if (response.IsSuccessStatusCode || (int)response.StatusCode >= 500)
            {
              if (attempt < maxAttempts) { await Task.Delay(1500 * attempt); continue; }
            }
            return (false, string.Empty, "خطا در ارتباط با درگاه پرداخت. لطفاً بعداً مجدداً تلاش کنید.");
          }

          // 5xx = transient server error → retry
          if ((int)response.StatusCode >= 500)
          {
            _logger.LogWarning("Zarinpal returned {Status} on attempt {Attempt}", response.StatusCode, attempt);
            if (_isSandbox || _forceAuthorityOnError)
            {
              var testAuth = "TEST-" + Guid.NewGuid().ToString()[..31];
              return (true, testAuth, "تست: درگاه شبیه‌ساز در دسترس نیست — authority آزمایشی ایجاد شد");
            }
            if (attempt < maxAttempts) { await Task.Delay(1500 * attempt); continue; }
            return (false, string.Empty, $"خطا در ارتباط با درگاه پرداخت (کد: {response.StatusCode})");
          }

          // 4xx = non-transient HTTP error
          if (!response.IsSuccessStatusCode)
          {
            _logger.LogError("Zarinpal HTTP {Status} on attempt {Attempt}: {Response}", response.StatusCode, attempt, responseContent);
            return (false, string.Empty, $"خطا در ارتباط با درگاه پرداخت (کد: {response.StatusCode})");
          }

          ZarinpalPaymentResponse? result;
          try { result = JsonSerializer.Deserialize<ZarinpalPaymentResponse>(responseContent); }
          catch (JsonException ex)
          {
            _logger.LogError(ex, "Failed to parse Zarinpal response: {Response}", responseContent);
            return (false, string.Empty, "خطا در پردازش پاسخ درگاه پرداخت");
          }

          if (result?.Data != null && result.Data.Code == 100 && !string.IsNullOrEmpty(result.Data.Authority))
          {
            _logger.LogInformation("Zarinpal payment request successful. Authority: {Authority}", result.Data.Authority);
            return (true, result.Data.Authority, "درخواست پرداخت با موفقیت ثبت شد");
          }

          var errorData = result?.GetErrorData();
          if (errorData != null)
          {
            var mapped = GetErrorMessage(errorData.Code);

            var isTooManyAttempts = errorData.Code == -12 ||
                (!string.IsNullOrWhiteSpace(errorData.Message) && errorData.Message.Contains("To many", StringComparison.OrdinalIgnoreCase));

            if (isTooManyAttempts && (_isSandbox || _forceAuthorityOnError))
            {
              var testAuth = "TEST-" + Guid.NewGuid().ToString()[..31];
              _logger.LogWarning("Zarinpal rate-limit (code {Code}). Forcing test authority.", errorData.Code);
              return (true, testAuth, "درخواست پرداخت (تست) ایجاد شد. توجه: این تراکنش واقعی نیست");
            }

            var errorMessage = !mapped.StartsWith("خطای نامشخص") ? mapped
                : !string.IsNullOrWhiteSpace(errorData.Message) ? errorData.Message
                : GetErrorMessage(errorData.Code);

            _logger.LogError("Zarinpal error code {Code}: {Error}", errorData.Code, errorMessage);
            return (false, string.Empty, errorMessage);
          }

          _logger.LogError("Zarinpal unexpected response: {Response}", responseContent);
          return (false, string.Empty, "خطای نامشخص در درگاه پرداخت");
        }
        catch (TaskCanceledException ex)
        {
          _logger.LogWarning(ex, "Zarinpal timeout on attempt {Attempt}/{Max}", attempt, maxAttempts);
          if (_isSandbox || _forceAuthorityOnError)
          {
            var testAuth = "TEST-" + Guid.NewGuid().ToString()[..31];
            return (true, testAuth, "تست: درگاه شبیه‌ساز در دسترس نیست — authority آزمایشی ایجاد شد");
          }
          if (attempt < maxAttempts) { await Task.Delay(1500 * attempt); continue; }
          return (false, string.Empty, "زمان اتصال به درگاه پرداخت به پایان رسید. لطفاً دوباره تلاش کنید.");
        }
        catch (HttpRequestException ex)
        {
          _logger.LogWarning(ex, "Zarinpal network error on attempt {Attempt}/{Max}", attempt, maxAttempts);
          if (_isSandbox || _forceAuthorityOnError)
          {
            var testAuth = "TEST-" + Guid.NewGuid().ToString()[..31];
            return (true, testAuth, "تست: درگاه شبیه‌ساز در دسترس نیست — authority آزمایشی ایجاد شد");
          }
          if (attempt < maxAttempts) { await Task.Delay(1500 * attempt); continue; }
          return (false, string.Empty, "خطا در ارتباط با سرور درگاه پرداخت. لطفاً اتصال اینترنت خود را بررسی کنید.");
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Zarinpal exception on attempt {Attempt}", attempt);
          return (false, string.Empty, $"خطا در ارسال درخواست: {ex.Message}");
        }
      }

      // Should never reach here, but satisfies compiler
      return (false, string.Empty, "خطا در ارتباط با درگاه پرداخت");
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
      // Auto-pass test authorities generated when sandbox gateway was unavailable
      if ((_isSandbox || _forceAuthorityOnError) && authority.StartsWith("TEST-", StringComparison.OrdinalIgnoreCase))
      {
        var fakeRefId = new Random().Next(10000000, 99999999);
        _logger.LogWarning("Test authority detected in sandbox mode. Auto-passing verification. RefId={RefId}", fakeRefId);
        return (true, fakeRefId, "0000-****-****-0000", "تست: پرداخت آزمایشی تایید شد");
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

        ZarinpalVerifyResponse? result;
        try
        {
          result = JsonSerializer.Deserialize<ZarinpalVerifyResponse>(responseContent);
        }
        catch (JsonException ex)
        {
          _logger.LogError(ex, "Failed to parse Zarinpal verify response. Response: {Response}", responseContent);
          return (false, 0, string.Empty, "خطا در پردازش پاسخ تایید پرداخت");
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
        else
        {
          var errorData = result?.GetErrorData();
          if (errorData != null)
          {
            // Prefer a localized/friendly message for known error codes.
            var mapped = GetErrorMessage(errorData.Code);
            string errorMessage;

            if (!mapped.StartsWith("خطای نامشخص"))
            {
              errorMessage = mapped;
            }
            else if (!string.IsNullOrWhiteSpace(errorData.Message))
            {
              errorMessage = errorData.Message;
            }
            else
            {
              errorMessage = GetErrorMessage(errorData.Code);
            }

            _logger.LogError("Zarinpal verification failed. Code: {Code}, Error: {Error}",
                errorData.Code, errorMessage);
            return (false, 0, string.Empty, errorMessage);
          }

          _logger.LogError("Zarinpal verify returned unexpected response format. Response: {Response}", responseContent);
          return (false, 0, string.Empty, "خطای نامشخص در تایید پرداخت");
        }
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Exception during Zarinpal payment verification");
        return (false, 0, string.Empty, $"خطا در تایید پرداخت: {ex.Message}");
      }
    }

    /// <summary>
    /// Get payment gateway URL
    /// </summary>
    public string GetPaymentGatewayUrl(string authority)
    {
      return $"{_gatewayUrl}{authority}";
    }

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
