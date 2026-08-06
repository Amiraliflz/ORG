#!/usr/bin/env bash
# Local run with Hot Reload (dotnet watch) + Razor runtime compilation.
set -euo pipefail
cd "$(dirname "$0")"

export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://127.0.0.1:5055}"

# Avoid inherited proxy settings breaking ORS / HttpClient calls.
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy ALL_PROXY all_proxy 2>/dev/null || true

exec dotnet watch run \
  --project Application.csproj \
  --no-launch-profile \
  --non-interactive
