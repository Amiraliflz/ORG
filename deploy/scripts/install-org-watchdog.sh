#!/usr/bin/env bash
# Install local watchdog timer (auto-start nginx + org on failure).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
if [[ -f "$ROOT/.env.deploy" ]]; then
  set -a
  # shellcheck source=/dev/null
  source "$ROOT/.env.deploy"
  set +a
fi

DEPLOY_HOST="${DEPLOY_HOST:-62.60.191.21}"
DEPLOY_USER="${DEPLOY_USER:-root}"
SSH_OPTS="-o StrictHostKeyChecking=no -o PreferredAuthentications=password -o PubkeyAuthentication=no -o ConnectTimeout=30"

if [[ -z "${DEPLOY_PASS:-}" ]]; then
  echo "Set DEPLOY_PASS in .env.deploy" >&2
  exit 1
fi

remote() {
  SSHPASS="$DEPLOY_PASS" sshpass -e ssh -T $SSH_OPTS "${DEPLOY_USER}@${DEPLOY_HOST}" "$@"
}

upload() {
  SSHPASS="$DEPLOY_PASS" sshpass -e scp -o StrictHostKeyChecking=no "$1" "${DEPLOY_USER}@${DEPLOY_HOST}:$2"
}

echo "==> Install org-watchdog on ${DEPLOY_HOST}"
upload "$ROOT/deploy/scripts/org-watchdog.sh" /tmp/org-watchdog.sh
upload "$ROOT/deploy/systemd/org-watchdog.service" /tmp/org-watchdog.service
upload "$ROOT/deploy/systemd/org-watchdog.timer" /tmp/org-watchdog.timer

remote "set -e
  install -m 0755 /tmp/org-watchdog.sh /usr/local/bin/org-watchdog.sh
  install -m 0644 /tmp/org-watchdog.service /etc/systemd/system/org-watchdog.service
  install -m 0644 /tmp/org-watchdog.timer /etc/systemd/system/org-watchdog.timer
  mkdir -p /var/lib/org-watchdog
  systemctl daemon-reload
  systemctl enable --now org-watchdog.timer
  systemctl start org-watchdog.service
  systemctl is-active org-watchdog.timer
  /usr/local/bin/org-watchdog.sh && echo 'watchdog: healthy' || echo 'watchdog: ran recovery'
"

echo "OK — timer: systemctl list-timers org-watchdog.timer"
