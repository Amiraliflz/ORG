#!/bin/bash
# Purge Docker BuildKit / buildx cache on this host.
# Safe for running containers — only removes build cache layers.
set -euo pipefail

echo "=== BEFORE ==="
df -h / | tail -1
docker system df 2>/dev/null || true

echo
echo "=== Purging build cache ==="
docker builder prune -af
# also clear classic builder cache if present
docker buildx prune -af 2>/dev/null || true

echo
echo "=== AFTER ==="
docker system df 2>/dev/null || true
df -h / | tail -1
echo "Done."
