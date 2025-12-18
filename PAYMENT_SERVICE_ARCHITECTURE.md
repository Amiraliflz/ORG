# Payment Service Architecture

## Overview
The payment system has been refactored to follow **SOLID principles**, particularly the **Dependency Inversion Principle (DIP)**. This allows easy integration of multiple payment gateways without modifying existing code.

## Architecture

### Interface-Based Design
```
┌─────────────────────────┐
│   PaymentController     │
│  (Depends on Interface) │
└───────────┬─────────────┘
            │
            │ depends on
            ▼
┌─────────────────────────┐
│   IPaymentService       │
│   (Interface/Contract)  │
└───────────┬─────────────┘
            │
            │ implemented by
            ▼
┌──────────────────┬──────────────────┬──────────────────┐
│ ZarinpalService  │  IdpayService    │  SepService      │
│  (Implemented)   │   (Example)      │   (Future)       │
└──────────────────┴──────────────────┴──────────────────┘
```

### Key Benefits
✅ **Dependency Inversion**: High-level modules (Controller) depend on abstractions (IPaymentService), not concrete implementations  
✅ **Open/Closed**: Open for extension (add new gateways), closed for modification (no controller changes)  
✅ **Single Responsibility**: Each service handles one payment gateway  
✅ **Easy Testing**: Can mock IPaymentService for unit tests  
✅ **Flexibility**: Switch payment providers by changing DI registration only  

## Implementation

### 1. Interface (IPaymentService.cs)
```csharp
public interface IPaymentService
{
    Task<(bool Success, string Authority, string Message)> RequestPaymentAsync(...);
    Task<(bool Success, long RefId, string CardPan, string Message)> VerifyPaymentAsync(...);
    string GetPaymentGatewayUrl(string authority);
}
```

### 2. Zarinpal Implementation (ZarinpalService.cs)
- Implements `IPaymentService`
- Handles Zarinpal API v4
- Supports both production and sandbox modes
- **Merchant validation with logging**:
  - Validates MerchantId is configured
  - Validates MerchantId format (36 characters UUID)
  - Validates CallbackUrl is configured
  - Logs warnings/errors for configuration issues

### 3. Dependency Injection (Program.cs)
```csharp
builder.Services.AddHttpClient<IPaymentService, ZarinpalService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

### 4. Controller Usage (PaymentController.cs)
```csharp
private readonly IPaymentService _paymentService;

public PaymentController(IPaymentService paymentService, ...)
{
    _paymentService = paymentService;
}
```

## Changes Made

### ✅ Removed
- ❌ All mock payment functionality (`UseMockPayment`, `ForceShowSandboxGateway`)
- ❌ Mock gateway controller actions (`MockGateway`, `ProcessMockPayment`)
- ❌ Mock payment configuration in appsettings.json
- ❌ Direct dependency on concrete `ZarinpalService` in controller

### ✅ Added
- ✅ `IPaymentService` interface for payment abstraction
- ✅ Merchant ID validation with detailed logging
- ✅ Configuration validation (MerchantId, CallbackUrl)
- ✅ Example `IdpayService.cs.example` showing how to add new gateways
- ✅ Proper nullable reference type handling

### ✅ Improved
- ✅ Better error messages and logging
- ✅ Cleaner configuration (removed mock-related settings)
- ✅ Type safety with nullable annotations
- ✅ Follows SOLID principles

## Merchant Validation

The service now validates merchant configuration on startup:

```csharp
// ❌ Missing MerchantId
LogError: "❌ Zarinpal MerchantId is not configured!"
Throws: InvalidOperationException

// ⚠️ Invalid MerchantId format  
LogWarning: "⚠️ Zarinpal MerchantId format may be incorrect. Expected 36 characters..."

// ⚠️ Missing CallbackUrl
LogWarning: "⚠️ Zarinpal CallbackUrl is not configured. Payment verification may fail."
```

## Configuration

### Production (appsettings.json)
```json
{
  "Zarinpal": {
    "MerchantId": "a3348b1d-3593-4aa0-922c-f539bf8f9ae3",
    "IsSandbox": false,
    "PaymentUrl": "https://payment.zarinpal.com/pg/v4/payment/request.json",
    "VerifyUrl": "https://payment.zarinpal.com/pg/v4/payment/verify.json",
    "PaymentGatewayUrl": "https://payment.zarinpal.com/pg/StartPay/",
    "CallbackUrl": "https://mrshoofer.ir/Payment/Verify",
    "Description": "خرید بلیط سواری مِسترشوفر"
  }
}
```

### Development (appsettings.Development.json)
```json
{
  "Zarinpal": {
    "MerchantId": "a3348b1d-3593-4aa0-922c-f539bf8f9ae3",
    "IsSandbox": true,
    "PaymentUrl": "https://sandbox.zarinpal.com/pg/v4/payment/request.json",
    "VerifyUrl": "https://sandbox.zarinpal.com/pg/v4/payment/verify.json",
    "PaymentGatewayUrl": "https://sandbox.zarinpal.com/pg/StartPay/",
    "CallbackUrl": "http://localhost:5055/Payment/Verify",
    "Description": "خرید بلیط مستر شوفر - تست محلی"
  }
}
```

## Adding New Payment Gateway

To add a new payment gateway (e.g., IDPay, Sep, Mellat):

### 1. Create Service Class
```csharp
public class IdpayService : IPaymentService
{
    // Implement interface methods
    public async Task<(bool Success, string Authority, string Message)> RequestPaymentAsync(...)
    {
        // Call Idpay API
    }
    
    public async Task<(bool Success, long RefId, string CardPan, string Message)> VerifyPaymentAsync(...)
    {
        // Call Idpay verification API
    }
    
    public string GetPaymentGatewayUrl(string authority)
    {
        return $"https://idpay.ir/p/ws/{authority}";
    }
}
```

### 2. Update DI Registration
```csharp
// In Program.cs, change only this line:
builder.Services.AddHttpClient<IPaymentService, IdpayService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

### 3. Add Configuration
```json
{
  "Idpay": {
    "ApiKey": "your-api-key",
    "CallbackUrl": "https://yoursite.com/Payment/Verify"
  }
}
```

**No changes needed** in `PaymentController` or any other code! ✨

## Testing

For development/testing, use Zarinpal sandbox mode:
- Set `"IsSandbox": true` in `appsettings.Development.json`
- Use sandbox URLs (automatically configured)
- Test with Zarinpal sandbox credentials

## Migration Notes

If you had code depending on the old `ZarinpalService` directly:
1. Change constructor parameter from `ZarinpalService` to `IPaymentService`
2. Remove any usage of `.IsMockPaymentEnabled` property
3. Remove references to mock gateway URLs

## References

- [SOLID Principles](https://en.wikipedia.org/wiki/SOLID)
- [Dependency Inversion Principle](https://en.wikipedia.org/wiki/Dependency_inversion_principle)
- [Zarinpal API Documentation](https://docs.zarinpal.com/)
