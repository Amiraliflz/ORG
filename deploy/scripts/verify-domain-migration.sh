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
  local loc code
  loc="$(curl -sI -m 25 "$from" 2>/dev/null | tr -d '\r' | awk 'tolower($1)=="location:"{print $2; exit}' || true)"
  code="$(curl -sI -m 25 "$from" 2>/dev/null | tr -d '\r' | head -1 | awk '{print $2}' || true)"
  if [[ "$code" == "301" || "$code" == "308" ]] && [[ "$loc" == "$expect_prefix"* ]]; then
    echo "OK  $from → $loc"
    pass=$((pass + 1))
  elif [[ -z "$code" ]]; then
    echo "FAIL $from (SSL/connect error — check DNS/cert for this host)"
    fail=$((fail + 1))
  else
    echo "FAIL $from (HTTP $code, Location: ${loc:-none}, want prefix $expect_prefix)"
    fail=$((fail + 1))
  fi
}

# GSC Change of address: first hop must land on .com (not https://mrshoofer.ir/)
check_single_hop_to_com() {
  local from="$1"
  local loc
  loc="$(curl -sI -m 25 "$from" 2>/dev/null | tr -d '\r' | awk 'tolower($1)=="location:"{print $2; exit}' || true)"
  if [[ "$loc" == "$CANONICAL"* ]]; then
    echo "OK  single-hop $from → $loc"
    pass=$((pass + 1))
  elif [[ "$loc" == https://mrshoofer.ir/* ]]; then
    echo "FAIL $from → $loc (two-hop chain via Arvan; fix CDN redirect — see docs/SEO_SYNC.md)"
    fail=$((fail + 1))
  else
    echo "FAIL $from (Location: ${loc:-none}, want single hop to $CANONICAL)"
    fail=$((fail + 1))
  fi
}

check_https_fetch() {
  local url="$1"
  local code loc
  code="$(curl -sI -m 25 "$url" 2>/dev/null | tr -d '\r' | head -1 | awk '{print $2}' || true)"
  if [[ "$code" == "301" || "$code" == "308" ]]; then
    loc="$(curl -sI -m 25 "$url" 2>/dev/null | tr -d '\r' | awk 'tolower($1)=="location:"{print $2; exit}' || true)"
    echo "OK  $url → $loc"
    pass=$((pass + 1))
  elif [[ -z "$code" ]]; then
    echo "FAIL $url (SSL/connect error — www.mrshoofer.ir DNS may point at origin IP; use Arvan CDN — see docs/SEO_SYNC.md)"
    fail=$((fail + 1))
  else
    echo "FAIL $url (HTTP $code, want 301 to $CANONICAL)"
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

echo "=== GSC Change of address (single-hop + HTTPS fetch) ==="
check_single_hop_to_com "http://mrshoofer.ir/"
check_single_hop_to_com "http://mrshoofer.ir/routes/tehran-isfahan"
check_https_fetch "https://www.mrshoofer.ir/"
check_https_fetch "https://mrshoofer.ir/"

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

check_sitemap_200() {
  local url="$1"
  local code
  code="$(curl -sI -m 25 "$url" | tr -d '\r' | head -1 | awk '{print $2}')"
  if [[ "$code" == "200" ]]; then
    echo "OK  $url → 200"
    pass=$((pass + 1))
  else
    echo "FAIL $url (HTTP $code, want 200 for GSC fetch)"
    fail=$((fail + 1))
  fi
}

echo "=== Sitemap fetch (GSC) ==="
check_sitemap_200 "$CANONICAL/sitemap.xml"
check_sitemap_200 "$LEGACY/sitemap.xml"

check_hsts() {
  local url="$1"
  if curl -sI -m 25 "$url" | tr -d '\r' | grep -qi '^strict-transport-security:'; then
    echo "OK  HSTS present on $url"
    pass=$((pass + 1))
  else
    echo "WARN HSTS missing on $url (enable in Arvan CDN + app after deploy)"
    fail=$((fail + 1))
  fi
}

echo "=== HTTPS signals ==="
check_hsts "$CANONICAL/"

echo "=== Summary: $pass passed, $fail failed ==="
[[ "$fail" -eq 0 ]]
