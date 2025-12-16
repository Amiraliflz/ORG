# Zarinpal Integration Fix Summary

## 🎯 Problem
Zarinpal checkout page wasn't loading when trying to reserve a ticket.

## 🔍 Root Causes Identified

### 1. **Wrong API Endpoints**
- Used: `https://sandbox.zarinpal.com/pg/v4/payment/request.json`
- Correct: `https://sandbox.zarinpal.com/pg/rest/WebGate/PaymentRequest.json`

### 2. **Wrong JSON Property Names**
- Used: snake_case (`merchant_id`, `amount`, `callback_url`)
- Correct: PascalCase (`MerchantID`, `Amount`, `CallbackURL`)

### 3. **Wrong Response Structure**
- Expected: Nested `data.code` structure
- Correct: Flat `Status` structure

## ✅ Files Modified

### 1. `appsettings.json`
```diff
- "PaymentUrl": "https://sandbox.zarinpal.com/pg/v4/payment/request.json",
- "VerifyUrl": "https://sandbox.zarinpal.com/pg/v4/payment/verify.json",
+ "PaymentUrl": "https://sandbox.zarinpal.com/pg/rest/WebGate/PaymentRequest.json",
+ "VerifyUrl": "https://sandbox.zarinpal.com/pg/rest/WebGate/PaymentVerification.json",
```

### 2. `Models/Payment/ZarinpalPaymentRequest.cs`
```diff
- [JsonPropertyName("merchant_id")]
+ [JsonPropertyName("MerchantID")]
  public string MerchantId { get; set; }

- [JsonPropertyName("amount")]
+ [JsonPropertyName("Amount")]
  public int Amount { get; set; }

- [JsonPropertyName("description")]
+ [JsonPropertyName("Description")]
  public string Description { get; set; }

- [JsonPropertyName("callback_url")]
+ [JsonPropertyName("CallbackURL")]
  public string CallbackUrl { get; set; }

- [JsonPropertyName("metadata")]
- public PaymentMetadata Metadata { get; set; }
+ [JsonPropertyName("Mobile")]
+ public string Mobile { get; set; }

+ [JsonPropertyName("Email")]
+ public string Email { get; set; }
```

### 3. `Models/Payment/ZarinpalPaymentResponse.cs`
```diff
- [JsonPropertyName("data")]
- public ZarinpalPaymentData Data { get; set; }
+ [JsonPropertyName("Status")]
+ public int Status { get; set; }

+ [JsonPropertyName("Authority")]
+ public string Authority { get; set; }

  [JsonPropertyName("Errors")]
- public List<string> Errors { get; set; }
+ public List<string> Errors { get; set; }
```

### 4. `Models/Payment/ZarinpalVerifyRequest.cs`
```diff
- [JsonPropertyName("merchant_id")]
+ [JsonPropertyName("MerchantID")]
  public string MerchantId { get; set; }

- [JsonPropertyName("amount")]
+ [JsonPropertyName("Amount")]
  public int Amount { get; set; }

- [JsonPropertyName("authority")]
+ [JsonPropertyName("Authority")]
  public string Authority { get; set; }
```

### 5. `Models/Payment/ZarinpalVerifyResponse.cs`
```diff
- [JsonPropertyName("data")]
- public ZarinpalVerifyData Data { get; set; }
+ [JsonPropertyName("Status")]
+ public int Status { get; set; }

+ [JsonPropertyName("RefID")]
+ public long RefId { get; set; }

+ [JsonPropertyName("CardPan")]
+ public string CardPan { get; set; }

+ [JsonPropertyName("CardHash")]
+ public string CardHash { get; set; }
```

### 6. `Services/Payment/ZarinpalService.cs`
**Major Changes:**
- Added `ILogger<ZarinpalService>` for comprehensive logging
- Updated to use flat response structure (`result.Status` instead of `result.Data.Code`)
- Added logging for request/response JSON
- Changed metadata handling to direct properties

**Key Code Changes:**
```diff
  var request = new ZarinpalPaymentRequest
  {
      MerchantId = _merchantId,
      Amount = amount,
      Description = description,
      CallbackUrl = _callbackUrl,
-     Metadata = new PaymentMetadata
-     {
-         Mobile = mobile,
-         Email = email
-     }
+     Mobile = mobile,
+     Email = email
  };

+ var json = JsonSerializer.Serialize(request);
+ _logger.LogInformation("Zarinpal Payment Request JSON: {Json}", json);

- if (result?.Data?.Code == 100)
+ if (result?.Status == 100 && !string.IsNullOrEmpty(result.Authority))
  {
-     return (true, result.Data.Authority, "درخواست پرداخت با موفقیت ثبت شد");
+     return (true, result.Authority, "درخواست پرداخت با موفقیت ثبت شد");
  }
```

### 7. `Program.cs`
```diff
- builder.Services.AddHttpClient<ZarinpalService>();
+ builder.Services.AddHttpClient<ZarinpalService>(client =>
+ {
+     client.Timeout = TimeSpan.FromSeconds(30);
+ });
```

## 🧪 Testing Instructions

### 1. Start the application
```bash
dotnet run
```

### 2. Navigate to trip reservation
- Go to a trip listing
- Fill out the reservation form
- Click "Confirm Payment"

### 3. Expected Behavior
You should see in logs:
```
ConfirmInfo POST started for trip: XXX
Temporary reserve code: YYY
Ticket confirmed: ZZZ
Ticket saved to database
Requesting Zarinpal payment. Amount in Rials: 100000
Zarinpal Payment Request JSON: {"MerchantID":"...","Amount":100000,...}
Zarinpal Payment Response: {"Status":100,"Authority":"..."}
Zarinpal payment request successful. Authority: AAA
Redirecting to Zarinpal. URL: https://sandbox.zarinpal.com/pg/StartPay/AAA
```

Browser should redirect to: `https://sandbox.zarinpal.com/pg/StartPay/{Authority}`

### 4. Complete Payment on Zarinpal
Use test card:
- **Card**: 6104-3388-0000-0000
- **CVV2**: 123
- **Expiry**: 12/25
- **OTP**: 1234

### 5. Verify Callback
After payment, Zarinpal redirects to: `/Payment/Verify?Authority=XXX&Status=OK`

Expected logs:
```
Payment verified successfully. TicketCode: ZZZ, RefId: 12345
```

## 📊 API Format Reference

### Request Format (Correct)
```json
{
  "MerchantID": "a3348b1d-3593-4aa0-922c-f539bf8f9ae3",
  "Amount": 100000,
  "Description": "خرید بلیط",
  "CallbackURL": "https://mrshoofer.ir/Payment/Verify",
  "Mobile": "09123456789",
  "Email": null
}
```

### Success Response (Correct)
```json
{
  "Status": 100,
  "Authority": "A00000000000000000000000000123456789"
}
```

### Error Response
```json
{
  "Status": -2,
  "Authority": null,
  "Errors": []
}
```

## 🚀 Production Checklist

Before deploying to production:

- [ ] Change API URLs from `sandbox.zarinpal.com` to `www.zarinpal.com`
- [ ] Update `MerchantId` to production merchant ID
- [ ] Verify `CallbackUrl` is publicly accessible
- [ ] Test with real payment card
- [ ] Monitor logs for any errors
- [ ] Set up error alerting

## 📝 Notes

1. **Amount**: Always in Rials (Toman × 10). Minimum 1000 Rials.
2. **Authority**: Must be saved to database for verification
3. **Verification**: Must use same amount as request
4. **Callback URL**: Must be HTTPS and publicly accessible
5. **Test Environment**: Use sandbox URLs and test merchant ID

## 🔗 References

- Zarinpal REST API: https://docs.zarinpal.com/paymentGateway/
- Sandbox Dashboard: https://sandbox.zarinpal.com/
- Test Cards: https://docs.zarinpal.com/paymentGateway/sandbox.html
