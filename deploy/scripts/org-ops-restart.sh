#!/usr/bin/env bash
# Restart MrShoofer ORG web app on the VPS (blue-green backend).
# Used by Ops mobile "راه‌اندازی مجدد وب‌اپ" — restarts ONLY the app process, not the machine.
set -euo pipefail

DEPLOY_DIR="${DEPLOY_DIR:-/var/www/org}"
BACKEND_A="${BACKEND_A:-15055}"
BACKEND_B="${BACKEND_B:-15056}"
HEALTH_PATH="${HEALTH_PATH:-/health}"

cd "$DEPLOY_DIR"
ACTIVE="$(cat ACTIVE_BACKEND_PORT 2>/dev/null || true)"
if [[ "$ACTIVE" != "$BACKEND_A" && "$ACTIVE" != "$BACKEND_B" ]]; then
  if ss -tlnp 2>/dev/null | grep -q "127.0.0.1:${BACKEND_B}"; then
    ACTIVE="$BACKEND_B"
  else
    ACTIVE="$BACKEND_A"
  fi
fi

# Prefer the free backend for zero-downtime style restart
if [[ "$ACTIVE" == "$BACKEND_A" ]]; then
  NEW="$BACKEND_B"
else
  NEW="$BACKEND_A"
fi

echo "[org-ops-restart] active=${ACTIVE} starting=${NEW}"

# Free target port
fuser -k "${NEW}/tcp" 2>/dev/null || true
sleep 1

if [[ ! -x ./Application ]]; then
  echo "[org-ops-restart] missing executable Application in ${DEPLOY_DIR}" >&2
  exit 1
fi

ASPNETCORE_ENVIRONMENT=Production \
ASPNETCORE_URLS="http://127.0.0.1:${NEW}" \
  nohup ./Application >> /var/log/org/ops-restart.log 2>&1 &
echo $! > /tmp/org-ops-restart.pid
echo "[org-ops-restart] pid=$(cat /tmp/org-ops-restart.pid)"

ok=0
for i in $(seq 1 40); do
  code=$(curl -s -m 2 -o /dev/null -w "%{http_code}" "http://127.0.0.1:${NEW}${HEALTH_PATH}" || true)
  if [[ "$code" == "200" || "$code" == "302" ]]; then
    ok=1
    break
  fi
  if ! kill -0 "$(cat /tmp/org-ops-restart.pid)" 2>/dev/null; then
    echo "[org-ops-restart] process died" >&2
    tail -40 /var/log/org/ops-restart.log >&2 || true
    exit 1
  fi
  sleep 1
done

if [[ "$ok" != "1" ]]; then
  echo "[org-ops-restart] health check failed on :${NEW}" >&2
  tail -40 /var/log/org/ops-restart.log >&2 || true
  exit 1
fi

# Point stable nginx upstream at the new backend
mkdir -p /etc/nginx/conf.d
cat > /etc/nginx/conf.d/org-backend-active.conf <<EOF
upstream org_backend {
  server 127.0.0.1:${NEW};
}
upstream org_app_backend {
  server 127.0.0.1:${NEW};
}
EOF

if nginx -t 2>/dev/null; then
  systemctl reload nginx
fi

echo "${NEW}" > ACTIVE_BACKEND_PORT

# Stop old backend
if [[ "$ACTIVE" != "$NEW" ]]; then
  fuser -k "${ACTIVE}/tcp" 2>/dev/null || true
fi

echo "[org-ops-restart] OK on :${NEW}"
