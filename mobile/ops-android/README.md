# MrShoofer Ops — Android APK

Minimal Kotlin app (no AndroidX) for live status + remote restart. Colors match the site index (black/white Shoofer palette).

## Build

If Google Maven is blocked on your network, sync deps from Gradle cache once (after any online Android build):

```bash
python3 scripts/sync-vendor-m2.py
./gradlew assembleDebug --offline
```

Otherwise:

```bash
cd mobile/ops-android
./gradlew assembleDebug    # emulator / local: http://10.0.2.2:5055
./gradlew assembleRelease  # production: https://mrshoofer.com
```

APK output:
- Debug: `app/build/outputs/apk/debug/app-debug.apk`
- Release: `app/build/outputs/apk/release/app-release-unsigned.apk`

Install on phone:
```bash
adb install app/build/outputs/apk/debug/app-debug.apk
```

For a real device hitting your Mac's local server, change debug `API_BASE` in `app/build.gradle` to your LAN IP (e.g. `http://192.168.1.x:5055`).

## Usage

1. Open app → login with **Admin** username/password
2. Monitor auto-refreshes every 30s
3. When status is DOWN, tap **راه‌اندازی مجدد سرویس**

## API used

- `POST /Admin/Ops/ApiLogin`
- `GET /Admin/Ops/StatusJson`
- `POST /Admin/Ops/ApiRestart` body `{"confirm":"RESTART"}`
