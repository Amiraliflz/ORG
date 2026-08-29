#!/usr/bin/env bash
# Restart MrShoofer ORG web app on the VPS (blue-green backend).
# Used by Ops mobile "راه‌اندازی مجدد وب‌اپ" — restarts ONLY the app process, not the machine.
set -euo pipefail

DEPLOY_DIR="${DEPLOY_DIR:-/var/www/org}"
SERVICE="${SERVICE:-org.service}"
BACKEND_A="${BACKEND_A:-15055}"
BACKEND_B="${BACKEND_B:-15056}"
HEALTH_PATH="${HEALTH_PATH:-/health}"

cd "$DEPLOY_DIR"

pids_on_port() {
  local port="$1"
  ss -tlnp 2>/dev/null | grep "127.0.0.1:${port}" | grep -oP 'pid=\K[0-9]+' || true
}

port_is_free() {
  local port="$1"
  ! ss -tlnp 2>/dev/null | grep -q "127.0.0.1:${port}"
}

free_backend_port() {
  local port="$1"
  local pid

  fuser -k "${port}/tcp" 2>/dev/null || true
  sleep 1

  for pid in $(pids_on_port "$port"); do
    if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
      echo "[org-ops-restart] killing pid=${pid} on :${port}"
      kill -TERM "$pid" 2>/dev/null || true
    fi
  done

  sleep 1
  for pid in $(pids_on_port "$port"); do
    if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
      echo "[org-ops-restart] force-kill pid=${pid} on :${port}"
      kill -9 "$pid" 2>/dev/null || true
    fi
  done

  sleep 1
  if ! port_is_free "$port"; then
    echo "[org-ops-restart] port :${port} still in use" >&2
    ss -tlnp | grep "${port}" >&2 || true
    return 1
  fi
}

# Orphan ./Application processes (not systemd) that block blue/green ports.
kill_stale_org_apps() {
  local keep_pid="${1:-0}"
  local pid

  for pid in $(pgrep -f "${DEPLOY_DIR}/Application" 2>/dev/null || true); do
    [[ "$pid" == "$keep_pid" ]] && continue
    echo "[org-ops-restart] stale app pid=${pid}"
    kill -TERM "$pid" 2>/dev/null || true
  done

  sleep 1

  for pid in $(pgrep -f "${DEPLOY_DIR}/Application" 2>/dev/null || true); do
    [[ "$pid" == "$keep_pid" ]] && continue
    kill -9 "$pid" 2>/dev/null || true
  done
}

stop_systemd_org() {
  if systemctl is-active --quiet "$SERVICE" 2>/dev/null; then
    echo "[org-ops-restart] stopping ${SERVICE}"
    systemctl stop "$SERVICE" || true
  fi
  systemctl reset-failed "$SERVICE" 2>/dev/null || true
}

prepare_backend_ports() {
  stop_systemd_org
  kill_stale_org_apps 0
  free_backend_port "$BACKEND_A"
  free_backend_port "$BACKEND_B"
}

sync_systemd_port() {
  local port="$1"
  mkdir -p "/etc/systemd/system/${SERVICE}.d"
  cat > "/etc/systemd/system/${SERVICE}.d/port.conf" <<EOF
[Service]
Environment=ASPNETCORE_URLS=http://127.0.0.1:${port}
EOF
  systemctl daemon-reload
  systemctl reset-failed "$SERVICE" 2>/dev/null || true
}

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

prepare_backend_ports

if [[ ! -x ./Application ]]; then
  echo "[org-ops-restart] missing executable Application in ${DEPLOY_DIR}" >&2
  exit 1
fi

mkdir -p /var/log/org

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
    echo "[org-ops-restart] process died (check port conflict / address already in use)" >&2
    tail -40 /var/log/org/ops-restart.log >&2 || true
    journalctl -u "$SERVICE" -n 20 --no-pager >&2 || true
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
sync_systemd_port "$NEW"

# Stop old backend AFTER a short delay so the Ops API can finish its HTTP response
if [[ "$ACTIVE" != "$NEW" ]]; then
  (
    sleep 4
    free_backend_port "$ACTIVE" || true
  ) >/dev/null 2>&1 &
fi

echo "[org-ops-restart] OK on :${NEW}"
