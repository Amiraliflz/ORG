# Guest Agency Fix

## Problem
The `agency` variable was `null` when unauthenticated users (guests) tried to reserve tickets, causing errors in the reservation flow.

## Root Cause
1. The guest agency record was not being created in the database
2. The migration `SeedGuestAgency.cs` had SQL Server syntax (`GETDATE()`) instead of PostgreSQL syntax (`NOW()`)
3. PostgreSQL uses quoted identifiers for table and column names

## Solution

### 1. Fixed Migration
Updated `Migrations/SeedGuestAgency.cs` to use PostgreSQL-compatible syntax:
- Changed `GETDATE()` to `NOW()`
- Added double quotes around table and column names
- Added `ON CONFLICT DO NOTHING` to prevent duplicate inserts

### 2. Controller Changes
Updated `Areas/AgencyArea/Controllers/ReserveController.cs`:
- Added `EnsureGuestAgencyExistsAsync()` method that checks if guest agency exists and creates it if missing
- Called this method in the constructor to ensure guest agency is always available
- Fixed the query to check `IdentityUser == null` instead of `IdentityUserId == null`
- Changed using directive from `System.Data.Entity` to `Microsoft.EntityFrameworkCore`

### 3. Helper Service
Created `Services/DatabaseInitializer.cs`:
- Contains static helper method to ensure guest agency exists
- Can be reused in other parts of the application if needed

### 4. SQL Script
Created `Scripts/EnsureGuestAgency.sql`:
- Standalone SQL script that can be run directly on the database
- Uses `WHERE NOT EXISTS` to avoid duplicate entries

## How to Apply the Fix

### Option 1: Run the Migration (Recommended)
```bash
dotnet ef database update
```

### Option 2: Execute the SQL Script Directly
Connect to your PostgreSQL database and run:
```sql
INSERT INTO "Agencies" ("Name", "PhoneNumber", "Address", "AdminMobile", "DateJoined", "ORSAPI_token", "Commission", "IdentityUserId")
SELECT 
    'مستر شوفر - مهمان',
    '02100000000',
    'تهران',
    '09900000000',
    NOW(),
    'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1laWRlbnRpZmllciI6IjU1NCIsImp0aSI6Ijk1ZTI1NjYzLTkzM2EtNGY1ZS04ZTdiLTMwNGQ0Yjg3M2Q3NiIsImV4cCI6MTkyMjc3ODkwOCwiaXNzIjoibXJzaG9vZmVyLmlyIiwiYXVkIjoibXJzaG9vZmVyLmlyIn0.2r5WoGmqb5Ra_6epV5jR3Y0RlHs5bcwE0li0wo1ricE',
    0,
    NULL
WHERE NOT EXISTS (
    SELECT 1 FROM "Agencies" 
    WHERE "Name" = 'مستر شوفر - مهمان' AND "IdentityUserId" IS NULL
);
```

### Option 3: Automatic Creation
The controller now automatically creates the guest agency when it's first instantiated, so simply restarting the application should create the guest agency automatically on the first guest reservation attempt.

## Verification

To verify the guest agency exists in your database, run:
```sql
SELECT * FROM "Agencies" WHERE "IdentityUserId" IS NULL;
```

You should see a record with:
- Name: مستر شوفر - مهمان
- IdentityUserId: NULL
- ORSAPI_token: (the token from your configuration)

## Token Configuration

The guest agency uses the token configured in `appsettings.json`:
```json
"MrShoofer": {
  "SellerToken": "eyJhbGc..."
}
```

This is the same token you provided in your request.
