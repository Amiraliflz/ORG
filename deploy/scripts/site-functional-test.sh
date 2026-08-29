#!/usr/bin/env bash
# Full public-site functional smoke test (production or staging).
set -euo pipefail

BASE="${SITE_BASE:-https://mrshoofer.com}"
pass=0
fail=0

check() {
  local name="$1"
  local ok="$2"
  local detail="${3:-}"
  if [[ "$ok" == "1" ]]; then
    echo "  PASS  $name${detail:+ — $detail}"
    pass=$((pass + 1))
  else
    echo "  FAIL  $name${detail:+ — $detail}"
    fail=$((fail + 1))
  fi
}

http_code() {
  curl -s -m 25 -o /dev/null -w '%{http_code}' "$1" 2>/dev/null || echo "000"
}

body_contains() {
  local url="$1"
  local needle="$2"
  curl -sL -m 30 -A "OrgFuncTest/1.0" "$url" 2>/dev/null | grep -qF "$needle"
}

echo "=== Site functional test → $BASE ==="

home_html="$(curl -sL -m 30 -A "OrgFuncTest/1.0" "$BASE/" 2>/dev/null || true)"

code="$(http_code "$BASE/health")"
check "GET /health" "$([[ "$code" == "200" ]] && echo 1 || echo 0)" "HTTP $code"

code="$(http_code "$BASE/")"
check "GET / (homepage)" "$([[ "$code" == "200" ]] && echo 1 || echo 0)" "HTTP $code"
check "Homepage has search form" "$([[ "$home_html" == *tripForm* ]] && echo 1 || echo 0)"
check "Homepage loads IndexPage.css" "$([[ "$home_html" == *IndexPage.css* ]] && echo 1 || echo 0)"

code="$(http_code "$BASE/routes/tehran-isfahan")"
check "GET route page" "$([[ "$code" == "200" ]] && echo 1 || echo 0)" "HTTP $code"

code="$(http_code "$BASE/css/jalali-datepicker.css")"
check "Static jalali-datepicker.css" "$([[ "$code" == "200" ]] && echo 1 || echo 0)" "HTTP $code"

code="$(http_code "$BASE/js/jalali-datepicker.js")"
check "Static jalali-datepicker.js" "$([[ "$code" == "200" ]] && echo 1 || echo 0)" "HTTP $code"

code="$(http_code "$BASE/sitemap.xml")"
check "GET sitemap.xml" "$([[ "$code" == "200" ]] && echo 1 || echo 0)" "HTTP $code"
check "Sitemap has canonical host" "$(curl -sL -m 25 "$BASE/sitemap.xml" | head -20 | grep -q 'mrshoofer.com' && echo 1 || echo 0)"

code="$(http_code "$BASE/robots.txt")"
check "GET robots.txt" "$([[ "$code" == "200" ]] && echo 1 || echo 0)" "HTTP $code"

code="$(http_code "$BASE/llms.txt")"
check "GET llms.txt" "$([[ "$code" == "200" ]] && echo 1 || echo 0)" "HTTP $code"

code="$(http_code "$BASE/ops-agent/health")"
check "Ops agent via CDN" "$([[ "$code" == "200" ]] && echo 1 || echo 0)" "HTTP $code"

code="$(http_code "$BASE/Customer/MyTickets")"
check "MyTickets (redirect or 200)" "$([[ "$code" == "200" || "$code" == "302" ]] && echo 1 || echo 0)" "HTTP $code"

code="$(http_code "$BASE/TaxiTrips")"
check "TaxiTrips search page" "$([[ "$code" == "200" || "$code" == "302" || "$code" == "301" ]] && echo 1 || echo 0)" "HTTP $code"

echo ""
echo "=== Summary: $pass passed, $fail failed ==="
[[ "$fail" -eq 0 ]]
