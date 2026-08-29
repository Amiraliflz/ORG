#!/usr/bin/env bash
# Install always-on Ops Agent on the VPS (survives web-app hard-down).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
AGENT_SRC="${ROOT}/deploy/ops-agent"
REMOTE_DIR="/opt/org-ops-agent"
SERVICE_NAME="org-ops-agent.service"

DEPLOY_HOST="${DEPLOY_HOST:-62.60.191.21}"
DEPLOY_USER="${DEPLOY_USER:-root}"
SSH_OPTS="-o StrictHostKeyChecking=no -o PreferredAuthentications=password -o PubkeyAuthentication=no -o ConnectTimeout=30"

remote() {
  SSHPASS="${DEPLOY_PASS:?DEPLOY_PASS required}" sshpass -e ssh -T $SSH_OPTS "${DEPLOY_USER}@${DEPLOY_HOST}" "$@"
}

upload() {
  local src="$1" dest="$2"
  SSHPASS="${DEPLOY_PASS:?DEPLOY_PASS required}" sshpass -e scp -o StrictHostKeyChecking=no "$src" "${DEPLOY_USER}@${DEPLOY_HOST}:${dest}"
}

echo "==> uploading agent"
remote "mkdir -p ${REMOTE_DIR}"
upload "${AGENT_SRC}/ops_agent.py" "${REMOTE_DIR}/ops_agent.py"
upload "${AGENT_SRC}/org-ops-agent.service" "/etc/systemd/system/${SERVICE_NAME}"
remote "chmod +x ${REMOTE_DIR}/ops_agent.py"

echo "==> nginx /ops-agent/ on mrshoofer.com"
remote 'set -e
mkdir -p /etc/nginx/snippets
cat > /etc/nginx/snippets/org-ops-agent.conf <<EOF
# Always-on Ops Agent (hard-down start/restart)
location /ops-agent/ {
    proxy_pass http://127.0.0.1:15057/;
    proxy_http_version 1.1;
    proxy_set_header Host \$host;
    proxy_set_header X-Real-IP \$remote_addr;
    proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto https;
    proxy_read_timeout 120s;
    proxy_connect_timeout 5s;
}
EOF

python3 - <<'"'"'PY'"'"'
from pathlib import Path
paths = list(Path("/etc/nginx/sites-enabled").glob("*")) + list(Path("/etc/nginx/conf.d").glob("*"))
for p in paths:
    if not p.is_file():
        continue
    text = p.read_text()
    if "server_name mrshoofer.com" not in text:
        continue
    if "org-ops-agent.conf" in text and "server_name mrshoofer.com" in text:
        # ensure include sits in the .com server block
        parts = text.split("server_name mrshoofer.com www.mrshoofer.com;")
        if len(parts) >= 2 and "org-ops-agent.conf" in parts[1].split("server {")[0]:
            print(f"ok: {p}")
            continue
    marker = "server_name mrshoofer.com www.mrshoofer.com;"
    if marker not in text:
        continue
    before, after = text.split(marker, 1)
    # insert include after first newline following marker, before location /
    insert = marker + "\n    include /etc/nginx/snippets/org-ops-agent.conf;"
    # avoid double-include
    after_clean = after.replace("\n    include /etc/nginx/snippets/org-ops-agent.conf;", "", 1)
    p.write_text(before + insert + after_clean)
    print(f"patched: {p}")
PY

nginx -t
systemctl reload nginx
# Arvan origin uses :8080 for mrshoofer.com
curl -sf -H "Host: mrshoofer.com" http://127.0.0.1:8080/ops-agent/health || \
  curl -sf -H "Host: mrshoofer.com" http://127.0.0.1/ops-agent/health
echo
'

echo "==> enable systemd"
remote "systemctl daemon-reload
systemctl enable --now ${SERVICE_NAME}
systemctl is-active ${SERVICE_NAME}
curl -s http://127.0.0.1:15057/health
echo
"

echo "OK — public: https://mrshoofer.com/ops-agent/health"
