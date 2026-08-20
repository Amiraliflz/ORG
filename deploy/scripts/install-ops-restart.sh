#!/usr/bin/env bash
# Install limited sudoers rule so the app can restart org.service from Ops monitor.
# Run once on the VPS as root (deploy can invoke this).
set -euo pipefail

SERVICE="${SERVICE:-org.service}"
RUN_USER="${RUN_USER:-www-data}"

SUDOERS_FILE="/etc/sudoers.d/org-ops-restart"

cat > "$SUDOERS_FILE" <<EOF
# MrShoofer Ops — allow ${RUN_USER} to restart/check ${SERVICE} only
${RUN_USER} ALL=(root) NOPASSWD: /bin/systemctl restart ${SERVICE}, /bin/systemctl is-active ${SERVICE}
EOF

chmod 440 "$SUDOERS_FILE"
visudo -cf "$SUDOERS_FILE"

mkdir -p /var/log/org
chown "${RUN_USER}:${RUN_USER}" /var/log/org 2>/dev/null || true

echo "Installed ${SUDOERS_FILE} for ${RUN_USER} → ${SERVICE}"
echo "Log directory: /var/log/org"
