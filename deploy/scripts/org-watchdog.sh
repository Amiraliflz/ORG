#!/usr/bin/env bash
# Local recovery when mrshoofer.com origin is unhealthy (nginx down, app down, or proxy broken).
# Runs from systemd timer — independent of ASP.NET and Uptime Kuma.
set -euo pipefail

STABLE_URL="${STABLE_URL:-http://127.0.0.1:5055/health}"
BACKEND_URL="${BACKEND_URL:-http://127.0.0.1:15055/health}"
PUBLIC_URL="${PUBLIC_URL:-http://127.0.0.1/health}"
PUBLIC_HOST="${PUBLIC_HOST:-mrshoofer.com}"
RESTART_SCRIPT="${RESTART_SCRIPT:-/usr/local/bin/org-ops-restart.sh}"
LOG_TAG="org-watchdog"
STATE_DIR="${STATE_DIR:-/var/lib/org-watchdog}"
COOLDOWN_SEC="${COOLDOWN_SEC:-300}"

mkdir -p "$STATE_DIR"

log() { echo "[$LOG_TAG] $*"; systemd-cat -t "$LOG_TAG" echo "$*" 2>/dev/null || true; }

http_ok() {
  local url="$1"
  local code
  code="$(curl -s -m 10 -o /dev/null -w '%{http_code}' -H "Host: ${PUBLIC_HOST}" "$url" 2>/dev/null || echo 000)"
  [[ "$code" == "200" ]]
}

stable_ok() {
  curl -sf -m 10 "$STABLE_URL" >/dev/null 2>&1
}

backend_ok() {
  curl -sf -m 10 "$BACKEND_URL" >/dev/null 2>&1
}

nginx_active() {
  systemctl is-active --quiet nginx
}

in_cooldown() {
  local stamp="$STATE_DIR/last-recover.ts"
  [[ -f "$stamp" ]] || return 1
  local last now
  last="$(cat "$stamp" 2>/dev/null || echo 0)"
  now="$(date +%s)"
  (( now - last < COOLDOWN_SEC ))
}

mark_recover() {
  date +%s > "$STATE_DIR/last-recover.ts"
}

recover_nginx() {
  if nginx_active; then
    return 0
  fi
  log "nginx not active — starting"
  if nginx -t 2>/dev/null; then
    systemctl start nginx || systemctl restart nginx
    sleep 2
  else
    log "nginx -t failed — cannot start ($(nginx -t 2>&1 | tail -1))"
    return 1
  fi
  nginx_active
}

recover_app() {
  if [[ ! -x "$RESTART_SCRIPT" ]]; then
    log "missing restart script: $RESTART_SCRIPT"
    return 1
  fi
  log "running $RESTART_SCRIPT"
  if "$RESTART_SCRIPT"; then
    sleep 3
    return 0
  fi
  return 1
}

# --- checks ---
if nginx_active && stable_ok && http_ok "$PUBLIC_URL"; then
  exit 0
fi

if in_cooldown; then
  log "still unhealthy but cooldown active — skip recover"
  exit 1
fi

log "unhealthy: nginx=$(systemctl is-active nginx 2>/dev/null || echo dead) stable=$(
  stable_ok && echo ok || echo fail) backend=$(backend_ok && echo ok || echo fail) public=$(
  http_ok "$PUBLIC_URL" && echo ok || echo fail)"

mark_recover

if ! recover_nginx; then
  log "nginx recovery failed"
  exit 1
fi

if ! stable_ok; then
  if backend_ok; then
    log "backend ok but stable proxy bad — reloading nginx"
    systemctl reload nginx
    sleep 2
  else
    recover_app || true
  fi
fi

if stable_ok && http_ok "$PUBLIC_URL"; then
  log "recovery OK"
  exit 0
fi

log "recovery attempted but site still unhealthy"
exit 1
