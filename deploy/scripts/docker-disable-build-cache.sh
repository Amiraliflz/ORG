#!/bin/bash
# Configure Docker so BuildKit keeps ~0 build cache permanently,
# and install an hourly prune timer as a safety net.
set -euo pipefail

DAEMON_JSON="/etc/docker/daemon.json"
BACKUP="/etc/docker/daemon.json.bak-$(date +%Y%m%d-%H%M%S)"

mkdir -p /etc/docker
if [ -f "$DAEMON_JSON" ]; then
  cp -a "$DAEMON_JSON" "$BACKUP"
  echo "Backed up existing daemon.json -> $BACKUP"
fi

python3 - <<'PY'
import json
from pathlib import Path

path = Path("/etc/docker/daemon.json")
data = {}
if path.exists() and path.read_text().strip():
    data = json.loads(path.read_text())

# Keep almost no BuildKit cache (auto-GC).
data["builder"] = {
    "gc": {
        "enabled": True,
        "defaultKeepStorage": "0B",
        "policy": [
            {"keepStorage": "0B", "all": True}
        ],
    }
}

path.write_text(json.dumps(data, indent=2) + "\n")
print("Wrote", path)
print(path.read_text())
PY

# Hourly safety net — wipe any leftover build cache
cat > /etc/systemd/system/docker-build-cache-prune.service <<'EOF'
[Unit]
Description=Purge Docker build cache
After=docker.service
Requires=docker.service

[Service]
Type=oneshot
ExecStart=/usr/bin/docker builder prune -af
EOF

cat > /etc/systemd/system/docker-build-cache-prune.timer <<'EOF'
[Unit]
Description=Hourly Docker build-cache purge

[Timer]
OnBootSec=10min
OnUnitActiveSec=1h
Persistent=true

[Install]
WantedBy=timers.target
EOF

systemctl daemon-reload
systemctl enable --now docker-build-cache-prune.timer

echo
echo "Restarting Docker to apply daemon.json (containers will briefly restart)..."
systemctl restart docker
sleep 3
systemctl is-active docker

echo
echo "Running one immediate purge..."
docker builder prune -af || true

echo
echo "=== Status ==="
systemctl list-timers docker-build-cache-prune.timer --no-pager || true
docker system df || true
df -h / | tail -1
echo
echo "Build cache GC is ON (keep 0B) + hourly prune timer enabled."
