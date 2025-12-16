# Zarinpal Payment Flow Debugging Guide

## ✅ FIXED - Issues Resolved

### 1. **API Endpoint URLs** (✅ FIXED)
**Issue**: Using incorrect v4 API endpoints  
**Solution**: Updated to correct REST API endpoints:
- **Payment Request**: `https://sandbox.zarinpal.com/pg/rest/WebGate/PaymentRequest.json`
- **Payment Verification**: `https://sandbox.zarinpal.com/pg/rest/WebGate/PaymentVerification.json`
- **Gateway URL**: `https://sandbox.zarinpal.com/pg/StartPay/{Authority}`

### 2. **Request/Response Property Names** (✅ FIXED)
**Issue**: Using snake_case (`merchant_id`, `amount`) instead of PascalCase  
**Solution**: Updated all JSON property names to match Zarinpal REST API:
- `MerchantID` (not `merchant_id`)
- `Amount` (not `amount`)
- `Description` (not `description`)
- `CallbackURL` (not `callback_url`)
- `Mobile` and `Email` as direct properties (not nested in `metadata`)

### 3. **Response Structure** (✅ FIXED)
**Issue**: Expecting nested `data` object with `code` property  
**Solution**: Updated to flat structure:
```csharp
{
  "Status": 100,      // Success code
  "Authority": "..."  // Payment authority
}
```

### 4. **Comprehensive Logging** (✅ Added)
Added detailed logging in `ZarinpalService` to track:
- Request JSON being sent
- Response JSON received
- All errors and status codes

## Current Status
The Zarinpal payment integration is now configured correctly for the **REST API**.

## How to Debug

### Step 1: Check Application Logs
Look for these log messages in order:

**In ReserveController.ConfirmInfo:**
1. `ConfirmInfo POST started for trip: {TripCode}`
2. `Temporary reserve code: {ReserveCode}`
3. `Ticket confirmed: {TicketCode}`
4. `Ticket saved to database. TicketCode: {TicketCode}, Price: {Price}`
5. `Requesting Zarinpal payment. Amount in Rials: {Amount}, Ticket: {TicketCode}`

**In ZarinpalService.RequestPaymentAsync:**
6. `Zarinpal Payment Request JSON: {...}` - Check this matches correct format
7. `Zarinpal Payment Response: {...}` - Check Status is 100
8. `Zarinpal payment request successful. Authority: {Authority}, Ticket: {TicketCode}`
9. `Redirecting to Zarinpal. URL: {PaymentUrl}`

### Step 2: Verify Request Format
The request JSON should look like this:
```json
{
  "MerchantID": "a3348b1d-3593-4aa0-922c-f539bf8f9ae3",
  "Amount": 100000,
  "Description": "خرید بلیط تهران به اصفهان",
  "CallbackURL": "https://mrshoofer.ir/Payment/Verify",
  "Mobile": "09123456789",
  "Email": null
}
```

**❌ WRONG format (old API):**
```json
{
  "merchant_id": "...",  // Wrong!
  "amount": 100000,      // Wrong!
  "metadata": {          // Wrong!
    "mobile": "..."
  }
}
```

### Step 3: Check Response Format
Success response should be:
```json
{
  "Status": 100,
  "Authority": "A00000000000000000000000000123456789"
}
```

**Error response:**
```json
{
  "Status": -2,  // Error code
  "Authority": null
}
```

### Step 4: Common Error Codes

| Status Code | Meaning | Solution |
|------------|---------|----------|
| -1 | Incomplete information | Check all required fields are sent |
| -2 | Invalid MerchantID or IP | Verify MerchantID in appsettings.json |
| -3 | Amount restriction | Amount must be >= 1000 Rials (100 Tomans) |
| -11 | Transaction not found | Authority is invalid |
| -22 | Transaction unsuccessful | Payment was not completed |
| -33 | Amount mismatch | Verification amount doesn't match request |
| 100 | ✅ Success | Payment request created successfully |
| 101 | ✅ Already verified | Payment already confirmed (duplicate request) |

### Step 5: Check Network Tab
1. Open browser DevTools (F12)
2. Go to Network tab
3. Submit the payment form
4. Look for:
   - **POST to `/AgencyArea/Reserve/ConfirmInfo`**
     - Should return HTTP 302 (Redirect)
     - Location header: `https://sandbox.zarinpal.com/pg/StartPay/{Authority}`
   - Browser should automatically redirect to Zarinpal

### Step 6: Test Payment Verification
After user pays on Zarinpal:
1. Zarinpal redirects to: `https://mrshoofer.ir/Payment/Verify?Authority={Authority}&Status=OK`
2. Check logs in `PaymentController.Verify`:
   - `Payment cancelled by user` - if Status != OK
   - `Ticket not found for authority` - if ticket lookup fails
   - `Ticket already paid` - if duplicate verification
   - `Payment verified successfully` - if all good

## Expected Flow

```
User fills form → 
POST to /AgencyArea/Reserve/Reservetrip → 
Shows ConfirmInfo view → 
User clicks payment button in modal → 
POST to /AgencyArea/Reserve/ConfirmInfo → 
Creates ticket in DB (IsPaid=false) → 
Requests Zarinpal payment (REST API) → 
Zarinpal returns Status=100 + Authority → 
Saves authority to ticket → 
Redirects to: https://sandbox.zarinpal.com/pg/StartPay/{Authority} → 
User completes payment on Zarinpal → 
Zarinpal redirects to: /Payment/Verify?Authority={Authority}&Status=OK → 
Verify payment (POST to Zarinpal verify endpoint) → 
Zarinpal returns Status=100 + RefID → 
Mark ticket as paid (IsPaid=true, PaymentRefId=RefID) → 
Redirect to ReserveConfirmed → 
Send SMS to customer
```

## Testing with Sandbox

### Sandbox Test Cards:
- **Card Number**: `6104-3388-0000-0000`
- **CVV2**: Any 3-4 digits (e.g., `123`)
- **Expiry**: Any future date (e.g., `12/25`)
- **OTP**: `1234` or `12345`

### Test Scenarios:

#### ✅ Successful Payment:
1. Complete form and click payment
2. Should redirect to Zarinpal sandbox
3. Enter test card details above
4. Click confirm
5. Should redirect back to your site
6. Ticket should be marked as paid

#### ❌ Cancelled Payment:
1. Complete form and click payment
2. On Zarinpal page, click "Cancel"
3. Should redirect to PaymentFailed page

#### 🔄 Duplicate Verification:
1. Complete a successful payment
2. Go back to verification URL manually
3. Should show "already paid" and redirect to success page

## Configuration Checklist

### ✅ appsettings.json
```json
{
  "Zarinpal": {
    "MerchantId": "a3348b1d-3593-4aa0-922c-f539bf8f9ae3",
    "IsSandbox": true,
    "PaymentUrl": "https://sandbox.zarinpal.com/pg/rest/WebGate/PaymentRequest.json",
    "VerifyUrl": "https://sandbox.zarinpal.com/pg/rest/WebGate/PaymentVerification.json",
    "PaymentGatewayUrl": "https://sandbox.zarinpal.com/pg/StartPay/",
    "CallbackUrl": "https://mrshoofer.ir/Payment/Verify"
  }
}
```

**Important Notes:**
- ✅ `MerchantId` is correct for sandbox
- ✅ URLs use `sandbox.zarinpal.com` for testing
- ⚠️ `CallbackUrl` must be accessible from internet (Zarinpal needs to redirect users back)
- 🔄 For production, change to `www.zarinpal.com` and use real MerchantID

### ✅ Database Schema
Ensure `Tickets` table has these columns:
- `PaymentAuthority` (string) - Stores Zarinpal authority
- `IsPaid` (bool) - Payment status
- `PaymentRefId` (string) - Zarinpal reference ID after verification
- `CardPan` (string) - Masked card number
- `PaidAt` (DateTime?) - Payment timestamp

## Troubleshooting

### Issue: "Redirecting to Zarinpal" log appears but browser doesn't redirect
**Cause**: JavaScript or modal preventing redirect  
**Solution**: Check browser console for errors, ensure no JavaScript is blocking the redirect

### Issue: Gets Status=-2 (Invalid MerchantID)
**Cause**: MerchantID is incorrect or IP is blocked  
**Solution**: 
- Verify MerchantID in appsettings.json
- For sandbox, use: `a3348b1d-3593-4aa0-922c-f539bf8f9ae3`
- Check if your server IP is whitelisted with Zarinpal

### Issue: Amount too low error (Status=-3)
**Cause**: Amount is less than 1000 Rials  
**Solution**: Ensure ticket price × 10 >= 1000 (minimum 100 Tomans)

### Issue: Verification fails with Status=-33
**Cause**: Amount mismatch between request and verification  
**Solution**: Ensure same amount (in Rials) is used for both request and verification

### Issue: "Ticket not found for authority"
**Cause**: Database lookup failing  
**Solution**:
- Check if ticket was saved with correct PaymentAuthority
- Verify Authority in URL matches database value

## Production Deployment Checklist

Before going to production:

1. ✅ Change Zarinpal URLs from `sandbox` to `www`:
   - `https://www.zarinpal.com/pg/rest/WebGate/PaymentRequest.json`
   - `https://www.zarinpal.com/pg/rest/WebGate/PaymentVerification.json`
   - `https://www.zarinpal.com/pg/StartPay/`

2. ✅ Update MerchantID to your real merchant ID from Zarinpal dashboard

3. ✅ Verify CallbackUrl is accessible from internet

4. ✅ Test with real card (not test cards)

5. ✅ Set appropriate logging level in production:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Application.Services.Payment": "Information"
    }
  }
}
```

## Support Resources

- **Zarinpal REST API Docs**: https://docs.zarinpal.com/paymentGateway/
- **Sandbox Dashboard**: https://sandbox.zarinpal.com/
- **Production Dashboard**: https://www.zarinpal.com/panel/
- **Support**: support@zarinpal.com
