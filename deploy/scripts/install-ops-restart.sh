#!/usr/bin/env bash
# Install Ops restart helper + sudoers for blue-green ORG app.
set -euo pipefail

SCRIPT_SRC="$(cd "$(dirname "$0")" && pwd)/org-ops-restart.sh"
INSTALL_PATH="/usr/local/bin/org-ops-restart.sh"
# App currently runs as root on this VPS; also allow www-data for future hardening
RUN_USERS="${RUN_USERS:-root www-data}"

install -m 755 "$SCRIPT_SRC" "$INSTALL_PATH"
mkdir -p /var/log/org
chmod 755 /var/log/org

SUDOERS_FILE="/etc/sudoers.d/org-ops-restart"
{
  echo "# MrShoofer Ops — restart web app only (not the whole server)"
  for u in $RUN_USERS; do
    echo "${u} ALL=(root) NOPASSWD: ${INSTALL_PATH}, /bin/systemctl restart org.service, /bin/systemctl is-active org.service"
  done
} > "$SUDOERS_FILE"
chmod 440 "$SUDOERS_FILE"
visudo -cf "$SUDOERS_FILE"

echo "Installed ${INSTALL_PATH}"
echo "Installed ${SUDOERS_FILE}"
