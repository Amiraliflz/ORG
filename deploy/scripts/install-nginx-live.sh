#!/usr/bin/env bash
# Install deploy/nginx/mrshoofer-live.conf + ir-redirect-8088.conf on the VPS.
# Safe for .ir SEO: keeps 301 on pages; sitemap/robots/llms proxy without redirect.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

if [[ -f "$ROOT/.env.deploy" ]]; then
  set -a
  # shellcheck source=/dev/null
  source "$ROOT/.env.deploy"
  set +a
fi

DEPLOY_HOST="${DEPLOY_HOST:-62.60.191.21}"
DEPLOY_USER="${DEPLOY_USER:-root}"
SSH_OPTS="-o StrictHostKeyChecking=no -o PreferredAuthentications=password -o PubkeyAuthentication=no -o ConnectTimeout=30"
LIVE_SRC="$ROOT/deploy/nginx/mrshoofer-live.conf"
IR8088_SRC="$ROOT/deploy/nginx/ir-redirect-8088.conf"
STATIC_SNIPPET_SRC="$ROOT/deploy/nginx/org-static-wwwroot.conf"
DEBUG_FORMAT_SRC="$ROOT/deploy/nginx/conf.d/00-migration-debug-format.conf"

remote() {
  if [[ -n "${DEPLOY_PASS:-}" ]]; then
    SSHPASS="$DEPLOY_PASS" sshpass -e ssh -n -T $SSH_OPTS "${DEPLOY_USER}@${DEPLOY_HOST}" "$@"
  else
    ssh -n -T -o StrictHostKeyChecking=no -o ConnectTimeout=30 "${DEPLOY_USER}@${DEPLOY_HOST}" "$@"
  fi
}

remote_scp() {
  if [[ -n "${DEPLOY_PASS:-}" ]]; then
    SSHPASS="$DEPLOY_PASS" sshpass -e scp $SSH_OPTS "$1" "${DEPLOY_USER}@${DEPLOY_HOST}:$2"
  else
    scp -o StrictHostKeyChecking=no -o ConnectTimeout=30 "$1" "${DEPLOY_USER}@${DEPLOY_HOST}:$2"
  fi
}

[[ -f "$LIVE_SRC" ]] || { echo "Missing $LIVE_SRC" >&2; exit 1; }
[[ -f "$IR8088_SRC" ]] || { echo "Missing $IR8088_SRC" >&2; exit 1; }
[[ -f "$STATIC_SNIPPET_SRC" ]] || { echo "Missing $STATIC_SNIPPET_SRC" >&2; exit 1; }
[[ -f "$DEBUG_FORMAT_SRC" ]] || { echo "Missing $DEBUG_FORMAT_SRC" >&2; exit 1; }

echo "==> Uploading nginx configs to ${DEPLOY_USER}@${DEPLOY_HOST}"
remote_scp "$LIVE_SRC" "/tmp/mrshoofer-live.conf"
remote_scp "$IR8088_SRC" "/tmp/ir-redirect-8088.conf"
remote_scp "$STATIC_SNIPPET_SRC" "/tmp/org-static-wwwroot.conf"
remote_scp "$DEBUG_FORMAT_SRC" "/tmp/00-migration-debug-format.conf"

remote "set -e
  mkdir -p /etc/nginx/sites-available /etc/nginx/sites-enabled /etc/nginx/snippets /etc/nginx/conf.d
  cp /tmp/mrshoofer-live.conf /etc/nginx/sites-available/mrshoofer-live.conf
  cp /tmp/ir-redirect-8088.conf /etc/nginx/sites-available/ir-redirect-8088.conf
  cp /tmp/org-static-wwwroot.conf /etc/nginx/snippets/org-static-wwwroot.conf
  cp /tmp/00-migration-debug-format.conf /etc/nginx/conf.d/00-migration-debug-format.conf
  rm -f /tmp/mrshoofer-live.conf /tmp/ir-redirect-8088.conf /tmp/org-static-wwwroot.conf /tmp/00-migration-debug-format.conf
  touch /var/log/nginx/migration_debug.log
  chown www-data:adm /var/log/nginx/migration_debug.log

  # Retire duplicate sale vhosts (conflicts with mrshoofer-live on :80/:8080)
  for stale in /etc/nginx/sites-enabled/mrshoofer /etc/nginx/sites-enabled/mrshoofer.bak-*; do
    if [[ -e \"\$stale\" && ! -L \"\$stale\" ]]; then
      mv \"\$stale\" \"/etc/nginx/sites-available/\$(basename \"\$stale\").disabled-\$(date +%Y%m%d-%H%M%S)\"
    fi
  done
  rm -f /etc/nginx/sites-enabled/mrshoofer

  ln -sf /etc/nginx/sites-available/mrshoofer-live.conf /etc/nginx/sites-enabled/mrshoofer-live.conf
  ln -sf /etc/nginx/sites-available/ir-redirect-8088.conf /etc/nginx/sites-enabled/ir-redirect-8088.conf

  nginx -t
  systemctl reload nginx
  echo 'nginx reloaded OK'
"

echo "==> Done. Verify: ./deploy/scripts/verify-domain-migration.sh"
