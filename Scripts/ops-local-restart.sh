#!/usr/bin/env bash
# Restart local ORG web app on :5055 (used by Ops mobile "راه‌اندازی مجدد")
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

# Give the API time to finish responding
sleep 2

# Stop anything on 5055
if command -v lsof >/dev/null 2>&1; then
  PIDS=$(lsof -ti :5055 2>/dev/null || true)
  if [ -n "${PIDS:-}" ]; then
    kill $PIDS 2>/dev/null || true
    sleep 1
    kill -9 $PIDS 2>/dev/null || true
  fi
fi

export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://0.0.0.0:5055}"

mkdir -p logs
nohup dotnet run --project Application.csproj --no-launch-profile \
  >> logs/ops-local-restart.log 2>&1 &

echo "Restarted ORG on ${ASPNETCORE_URLS} (pid $!)"
