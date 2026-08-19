#!/usr/bin/env bash
# Verify mrshoofer.ir → mrshoofer.com migration (run after deploy + CDN/nginx).
set -euo pipefail

CANONICAL="${CANONICAL_ORIGIN:-https://mrshoofer.com}"
LEGACY="${LEGACY_ORIGIN:-https://mrshoofer.ir}"

pass=0
fail=0

check_redirect() {
  local from="$1"
  local expect_prefix="$2"
  local loc
  loc="$(curl -sI -m 25 "$from" | tr -d '\r' | awk 'tolower($1)=="location:"{print $2; exit}')"
  local code
  code="$(curl -sI -m 25 "$from" | tr -d '\r' | head -1 | awk '{print $2}')"
  if [[ "$code" == "301" || "$code" == "308" ]] && [[ "$loc" == "$expect_prefix"* ]]; then
    echo "OK  $from → $loc"
    pass=$((pass + 1))
  else
    echo "FAIL $from (HTTP $code, Location: ${loc:-none}, want prefix $expect_prefix)"
    fail=$((fail + 1))
  fi
}

check_200_canonical() {
  local url="$1"
  local code canon
  code="$(curl -sI -m 25 "$url" | tr -d '\r' | head -1 | awk '{print $2}')"
  canon="$(curl -sL -m 25 "$url" | grep -o 'rel="canonical" href="[^"]*"' | head -1 | sed 's/.*href="//;s/"//')"
  if [[ "$code" == "200" && "$canon" == "$CANONICAL/" || "$canon" == "$CANONICAL"/* ]]; then
    echo "OK  $url canonical=$canon"
    pass=$((pass + 1))
  else
    echo "FAIL $url (HTTP $code, canonical=${canon:-none})"
    fail=$((fail + 1))
  fi
}

echo "=== Redirects (legacy → $CANONICAL) ==="
check_redirect "$LEGACY/" "$CANONICAL/"
check_redirect "$LEGACY/routes/tehran-isfahan" "$CANONICAL/routes/tehran-isfahan"
check_redirect "https://www.mrshoofer.ir/" "$CANONICAL/"
check_redirect "https://www.mrshoofer.com/" "$CANONICAL/"
check_redirect "http://mrshoofer.ir/" "$CANONICAL/"
check_redirect "http://www.mrshoofer.ir/" "$CANONICAL/"
check_redirect "http://www.mrshoofer.ir/otapanel/Auth/Login" "$CANONICAL/"

check_gone() {
  local from="$1"
  local code
  code="$(curl -sI -m 25 -o /dev/null -w '%{http_code}' "$from")"
  if [[ "$code" == "410" ]]; then
    echo "OK  $from → 410"
    pass=$((pass + 1))
  elif [[ "$code" == "301" || "$code" == "308" ]]; then
    local loc
    loc="$(curl -sI -m 25 "$from" | tr -d '\r' | awk 'tolower($1)=="location:"{print $2; exit}')"
    local final
    final="$(curl -sI -m 25 -o /dev/null -w '%{http_code}' "$loc")"
    if [[ "$final" == "410" ]]; then
      echo "OK  $from → $loc → 410"
      pass=$((pass + 1))
    else
      echo "FAIL $from (redirected to $loc HTTP $final, want 410)"
      fail=$((fail + 1))
    fi
  else
    echo "FAIL $from (HTTP $code, want 410)"
    fail=$((fail + 1))
  fi
}

echo "=== Canonical site ==="
check_200_canonical "$CANONICAL/"
check_200_canonical "$CANONICAL/routes/tehran-isfahan"

echo "=== Dead URLs (410) ==="
check_gone "$CANONICAL/cgi-sys/suspendedpage.cgi"
check_gone "$CANONICAL/index.php/category/uncategorized/feed/"
check_gone "$LEGACY/index.php/2024/09/01/enhancing-customer-engagement-with-hubspot-crm/feed/"

echo "=== Sitemap ==="
if curl -sL -m 25 "$CANONICAL/sitemap.xml" | head -5 | grep -q "$CANONICAL"; then
  echo "OK  sitemap uses $CANONICAL"
  pass=$((pass + 1))
else
  echo "FAIL sitemap missing $CANONICAL URLs"
  fail=$((fail + 1))
fi

echo "=== Summary: $pass passed, $fail failed ==="
[[ "$fail" -eq 0 ]]
