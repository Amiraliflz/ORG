# MrShoofer ORG — Project Structure & Developer Guide

ASP.NET Core 8.0 ticket booking system for MrShoofer (Iranian ride-sharing service).
Integrates with MrShoofer ORS API for trip search/reservation and Zarinpal for payments.

---

## Table of Contents

1. [Tech Stack](#tech-stack)
2. [Directory Structure](#directory-structure)
3. [Controllers](#controllers)
4. [Services](#services)
5. [Models & ViewModels](#models--viewmodels)
6. [Database](#database)
7. [Configuration Keys](#configuration-keys)
8. [Authentication & Authorization](#authentication--authorization)
9. [Payment Flow](#payment-flow)
10. [MrShoofer ORS Integration](#mrshoofer-ors-integration)
11. [Deployment](#deployment)
12. [Adding New Features](#adding-new-features)

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Framework | ASP.NET Core 8.0 MVC |
| Database | PostgreSQL via Npgsql + EF Core 8 |
| Auth | ASP.NET Core Identity + Cookie auth |
| Payment | Zarinpal API v4 |
| Trip API | MrShoofer ORS REST API (`mrbilit.mrshoofer.ir`) |
| SMS | Kavenegar + SMSIR |
| PDF | iText7 |
| Container | Docker (multi-stage build) |
| Runtime Port | 5000 (HTTP) |

---

## Directory Structure

```
/
├── Areas/
│   ├── Admin/                      # Admin portal
│   │   ├── Controllers/
│   │   │   ├── AgencyManagement.cs  # Create/manage agencies
│   │   │   ├── MessagesController.cs
│   │   │   └── PaymentsController.cs
│   │   └── Views/
│   │
│   └── AgencyArea/                 # Main booking platform
│       ├── Controllers/
│       │   ├── AgencyController.cs         # Agency dashboard & balance
│       │   ├── AuthController.cs           # Login / OTP
│       │   ├── ContactUsController.cs      # Contact form (rate-limited)
│       │   ├── CustomerServiceController.cs
│       │   ├── HomeController.cs
│       │   ├── ReserveController.cs        # Trip reservation flow
│       │   ├── TaxiTripsController.cs      # Trip search
│       │   └── TicketInfoController.cs
│       └── Views/
│
├── Controllers/
│   └── PaymentController.cs        # Zarinpal payment entry point & verify
│
├── Data/
│   └── AppDbContext.cs             # EF Core DbContext (extends IdentityDbContext)
│
├── Migrations/                     # EF Core migration files
│
├── Models/
│   ├── Agency.cs
│   ├── Ticket.cs
│   ├── AdminUser.cs
│   ├── AgencyBalanceCharge.cs
│   ├── ChargePaymentRequest.cs
│   ├── ContactUsMessage.cs
│   └── Payment/
│       ├── ZarinpalPaymentRequest.cs
│       ├── ZarinpalPaymentResponse.cs
│       ├── ZarinpalVerifyRequest.cs
│       └── ZarinpalVerifyResponse.cs
│
├── Services/
│   ├── Auth/
│   │   ├── IOtpLogin.cs
│   │   └── KavehNeagerOtp.cs       # Kavenegar SMS OTP
│   ├── MrShooferORS/
│   │   ├── MrShooferAPIClient.cs   # ORS HTTP client (IHttpClientFactory)
│   │   ├── MockMrShooferAPIClient.cs
│   │   ├── DirectionsRepository.cs # Singleton city ID cache
│   │   ├── DirectionsTravelTimeCalculator.cs
│   │   ├── SearchedTrip.cs
│   │   ├── TicketTempReserveRequestModel.cs
│   │   ├── ConfirmReserveRequestModel.cs
│   │   └── TicketConfirmationResponse.cs
│   └── Payment/
│       ├── IPaymentService.cs
│       └── ZarinpalService.cs
│
├── ViewModels/
│   ├── Reserve/
│   │   ├── ReserveInfoViewModel.cs
│   │   └── ConfirmInfoViewModel.cs
│   ├── TaxiTrips/
│   │   └── SearchedTripViewModel.cs
│   ├── Auth/
│   │   └── LoginViewModel.cs
│   └── Admin/
│       ├── CreatingAgencyViewModel.cs
│       └── ChargeAgencyBalanceViewModel.cs
│
├── Views/
├── wwwroot/
├── deploy/systemd/application.service
├── Dockerfile
├── docker-compose.yml
├── Program.cs
├── appsettings.json
└── appsettings.Development.json
```

---

## Controllers

### Root Area

#### `PaymentController` — `/Controllers/PaymentController.cs`

The payment relay controller. This server's IP is whitelisted with Zarinpal.

| Route | Method | Description |
|-------|--------|-------------|
| `/Payment/Start` | GET | Entry from main app — validates HMAC, calls Zarinpal, returns HTML redirect page |
| `/Payment/RequestPayment` | POST | Alternative direct payment initiation by ticket ID |
| `/Payment/Verify` | GET | Zarinpal callback — verifies payment, creates ORS reservation |
| `/link` | GET | Fast callback shortcut (forwards to Verify) |
| `/Payment/PaymentFailed` | GET | Payment failure view |

**Key logic in `Start`:**
- Validates HMAC-SHA256 signature (`ticketId:timestamp` signed with `PaymentServer:SharedKey`)
- 10-minute timestamp window to prevent replay attacks
- Calls `ZarinpalService.RequestPaymentAsync()` and returns animated HTML redirect page

**Key logic in `Verify`:**
- Calls `ZarinpalService.VerifyPaymentAsync()`
- After verification: creates ORS reservation (temporary → confirm)
- Updates `Ticket` with `IsPaid`, `PaymentRefId`, `CardPan`, `PaidAt`, `TicketCode`
- If `WebappToken` received from ORS: POSTs to webapp endpoint then redirects user

---

### Admin Area (`/Admin/...`)

#### `AgencyManagement`
- Creates `IdentityUser` + `Agency` record
- Calls `MrShooferAPIClient.RegisterOTA()` to register agency with ORS
- Stores returned OTA API token in `Agency.ORSAPI_token`

#### `PaymentsController` (Admin)
- Views all payment/charge history
- JSON endpoint for DataTables: `/Admin/Payments/ChargeRequestsJson`

---

### Agency Area (default routes)

#### `TaxiTripsController` — `/TaxiTrips/Index`
- Accepts: `originstring`, `destinationstring`, `searchdate`
- Normalizes Persian city names (removes diacritics, handles terminal suffixes)
- Maps city name → ID via `DirectionsRepository` (singleton cache) or live API call
- Calls `MrShooferAPIClient.SearchTrips()` and returns trip list

#### `ReserveController`

| Route | Method | Description |
|-------|--------|-------------|
| `/Reserve/Reservetrip?tripcode=` | GET | Show reservation form |
| `/Reserve/Reservetrip` | POST | Validate passenger data |
| `/Reserve/ConfirmInfo` | POST | Save ticket to DB, redirect to payment server |
| `/Reserve/ReserveConfirmed?ticketcode=` | GET | Confirmation page post-payment |

**`ConfirmInfo` flow:**
1. Saves preliminary `Ticket` (status: PENDING)
2. Generates HMAC signature: `HMAC-SHA256("{ticketId}:{timestamp}", SharedKey)`
3. Redirects to `{PaymentServer:BaseUrl}/Payment/Start?ticketId=X&t=TIMESTAMP&sig=BASE64`

#### `AuthController`
- `/Auth/Login` — username/password via ASP.NET Identity
- `/Auth/Loginotp` — sends OTP via Kavenegar, validates on next step
- Session cookie expires in 75 days (sliding)

#### `AgencyController`
- Agency dashboard
- Balance display (calls `MrShooferAPIClient.GetAccountBalance()`)
- Logo upload/management

---

## Services

### `ZarinpalService` — `Services/Payment/ZarinpalService.cs`

Implements `IPaymentService`. Registered via `AddHttpClient<IPaymentService, ZarinpalService>`.

```csharp
Task<(bool Success, string Authority, string Message)>
    RequestPaymentAsync(int amountInRials, string description, string mobile, string? email)

Task<(bool Success, long RefId, string CardPan, string Message)>
    VerifyPaymentAsync(string authority, int amountInRials)

string GetPaymentGatewayUrl(string authority)
```

**Important:**
- Amount must be in **Rials** (multiply Toman × 10 before passing)
- `mobile` and `email` go inside `metadata` object in request JSON (not top-level)
- Reads all URLs from config; falls back to production/sandbox defaults based on `IsSandbox`

---

### `MrShooferAPIClient` — `Services/MrShooferORS/MrShooferAPIClient.cs`

Registered via `AddHttpClient<MrShooferAPIClient>` with base URL from `MrShoofer:ApiBaseUrl`.

```csharp
void SetSellerApiKey(string token)                // Set Bearer token (call before each request)
Task<IList<SearchedTrip>> SearchTrips(...)        // Search available trips
Task<SearchedTrip> GetTripInfo(string tripcode)   // Get single trip details
Task<string> ReserveTicketTemporarirly(...)       // Step 1: Temp reservation → returns reserveCode
Task<TicketConfirmationResponse> ConfirmReserve(...)  // Step 2: Final reservation → returns ticketCode
Task<string> GetAccountBalance()                  // Returns balance in Tomans
Task RegisterOTA(RegisterOTADTO)                  // Register new OTA seller
Task<Dictionary<string, int>> GetCityNameIdMapAsync()  // City name → ID map
Task<List<AvaiableDirection>> GetAvaiableOTADirectionsAsync()
Task ChargeOTABalanceAsync(int amount)
static Task<string> GetSellerApiKey_LoginAsync(username, password)  // Get JWT token
```

**Token selection priority (in `Verify` and `ConfirmInfo`):**
1. Guest agency (`IdentityUser == null` AND `Name.Contains("مهمان")`)
2. Ticket's own agency token
3. `MrShoofer:SellerToken` from config

---

### `KavehNeagerOtp` — `Services/Auth/KavehNeagerOtp.cs`

Implements `IOtpLogin`. Generates 5-digit random OTP and sends via Kavenegar.

### `CustomerServiceSmsSender` — `Services/CustomerServiceSmsSender.cs`

Sends SMS notifications (payment confirmation) via SMSIR.

### `DirectionsRepository` — `Services/MrShooferORS/DirectionsRepository.cs`

Singleton. Caches city name → ID mapping loaded from the ORS API.

---

## Models & ViewModels

### Domain Models

#### `Ticket` — `/Models/Ticket.cs`

```csharp
int Id
string Tripcode          // MrShoofer trip plan code
string TicketCode        // Final MrShoofer ticket code (PENDING-* before payment)
string Firstname, Lastname, Gender
string NaCode            // National ID
DateTime DOB
string PhoneNumber, Email
int TicketOriginalPrice, TicketFinalPrice  // In Tomans
string TripOrigin, TripDestination
string ServiceName, CarName
DateTime RegisteredAt
bool IsCancelled, IsPaid
string? PaymentAuthority // Zarinpal authority token
string? PaymentRefId     // Zarinpal reference ID after verification
string? CardPan          // Masked card number
DateTime? PaidAt
string? WebappToken      // From MrShoofer — used for webapp redirect
Agency? Agency           // Navigation property
```

#### `Agency` — `/Models/Agency.cs`

```csharp
int Id
string Name, PhoneNumber, Address, AdminMobile
DateTime DateJoined
string? ORSAPI_token     // JWT for MrShoofer ORS API
decimal Commission
string? LogoPath
bool IsDefaultSeller     // Guest/default agency flag
IdentityUser? IdentityUser  // null for guest agency
ICollection<Ticket> SoldTickets
ICollection<AgencyBalanceCharge> BalanceCharges
```

### Payment Request Model

```csharp
// ZarinpalPaymentRequest — JSON sent to Zarinpal
{
  "merchant_id": "...",
  "amount": 100000,           // Rials
  "description": "...",
  "callback_url": "https://pay.mrshoofer.ir/Payment/Verify",
  "metadata": {               // mobile/email MUST be inside metadata
    "mobile": "09...",
    "email": "..."            // optional
  }
}
```

### Key ViewModels

| ViewModel | Used In | Key Fields |
|-----------|---------|-----------|
| `ReserveInfoViewModel` | `ReserveController.Reservetrip` | TripCode, Firstname, Lastname, Gender, NaCode, PhoneNumber, WebappToken |
| `ConfirmInfoViewModel` | `ReserveController.ConfirmInfo` | TripCode, Firstname, Lastname, NaCode, Gender, WebappToken |
| `SearchedTripViewModel` | `TaxiTripsController.Index` | tripcode, origin, destination, startingDateTime, originalPrice, afterdiscount, carModelName, Image |

---

## Database

**Context:** `AppDbContext : IdentityDbContext<IdentityUser>`

**Provider:** PostgreSQL (Npgsql)

**Connection string key:** `development` or `production` (selected by environment in `Program.cs`)

**DbSets:**

```csharp
DbSet<Agency>
DbSet<Ticket>
DbSet<AdminUser>
DbSet<AgencyBalanceCharge>
DbSet<ChargePaymentRequest>
DbSet<ContactUsMessage>
// + ASP.NET Identity tables
```

**Run migrations:**
```bash
dotnet ef database update --project Application.csproj
```

**Create new migration:**
```bash
dotnet ef migrations add MigrationName --project Application.csproj
```

**Applied migrations (in order):**
1. `20240409194714_initmig` — Initial schema
2. `20251202094000_new`
3. `20251206072823_DateTime1`
4. `20251206072853_DateTime12`
5. `20251215110115_AddPaymentFieldsToTicket` — CardPan, IsPaid, PaidAt, PaymentAuthority, PaymentRefId
6. `20251219091731_AddDeFaultSeller` — IsDefaultSeller on Agency
7. `20251221112124_AddLogoPathToAgency` — LogoPath
8. `20251225082336_WEBAPP_TOKEN` — WebappToken on Ticket

---

## Configuration Keys

### `appsettings.json` (production base)

```json
{
  "ConnectionStrings": {
    "development": "Host=...;Database=ORG;Username=root;Password=...",
    "production":  "Host=...;Database=ORG;Username=root;Password=..."
  },
  "PaymentServer": {
    "BaseUrl":    "https://pay.mrshoofer.ir",
    "SharedKey":  "mrshooferpay-2026-secret-key"    // HMAC key for payment relay security
  },
  "MrShoofer": {
    "ApiBaseUrl":   "https://mrbilit.mrshoofer.ir",
    "SellerToken":  "eyJhbGc..."                    // JWT fallback token
  },
  "Zarinpal": {
    "MerchantId":      "a3348b1d-...",
    "IsSandbox":       false,
    "PaymentUrl":      "https://payment.zarinpal.com/pg/v4/payment/request.json",
    "VerifyUrl":       "https://payment.zarinpal.com/pg/v4/payment/verify.json",
    "PaymentGatewayUrl": "https://payment.zarinpal.com/pg/StartPay/",
    "CallbackUrl":     "https://pay.mrshoofer.ir/Payment/Verify",
    "Description":     "خرید بلیط سواری مِسترشوفر"
  },
  "Webapp": {
    "BaseUrl": "https://webapp.mrshoofer.ir"        // Redirect after reservation
  },
  "kavehnegar_key": "...",   // OTP SMS
  "smsirapikey":    "...",   // Confirmation SMS
  "serivce_url":    "https://mrshoofer.ir"
}
```

### `appsettings.Development.json` overrides

- `Zarinpal:IsSandbox = true` — uses sandbox endpoints
- `Zarinpal:CallbackUrl = http://localhost:5055/Payment/Verify`
- `MrShoofer:ApiBaseUrl` — can point to local mock

---

## Authentication & Authorization

### Identity Setup

```csharp
// Password rules (Program.cs)
options.Password.RequiredLength = 6;
// All other requirements: false (no digit, uppercase, special char needed)
```

### Cookie

```csharp
options.Cookie.HttpOnly = true;
options.ExpireTimeSpan = TimeSpan.FromDays(75);
options.SlidingExpiration = true;
options.LoginPath = "/Auth/Login";
options.AccessDeniedPath = "/Auth/AccessDenied";
```

### Policies

```csharp
"Agency" — requires Claim("Role", "Agency")
"Admin"  — requires Claim("Role", "Admin")
```

Apply with `[Authorize(Policy = "Agency")]` on controllers/actions.

### Guest access

Trip search, home page, and the entire payment flow are **anonymous** — no login required.

---

## Payment Flow

```
User submits reservation form
        │
        ▼
ReserveController.ConfirmInfo (POST)
  ├─ Save Ticket to DB (TicketCode = "PENDING-{timestamp}-{id}")
  ├─ Generate HMAC: SHA256("{ticketId}:{unixTimestamp}", SharedKey)
  └─ Redirect ──────────────────────────────────────────────────────┐
                                                                     │
                                          pay.mrshoofer.ir           │
                                          ┌──────────────────────────▼───┐
                                          │ PaymentController.Start (GET) │
                                          │  ✓ Validate HMAC & timestamp  │
                                          │  ✓ Call ZarinpalService        │
                                          │  ✓ Save authority to Ticket    │
                                          │  ✓ Return HTML redirect page  │
                                          └──────────────┬───────────────┘
                                                         │
                                          ┌──────────────▼──────────────┐
                                          │   Zarinpal Payment Gateway   │
                                          │   User enters card details   │
                                          └──────────────┬──────────────┘
                                                         │
                                          ┌──────────────▼───────────────┐
                                          │ PaymentController.Verify (GET)│
                                          │  ✓ Status == "OK"?            │
                                          │  ✓ VerifyPaymentAsync()       │
                                          │  ✓ Update Ticket (IsPaid etc) │
                                          │  ✓ ORS: TempReserve           │
                                          │  ✓ ORS: ConfirmReserve        │
                                          │  ✓ Update TicketCode          │
                                          └──────────────┬───────────────┘
                                                         │
                                          Has WebappToken?
                                          ├─ YES → POST to webapp, Redirect to webapp
                                          └─ NO  → ReserveConfirmed view
```

### Amount conversion
Always multiply Toman × 10 to get Rials before passing to `RequestPaymentAsync` or `VerifyPaymentAsync`.

### ORS reservation failure handling
If MrShoofer reservation fails (e.g., insufficient balance), the ticket is still marked paid:
`TicketCode = "PAID-NO-RESERVE-{timestamp}-{ticketId}"`

---

## MrShoofer ORS Integration

### Trip search endpoint

```
GET /Trips/GetPlanedTripsbyCityID/{startDate:yyyy-MM-dd}/{endDate:yyyy-MM-dd}/{originCityId}/{destCityId}
                                                                              [/{originTerminalId}]
                                                                              [/{destTerminalId}]
```

### Reservation sequence

```
POST /Tickets/reserverTemporarily
  body: { tripCode, isPrivate, seatnumber? }
  → returns: { ticketCode: "temp-xxx" }

POST /Tickets/confirmReserve
  body: { reservationCode, passengerFirstName, passengerLastName,
          passengerNationalCode, passengerNumberPhone }
  → returns: { ticketCode: "final-xxx", webappToken: "jwt..." }
```

### Authentication for ORS

Each request requires a Bearer token set via `SetSellerApiKey(token)`.
Token selection priority: Guest agency → Ticket's agency → Config fallback.

To get a new token: `MrShooferAPIClient.GetSellerApiKey_LoginAsync(username, password)`

---

## Deployment

### Local development

```bash
dotnet run --project Application.csproj
# App at http://localhost:5055 (uses appsettings.Development.json)
```

### Docker (local production test)

```bash
docker compose up --build
# App at http://localhost:5050
```

### VPS deployment (`pay.mrshoofer.ir` — `62.60.191.21`)

The VPS runs the same Docker image. It is the **payment server** — its IP is whitelisted with Zarinpal.

```bash
# On dev machine: build image for deployment
docker build -t mrshoofer-app .
docker save mrshoofer-app | gzip > mrshoofer-app.tar.gz

# Upload to VPS
scp mrshoofer-app.tar.gz root@62.60.191.21:/tmp/

# On VPS: load and run
docker load < /tmp/mrshoofer-app.tar.gz
docker stop mrshoofer && docker rm mrshoofer
docker run -d --name mrshoofer --restart always -p 5000:5000 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ASPNETCORE_URLS=http://0.0.0.0:5000 \
  mrshoofer-app
```

Nginx on the VPS proxies port 80 → 5000 for `pay.mrshoofer.ir`.

### Liara deployment (`mrshoofer-orgselling`)

```bash
liara deploy --app mrshoofer-orgselling --port 5000
```

---

## Adding New Features

### New entity (DB table)

1. Add model class in `Models/`
2. Add `DbSet<T>` to `AppDbContext`
3. Run `dotnet ef migrations add YourMigrationName --project Application.csproj`
4. Run `dotnet ef database update --project Application.csproj`

### New controller in AgencyArea

1. Create `Areas/AgencyArea/Controllers/YourController.cs`
2. Add `[Area("AgencyArea")]` attribute
3. Add corresponding views in `Areas/AgencyArea/Views/Your/`
4. Add `[Authorize(Policy = "Agency")]` if login required

### New API endpoint to MrShoofer ORS

1. Add method to `MrShooferAPIClient`
2. Use relative paths (`/Endpoint/Path`) — base URL is set from config
3. Always call `SetSellerApiKey(token)` before requests that need auth

### New payment provider

1. Implement `IPaymentService` interface
2. Register in `Program.cs`: `builder.Services.AddHttpClient<IPaymentService, YourService>(...)`
3. The existing payment flow in `PaymentController` works without changes

### Deploy changes to VPS

```bash
docker build -t mrshoofer-app .
docker save mrshoofer-app | gzip > /tmp/mrshoofer-app.tar.gz
sshpass -p 'PASSWORD' scp /tmp/mrshoofer-app.tar.gz root@62.60.191.21:/tmp/
sshpass -p 'PASSWORD' ssh root@62.60.191.21 '
  docker load < /tmp/mrshoofer-app.tar.gz
  docker stop mrshoofer && docker rm mrshoofer
  docker run -d --name mrshoofer --restart always -p 5000:5000 \
    -e ASPNETCORE_ENVIRONMENT=Production \
    -e ASPNETCORE_URLS=http://0.0.0.0:5000 mrshoofer-app
'
```
