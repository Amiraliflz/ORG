#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
exec dotnet run --project tools/SeoSync --configuration Release -- "$@"
