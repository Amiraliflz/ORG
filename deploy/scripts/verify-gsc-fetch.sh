#!/usr/bin/env bash
# GSC migration fetch checks (Googlebot + Inspection Tool UAs).
# Run: ./deploy/scripts/verify-gsc-fetch.sh
#
# Passing here does NOT guarantee GSC live test passes — Google crawls from its
# own IPs through Arvan CDN. If this passes but GSC fails, fix Arvan WAF/bot rules.
set -euo pipefail

UA_BOT='Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)'
UA_INSPECT='Mozilla/5.0 (Linux; Android 6.0.1; Nexus 5X Build/MMB29P) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.7390.122 Mobile Safari/537.36 (compatible; Google-InspectionTool/1.0)'

pass=0
fail=0

check() {
  local name="$1" url="$2" expect="$3" ua="$4"
  local code loc
  code="$(curl -sI -m 25 -A "$ua" "$url" 2>/dev/null | tr -d '\r' | head -1 | awk '{print $2}' || true)"
  loc="$(curl -sI -m 25 -A "$ua" "$url" 2>/dev/null | tr -d '\r' | awk 'tolower($1)=="location:"{print $2; exit}' || true)"
  if [[ "$code" == "$expect" ]]; then
    if [[ "$expect" == "301" || "$expect" == "308" ]]; then
      if [[ "$loc" == https://mrshoofer.com* ]]; then
        echo "OK   $name → $code $loc"
        pass=$((pass + 1))
        return
      fi
      echo "FAIL $name → $code Location=$loc (want https://mrshoofer.com…)"
      fail=$((fail + 1))
      return
    fi
    echo "OK   $name → $code"
    pass=$((pass + 1))
  else
    echo "FAIL $name → HTTP ${code:-error} (want $expect)"
    fail=$((fail + 1))
  fi
}

echo "=== Googlebot ==="
check "HTTP  robots.txt"  "http://mrshoofer.ir/robots.txt"  200 "$UA_BOT"
check "HTTPS robots.txt"  "https://mrshoofer.ir/robots.txt" 200 "$UA_BOT"
check "HTTP  homepage"    "http://mrshoofer.ir/"            301 "$UA_BOT"
check "HTTPS homepage"    "https://mrshoofer.ir/"           301 "$UA_BOT"
check "HTTPS sitemap"     "https://mrshoofer.ir/sitemap.xml" 200 "$UA_BOT"

echo "=== Google Inspection Tool (mobile) ==="
check "HTTP  robots.txt"  "http://mrshoofer.ir/robots.txt"  200 "$UA_INSPECT"
check "HTTPS robots.txt"  "https://mrshoofer.ir/robots.txt" 200 "$UA_INSPECT"
check "HTTP  homepage"    "http://mrshoofer.ir/"            301 "$UA_INSPECT"
check "HTTPS homepage"    "https://mrshoofer.ir/"           301 "$UA_INSPECT"

echo
echo "=== Summary: $pass passed, $fail failed ==="
if [[ "$fail" -eq 0 ]]; then
  echo
  echo "Our network can reach .ir fine."
  echo "If GSC still says 'Robots.txt unreachable', Arvan is blocking Google crawler IPs."
  echo "Playwright/curl cannot change Arvan or GSC — fix WAF/bot fight in Arvan panel."
fi
[[ "$fail" -eq 0 ]]
