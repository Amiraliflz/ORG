#!/usr/bin/env bash
# One-time VPS setup: stable localhost :5055 for inter-app API calls.
# Blue/green deploys will only flip 15055 <-> 15056 behind this proxy.
#
#   ./deploy/scripts/install-stable-localhost-proxy.sh
#   # or: DEPLOY_PASS='...' ./deploy/scripts/install-stable-localhost-proxy.sh
#
# You do NOT need this for safe deploys — plain ./deploy.sh already keeps :5055 fixed.
# Only install this if you want zero-downtime blue/green behind that port.
#
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"

# Load secrets first (same file as ./deploy.sh)
if [[ -f "$ROOT/.env.deploy" ]]; then
  set -a
  # shellcheck source=/dev/null
  source "$ROOT/.env.deploy"
  set +a
fi

DEPLOY_HOST="${DEPLOY_HOST:-62.60.191.21}"
DEPLOY_USER="${DEPLOY_USER:-root}"
DEPLOY_DIR="${DEPLOY_DIR:-/var/www/org}"
SERVICE="${SERVICE:-org.service}"
STABLE_PORT="${STABLE_PORT:-5055}"
BACKEND_A="${BACKEND_A:-15055}"
SSH_OPTS="-o StrictHostKeyChecking=no -o PreferredAuthentications=password -o PubkeyAuthentication=no -o ConnectTimeout=30"

if [[ -z "${DEPLOY_PASS:-}" ]]; then
  if [[ -t 0 ]]; then
    read -r -s -p "SSH password for ${DEPLOY_USER}@${DEPLOY_HOST}: " DEPLOY_PASS
    echo
  else
    echo "Set DEPLOY_PASS in .env.deploy or the environment." >&2
    exit 1
  fi
fi

if ! command -v sshpass >/dev/null 2>&1; then
  echo "Missing sshpass (brew install sshpass / apt install sshpass)." >&2
  exit 1
fi

remote() {
  SSHPASS="$DEPLOY_PASS" sshpass -e ssh -T $SSH_OPTS "${DEPLOY_USER}@${DEPLOY_HOST}" "$@"
}

remote_scp() {
  SSHPASS="$DEPLOY_PASS" sshpass -e scp $SSH_OPTS "$1" "${DEPLOY_USER}@${DEPLOY_HOST}:$2"
}

echo "==> Auth check ${DEPLOY_USER}@${DEPLOY_HOST}"
if ! remote "echo ok" >/dev/null; then
  echo "SSH failed — update DEPLOY_PASS in .env.deploy (password auth rejected)." >&2
  echo "Safe deploy without this install: ./deploy.sh" >&2
  exit 1
fi

echo "==> Installing stable localhost proxy on ${DEPLOY_HOST}"
remote_scp "$ROOT/deploy/nginx/org-stable-localhost.conf" /tmp/org-stable-localhost.conf

remote "set -e
  cat > /etc/nginx/conf.d/org-backend-active.conf <<EOF
upstream org_app_backend {
    server 127.0.0.1:${BACKEND_A};
}
EOF
  cp /tmp/org-stable-localhost.conf /etc/nginx/conf.d/org-stable-localhost.conf

  # Move org.service off :${STABLE_PORT} onto backend A so the proxy owns :${STABLE_PORT}
  mkdir -p /etc/systemd/system/${SERVICE}.d
  cat > /etc/systemd/system/${SERVICE}.d/port.conf <<EOF
[Service]
Environment=ASPNETCORE_URLS=http://127.0.0.1:${BACKEND_A}
EOF
  systemctl daemon-reload
  systemctl stop ${SERVICE} 2>/dev/null || true
  fuser -k ${STABLE_PORT}/tcp 2>/dev/null || true
  systemctl start ${SERVICE}
  sleep 2

  # Public nginx must keep calling stable :${STABLE_PORT} (never backends)
  if grep -Rql '127.0.0.1:15055\|127.0.0.1:15056\|127.0.0.1:5056' /etc/nginx/sites-enabled/ 2>/dev/null; then
    sed -i 's/127.0.0.1:15055/127.0.0.1:${STABLE_PORT}/g; s/127.0.0.1:15056/127.0.0.1:${STABLE_PORT}/g; s/127.0.0.1:5056/127.0.0.1:${STABLE_PORT}/g' /etc/nginx/sites-enabled/* || true
  fi

  nginx -t
  systemctl reload nginx
  echo ${BACKEND_A} > ${DEPLOY_DIR}/ACTIVE_BACKEND_PORT
  echo ${STABLE_PORT} > ${DEPLOY_DIR}/STABLE_PORT

  ss -tlnp | grep -E '${STABLE_PORT}|15055|15056' || true
  curl -sf -m 5 -o /dev/null -w 'backend :${BACKEND_A} -> %{http_code}\\n' http://127.0.0.1:${BACKEND_A}/ || true
  curl -sf -m 5 -o /dev/null -w 'stable :${STABLE_PORT} -> %{http_code}\\n' http://127.0.0.1:${STABLE_PORT}/ || true
"

echo "==> Done. Peer apps must keep calling http://127.0.0.1:${STABLE_PORT}/"
echo "    Deploy blue/green will only flip ${BACKEND_A} <-> 15056 behind that proxy."
