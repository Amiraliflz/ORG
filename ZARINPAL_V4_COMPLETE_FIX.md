# ✅ تمام تغییرات Zarinpal API v4

## 🎯 مشکل اصلی که حل شد

```
"The merchant id field is required." (code: -9)
```

**علت:** Property names در JSON اشتباه بود.

---

## 🔧 تغییرات انجام شده

### **1. ZarinpalPaymentRequest.cs**

```csharp
// ❌ قبلاً (اشتباه):
[JsonPropertyName("MerchantID")]
[JsonPropertyName("Amount")]
[JsonPropertyName("CallbackURL")]

// ✅ حالا (درست):
[JsonPropertyName("merchant_id")]    // snake_case
[JsonPropertyName("amount")]          // snake_case
[JsonPropertyName("callback_url")]    // snake_case
```

### **2. ZarinpalPaymentResponse.cs**

```csharp
// ساختار جدید v4:
{
  "data": {
    "code": 100,
    "message": "...",
    "authority": "A000..."
  },
  "errors": null
}

// یا در صورت خطا:
{
  "data": {},
  "errors": {
    "code": -9,
    "message": "The merchant id field is required.",
    "validations": []
  }
}
```

### **3. ZarinpalVerifyRequest.cs**

```csharp
[JsonPropertyName("merchant_id")]
[JsonPropertyName("amount")]
[JsonPropertyName("authority")]
```

### **4. ZarinpalVerifyResponse.cs**

```csharp
{
  "data": {
    "code": 100,
    "ref_id": 123456,
    "card_pan": "6219-86**-****-1234",
    "card_hash": "...",
    "fee": 0,
    "fee_type": "Merchant"
  },
  "errors": null
}
```

### **5. ZarinpalService.cs**

```csharp
// ✅ چک کردن response جدید:
if (result?.Data != null && result.Data.Code == 100)
{
    return (true, result.Data.Authority, "...");
}
else if (result?.Errors != null)
{
    return (false, null, result.Errors.Message);
}
```

---

## 📋 حالا باید این کارها رو انجام بدی:

### **گام 1: تنظیم MerchantId**

```json
// در appsettings.Development.json

{
  "Zarinpal": {
    "MerchantId": "YOUR-MERCHANT-ID-HERE",  // ← اینجا MerchantId واقعی بذار
    "PaymentUrl": "https://payment.zarinpal.com/pg/v4/payment/request.json",
    "VerifyUrl": "https://payment.zarinpal.com/pg/v4/payment/verify.json",
    "PaymentGatewayUrl": "https://payment.zarinpal.com/pg/StartPay/",
    "CallbackUrl": "http://localhost:5055/Payment/Verify"
  }
}
```

**چطور MerchantId بگیری:**

#### **روش A: حساب Zarinpal**
1. لاگین به https://www.zarinpal.com/
2. بخش "درگاه پرداخت"
3. کپی کردن Merchant ID

#### **روش B: تست سریع (بدون حساب)**
استفاده از Merchant ID تستی:
```
00000000-0000-0000-0000-000000000000
```

⚠️ **نکته:** این فقط برای تست محلی کار می‌کنه!

---

### **گام 2: Restart برنامه**

```sh
# Stop برنامه
# Start دوباره (F5 یا dotnet run)
```

---

## 🧪 تست

1. یک سفر انتخاب کن
2. فرم رزرو رو پر کن
3. کلیک "تایید پرداخت"
4. **اگه همه چیز درست باشه:**
   - به صفحه Zarinpal redirect میشی ✅
   - درخواست پرداخت موفق میشه ✅

---

## 📊 تفاوت API v3 و v4

| ویژگی | API v3 (قدیمی) | API v4 (جدید) |
|-------|---------------|-------------|
| **Property Names** | PascalCase (`MerchantID`) | snake_case (`merchant_id`) |
| **Response Structure** | Flat | Nested (`data`/`errors`) |
| **Domain** | `sandbox.zarinpal.com` | `payment.zarinpal.com` |
| **Path** | `/WebGate/...` | `/v4/payment/...` |
| **Status Codes** | `Status` | `data.code` یا `errors.code` |

---

## ✅ Checklist

- [x] Property names به snake_case تبدیل شدند
- [x] Response models به ساختار v4 آپدیت شدند
- [x] Service logic برای data/errors آپدیت شد
- [x] URLs به v4 تغییر کردند
- [ ] **MerchantId تنظیم بشه** ← ⚠️ **مهم!**
- [ ] برنامه restart بشه
- [ ] تست بشه

---

## 🐛 اگه هنوز خطا داری

### **خطا: "The merchant id field is required"**

**راه‌حل:**
1. چک کن `appsettings.Development.json` باز شده باشه
2. مطمئن شو `MerchantId` خالی نباشه:
```json
// ❌ اشتباه
"MerchantId": "XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX"

// ✅ درست
"MerchantId": "00000000-0000-0000-0000-000000000000"
```
3. برنامه رو restart کن (Hot Reload کافی نیست!)

---

### **خطا: "Invalid merchant_id"**

**راه‌حل:**
- از Merchant ID واقعی از Zarinpal استفاده کن
- یا برای تست: `00000000-0000-0000-0000-000000000000`

---

### **چک کردن Logs:**

در Output window دنبال این‌ها بگرد:

```
✅ "Zarinpal Payment Request JSON: {\"merchant_id\":\"...\"}"
✅ "Zarinpal HTTP Status: OK"
✅ "Zarinpal payment request successful"
```

اگه این پیام‌ها رو دیدی، یعنی همه چیز درسته! 🎉

---

**آخرین بروزرسانی:** 2024-12-16  
**API Version:** v4  
**نسخه:** 3.0 (Final)
