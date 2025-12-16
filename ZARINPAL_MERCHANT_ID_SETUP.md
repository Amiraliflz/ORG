# راهنمای تنظیم Zarinpal Merchant ID

## 🚨 مشکل فعلی

خطای زیر دریافت می‌شود:
```
'<' is an invalid start of a value
```

**دلیل:** Zarinpal به جای JSON، صفحه HTML (خطا) برمی‌گرداند.  
**علت:** MerchantId نامعتبر است.

---

## ✅ راه‌حل

### **گام 1: دریافت Merchant ID از Zarinpal**

#### **برای Sandbox (تست):**

1. برو به: https://www.zarinpal.com/
2. ثبت‌نام کن یا لاگین کن
3. برو به بخش **درگاه پرداخت** → **اطلاعات پذیرنده**
4. **Merchant ID** خودت رو کپی کن (فرمت: `XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX`)

#### **یا استفاده از Merchant ID تستی:**

برای sandbox زرین‌پال می‌تونی از **هر Merchant ID معتبر** استفاده کنی.  
فرمت باید UUID باشه: `36 کاراکتر با خط‌فاصله`

مثال:
```
12345678-1234-1234-1234-123456789012
```

### **گام 2: آپدیت کردن `appsettings.Development.json`**

```json
{
  "Zarinpal": {
    "MerchantId": "12345678-1234-1234-1234-123456789012",  // ← MerchantID خودت رو اینجا بذار
    "IsSandbox": true,
    "PaymentUrl": "https://sandbox.zarinpal.com/pg/rest/WebGate/PaymentRequest.json",
    "VerifyUrl": "https://sandbox.zarinpal.com/pg/rest/WebGate/PaymentVerification.json",
    "PaymentGatewayUrl": "https://sandbox.zarinpal.com/pg/StartPay/",
    "CallbackUrl": "https://YOUR-NGROK-URL.ngrok.io/Payment/Verify",  // ← ngrok URL
    "Description": "خرید بلیط مستر شوفر - توسعه محلی"
  }
}
```

### **گام 3: تنظیم Callback URL با ngrok**

```sh
# 1. اجرای ngrok
ngrok http 5055

# 2. کپی کردن HTTPS URL
# مثال: https://abc123.ngrok.io

# 3. آپدیت CallbackUrl در appsettings.Development.json
"CallbackUrl": "https://abc123.ngrok.io/Payment/Verify"
```

### **گام 4: Restart برنامه**

```sh
# توقف برنامه (Ctrl+C در terminal یا Stop در Visual Studio)
# اجرای مجدد
dotnet run
```

---

## 📋 تفاوت Sandbox و Production

| ویژگی | Sandbox (تست) | Production (واقعی) |
|-------|--------------|-------------------|
| **URL Payment** | `sandbox.zarinpal.com` | `api.zarinpal.com` |
| **پول واقعی** | ❌ خیر | ✅ بله |
| **نیاز به تایید** | ❌ خیر | ✅ بله |
| **Merchant ID** | هر UUID معتبر | فقط Merchant واقعی |
| **تست کارت** | `5022-2910-xxxx-xxxx` | کارت واقعی |

---

## 🧪 تست Sandbox

### **اطلاعات تست:**

```
شماره کارت: 5022-2910-xxxx-xxxx (هر عددی برای x)
CVV2: هر عددی (مثلاً 123)
تاریخ انقضا: هر تاریخ آینده (مثلاً 12/30)
رمز دوم: 123456
```

### **سناریوهای تست:**

#### ✅ **پرداخت موفق:**
1. وارد کردن اطلاعات کارت بالا
2. کلیک روی "پرداخت"
3. باید به صفحه موفقیت redirect بشی

#### ❌ **پرداخت ناموفق:**
1. وارد کردن شماره کارت نامعتبر
2. یا کلیک روی "انصراف"
3. باید صفحه `PaymentFailed` رو ببینی

---

## 🔧 تنظیمات Production

وقتی آماده deploy شدی:

### **`appsettings.json` (Production):**

```json
{
  "Zarinpal": {
    "MerchantId": "YOUR-REAL-MERCHANT-ID-FROM-ZARINPAL",
    "IsSandbox": false,
    "PaymentUrl": "https://api.zarinpal.com/pg/v4/payment/request.json",
    "VerifyUrl": "https://api.zarinpal.com/pg/v4/payment/verify.json",
    "PaymentGatewayUrl": "https://www.zarinpal.com/pg/StartPay/",
    "CallbackUrl": "https://mrshoofer.ir/Payment/Verify",
    "Description": "خرید بلیط مستر شوفر"
  }
}
```

**⚠️ نکات مهم:**
- `IsSandbox` رو `false` کن
- URLs رو به production تغییر بده
- Merchant ID واقعی از زرین‌پال دریافت کن
- CallbackUrl باید دامنه واقعی باشه (نه ngrok!)

---

## 🐛 عیب‌یابی

### **خطا: "HTML instead of JSON"**

**علت:** MerchantId نامعتبر یا URL اشتباه

**راه‌حل:**
1. Merchant ID رو چک کن (فرمت UUID)
2. `IsSandbox: true` رو چک کن
3. URLs sandbox رو چک کن

### **خطا: "درگاه پرداخت پاسخ نمی‌دهد"**

**علت:** فیلترشکن یا مشکل اینترنت

**راه‌حل:**
1. فیلترشکن رو خاموش کن
2. اتصال اینترنت رو چک کن
3. از DNS گوگل استفاده کن: `8.8.8.8`

### **خطا: "Callback URL unreachable"**

**علت:** ngrok نمی‌تونه به localhost وصل بشه

**راه‌حل:**
1. ngrok رو مطمئن شو که اجرا شده
2. URL ngrok رو درست کپی کن (با `https://`)
3. Port رو چک کن (باید با برنامه مطابقت داشته باشه)

---

## 📊 جریان کامل پرداخت

```
کاربر کلیک "تایید پرداخت"
    ↓
درخواست به Zarinpal با MerchantId
    ↓
    ├─ MerchantId نامعتبر → HTML Error ❌
    └─ MerchantId معتبر → JSON با Authority ✅
         ↓
    Redirect به Zarinpal Gateway
         ↓
    کاربر وارد اطلاعات کارت می‌کنه
         ↓
         ├─ پرداخت موفق → Redirect به CallbackUrl?Status=OK&Authority=xxx
         └─ پرداخت ناموفق → Redirect به CallbackUrl?Status=NOK&Authority=xxx
              ↓
         PaymentController.Verify
              ↓
              ├─ Status=OK → Verify با Zarinpal → Create MrShoofer Reservation
              └─ Status=NOK → Show PaymentFailed
```

---

## 📞 لینک‌های مفید

- **مستندات زرین‌پال:** https://docs.zarinpal.com/
- **پنل پذیرنده:** https://www.zarinpal.com/panel/
- **تست کارت Sandbox:** https://docs.zarinpal.com/paymentGateway/sandbox.html
- **پشتیبانی:** support@zarinpal.com

---

## ✅ Checklist قبل از تست

- [ ] Merchant ID معتبر در `appsettings.Development.json`
- [ ] `IsSandbox: true` تنظیم شده
- [ ] URLs sandbox درست هستند
- [ ] ngrok اجرا شده و URL در `CallbackUrl` قرار گرفته
- [ ] برنامه restart شده
- [ ] فیلترشکن خاموش است
- [ ] اتصال اینترنت برقرار است

---

**آخرین بروزرسانی:** 2024-12-16  
**نسخه:** 1.0
