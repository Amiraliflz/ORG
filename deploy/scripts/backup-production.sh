#!/usr/bin/env bash
# Snapshot live sale app + nginx before deploy/migration changes.
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
DEPLOY_DIR="${DEPLOY_DIR:-/var/www/org}"
BACKUP_ROOT="${BACKUP_ROOT:-/var/backups/org}"
STAMP="$(date +%Y%m%d-%H%M%S)"
SSH_OPTS="-o StrictHostKeyChecking=no -o PreferredAuthentications=password -o PubkeyAuthentication=no -o ConnectTimeout=30"

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

echo "==> Production backup on ${DEPLOY_USER}@${DEPLOY_HOST}"
echo "    app dir: ${DEPLOY_DIR}"
echo "    stamp:   ${STAMP}"

remote "set -e
  mkdir -p ${BACKUP_ROOT}/${STAMP}
  APP_ARCHIVE=${BACKUP_ROOT}/${STAMP}/org-app.tar.gz
  NGINX_ARCHIVE=${BACKUP_ROOT}/${STAMP}/nginx-config.tar.gz
  META=${BACKUP_ROOT}/${STAMP}/meta.txt

  if [ -d ${DEPLOY_DIR} ]; then
    tar -czf \"\$APP_ARCHIVE\" -C ${DEPLOY_DIR} .
  else
    echo 'WARN: ${DEPLOY_DIR} missing' >&2
  fi

  tar -czf \"\$NGINX_ARCHIVE\" \
    /etc/nginx/sites-enabled \
    /etc/nginx/sites-available \
    /etc/nginx/conf.d/org-backend-active.conf \
    /etc/nginx/conf.d/org-stable-localhost.conf \
    /etc/nginx/snippets/org-ops-agent.conf 2>/dev/null || true

  {
    echo \"stamp=${STAMP}\"
    echo \"host=\$(hostname)\"
    echo \"deploy_dir=${DEPLOY_DIR}\"
    systemctl is-active org.service 2>/dev/null || true
    ss -tlnp | grep -E ':5055|:15055|:15056' || true
    curl -sI -m 5 http://127.0.0.1:5055/health | head -1 || true
  } > \"\$META\"

  ls -lh \"\$APP_ARCHIVE\" \"\$NGINX_ARCHIVE\" \"\$META\"
  echo \"BACKUP_DIR=${BACKUP_ROOT}/${STAMP}\"
"

echo "==> Latest backup symlink"
remote "ln -sfn ${BACKUP_ROOT}/${STAMP} ${BACKUP_ROOT}/latest && ls -la ${BACKUP_ROOT}/latest"

LOCAL_DIR="$ROOT/.backups/production"
mkdir -p "$LOCAL_DIR"
LOCAL_META="$LOCAL_DIR/backup-${STAMP}.meta.txt"
echo "==> Saving backup metadata locally → $LOCAL_META"
remote "cat ${BACKUP_ROOT}/${STAMP}/meta.txt; ls -lh ${BACKUP_ROOT}/${STAMP}/" > "$LOCAL_META"

echo
echo "Backup complete."
echo "  VPS:  ${BACKUP_ROOT}/${STAMP}/"
echo "  Local meta: ${LOCAL_META}"
echo
echo "Restore app (emergency):"
echo "  ssh ${DEPLOY_USER}@${DEPLOY_HOST} 'systemctl stop org.service; tar -xzf ${BACKUP_ROOT}/${STAMP}/org-app.tar.gz -C ${DEPLOY_DIR}; systemctl start org.service'"
