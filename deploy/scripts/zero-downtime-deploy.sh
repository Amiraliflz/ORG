#!/usr/bin/env bash
# Zero / near-zero downtime deploy for MrShoofer ORG (sale app).
#
# Only ships artifacts needed for files changed in the last commit
# (override with FROM_REF / TO_REF).
#
# Usage (from repo root):
#   export DEPLOY_HOST=62.60.191.21
#   export DEPLOY_PASS='your-root-password'   # or use SSH keys and leave unset
#   ./deploy/scripts/zero-downtime-deploy.sh
#
# Options:
#   FROM_REF=HEAD~1 TO_REF=HEAD   # default: last commit
#   DRY_RUN=1                     # print plan only
#   FORCE_FULL=1                  # always rebuild + deploy
#   SKIP_BLUE_GREEN=1             # copy/restart (short blip) — systemd mode only
#   USE_DOCKER=1                  # build/push Docker image instead of bare DLL
#   ENSURE_AUTOSTART=1            # default ON — survive VPS reboot
#   ORG_IMAGE=mrshoofer-org:latest
#
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

DEPLOY_HOST="${DEPLOY_HOST:-62.60.191.21}"
DEPLOY_USER="${DEPLOY_USER:-root}"
DEPLOY_DIR="${DEPLOY_DIR:-/var/www/org}"
SERVICE="${SERVICE:-org.service}"
PORT_A="${PORT_A:-5055}"
PORT_B="${PORT_B:-5056}"
FROM_REF="${FROM_REF:-HEAD~1}"
TO_REF="${TO_REF:-HEAD}"
DRY_RUN="${DRY_RUN:-0}"
FORCE_FULL="${FORCE_FULL:-0}"
SKIP_BLUE_GREEN="${SKIP_BLUE_GREEN:-0}"
USE_DOCKER="${USE_DOCKER:-0}"
ENSURE_AUTOSTART="${ENSURE_AUTOSTART:-1}"
ORG_IMAGE="${ORG_IMAGE:-mrshoofer-org:latest}"
DOCKER_NAME="${DOCKER_NAME:-mrshoofer-org}"
HEALTH_PATH="${HEALTH_PATH:-/}"
SSH_OPTS="-o StrictHostKeyChecking=no -o PreferredAuthentications=password -o PubkeyAuthentication=no -o ConnectTimeout=30"

need() { command -v "$1" >/dev/null || { echo "Missing dependency: $1" >&2; exit 1; }; }
need git

remote() {
  if [[ -n "${DEPLOY_PASS:-}" ]]; then
    need sshpass
    SSHPASS="$DEPLOY_PASS" sshpass -e ssh -T $SSH_OPTS "${DEPLOY_USER}@${DEPLOY_HOST}" "$@"
  else
    ssh -T -o StrictHostKeyChecking=no -o ConnectTimeout=30 "${DEPLOY_USER}@${DEPLOY_HOST}" "$@"
  fi
}

remote_scp() {
  local src="$1" dst="$2"
  if [[ -n "${DEPLOY_PASS:-}" ]]; then
    SSHPASS="$DEPLOY_PASS" sshpass -e scp $SSH_OPTS "$src" "${DEPLOY_USER}@${DEPLOY_HOST}:$dst"
  else
    scp -o StrictHostKeyChecking=no -o ConnectTimeout=30 "$src" "${DEPLOY_USER}@${DEPLOY_HOST}:$dst"
  fi
}

ensure_autostart_systemd() {
  [[ "$ENSURE_AUTOSTART" == "1" ]] || return 0
  echo "==> Ensuring ${SERVICE} starts on VPS reboot"
  remote "systemctl enable ${SERVICE} >/dev/null; systemctl is-enabled ${SERVICE}"
}

ensure_autostart_docker() {
  [[ "$ENSURE_AUTOSTART" == "1" ]] || return 0
  echo "==> Ensuring Docker + container restart on VPS reboot"
  remote "systemctl enable docker >/dev/null 2>&1 || true
    systemctl enable containerd >/dev/null 2>&1 || true
    systemctl is-enabled docker 2>/dev/null || true
    # container itself uses --restart always (set at run time)
  "
}

echo "==> Change set: ${FROM_REF}..${TO_REF}"
CHANGED=()
while IFS= read -r line; do
  [[ -n "$line" ]] && CHANGED+=("$line")
done < <(git diff --name-only "${FROM_REF}" "${TO_REF}" -- . || true)

if [[ ${#CHANGED[@]} -eq 0 && "$FORCE_FULL" != "1" ]]; then
  echo "No files changed in ${FROM_REF}..${TO_REF}. Nothing to deploy."
  # Still allow fixing autostart alone
  if [[ "$ENSURE_AUTOSTART" == "1" && "$DRY_RUN" != "1" ]]; then
    if [[ "$USE_DOCKER" == "1" ]]; then ensure_autostart_docker; else ensure_autostart_systemd; fi
  fi
  exit 0
fi

for f in "${CHANGED[@]:-}"; do
  printf '  - %s\n' "$f"
done

NEED_STATIC=0
NEED_CONFIG=0
NEED_CODE=0

for f in "${CHANGED[@]:-}"; do
  case "$f" in
    wwwroot/*) NEED_STATIC=1 ;;
    appsettings*.json) NEED_CONFIG=1 ;;
    Areas/*|Controllers/*|Services/*|Models/*|Views/*|Program.cs|*.csproj|*.cs|*.cshtml|Dockerfile|deploy/docker-compose.org.yml)
      NEED_CODE=1
      ;;
    *)
      if [[ "$f" == *.cs || "$f" == *.cshtml || "$f" == *.csproj ]]; then
        NEED_CODE=1
      fi
      ;;
  esac
done

if [[ "$FORCE_FULL" == "1" ]]; then
  NEED_CODE=1
fi

# Docker deploys bake wwwroot into the image — static-only still needs rebuild when USE_DOCKER=1
if [[ "$USE_DOCKER" == "1" && "$NEED_STATIC" == "1" ]]; then
  NEED_CODE=1
fi

MODE="systemd"
[[ "$USE_DOCKER" == "1" ]] && MODE="docker"
[[ "$SKIP_BLUE_GREEN" == "1" && "$USE_DOCKER" != "1" ]] && MODE="systemd-restart"

echo
echo "==> Plan"
echo "  static(wwwroot): $NEED_STATIC"
echo "  config:          $NEED_CONFIG"
echo "  rebuild app:     $NEED_CODE"
echo "  mode:            $MODE"
echo "  autostart:       $ENSURE_AUTOSTART"

if [[ "$DRY_RUN" == "1" ]]; then
  echo "DRY_RUN=1 — exiting before deploy."
  exit 0
fi

# ========== DOCKER MODE ==========
if [[ "$USE_DOCKER" == "1" ]]; then
  need docker

  if [[ "$NEED_CODE" == "1" || "$NEED_CONFIG" == "1" || "$FORCE_FULL" == "1" ]]; then
    echo
    echo "==> Docker build ${ORG_IMAGE}"
    # Prefer linux/amd64 for typical VPS; override with DOCKER_PLATFORM=
    PLATFORM_ARGS=()
    if [[ -n "${DOCKER_PLATFORM:-}" ]]; then
      PLATFORM_ARGS=(--platform "$DOCKER_PLATFORM")
    fi
    docker build "${PLATFORM_ARGS[@]}" -t "$ORG_IMAGE" -f Dockerfile .

    TAR="/tmp/mrshoofer-org-deploy-$$.tar.gz"
    echo "==> Saving image → $TAR"
    docker save "$ORG_IMAGE" | gzip > "$TAR"

    echo "==> Uploading image to ${DEPLOY_HOST}"
    remote_scp "$TAR" /tmp/mrshoofer-org-deploy.tar.gz
    rm -f "$TAR"

    # Upload compose helper (optional)
    if [[ -f deploy/docker-compose.org.yml ]]; then
      remote "mkdir -p ${DEPLOY_DIR}/deploy"
      remote_scp deploy/docker-compose.org.yml "${DEPLOY_DIR}/deploy/docker-compose.org.yml"
    fi

    # Upload config if changed
    if [[ "$NEED_CONFIG" == "1" ]]; then
      for f in appsettings.json appsettings.Production.json; do
        if [[ -f "$f" ]] && git diff --name-only "${FROM_REF}" "${TO_REF}" -- "$f" | grep -q .; then
          remote_scp "$f" "${DEPLOY_DIR}/$f"
        fi
      done
    fi

    echo "==> Blue-green Docker cutover on VPS"
    remote "set -e
      docker load < /tmp/mrshoofer-org-deploy.tar.gz
      rm -f /tmp/mrshoofer-org-deploy.tar.gz

      # Stop conflicting systemd host process if it holds 5055
      systemctl stop ${SERVICE} 2>/dev/null || true
      systemctl disable ${SERVICE} 2>/dev/null || true

      ACTIVE_PORT=\$(ss -tlnp | awk '/127.0.0.1:5055/ {print 5055} /127.0.0.1:5056/ {print 5056}' | head -1)
      [ -z \"\$ACTIVE_PORT\" ] && ACTIVE_PORT=${PORT_A}
      if [ \"\$ACTIVE_PORT\" = \"${PORT_A}\" ]; then NEW_PORT=${PORT_B}; else NEW_PORT=${PORT_A}; fi
      echo active=\$ACTIVE_PORT new=\$NEW_PORT

      STANDBY_NAME=${DOCKER_NAME}-next
      docker rm -f \$STANDBY_NAME 2>/dev/null || true
      docker run -d --name \$STANDBY_NAME --restart always \\
        -p 127.0.0.1:\$NEW_PORT:5000 \\
        -e ASPNETCORE_ENVIRONMENT=Production \\
        -e ASPNETCORE_URLS=http://0.0.0.0:5000 \\
        -e TZ=Asia/Tehran \\
        -v ${DEPLOY_DIR}/appsettings.json:/app/appsettings.json:ro \\
        ${ORG_IMAGE}

      ok=0
      for i in \$(seq 1 40); do
        code=\$(curl -s -m 2 -o /dev/null -w '%{http_code}' http://127.0.0.1:\$NEW_PORT${HEALTH_PATH} || true)
        if [ \"\$code\" = \"200\" ] || [ \"\$code\" = \"302\" ] || [ \"\$code\" = \"301\" ]; then ok=1; break; fi
        sleep 1
      done
      if [ \"\$ok\" != \"1\" ]; then
        echo 'Standby container health FAILED' >&2
        docker logs \$STANDBY_NAME 2>&1 | tail -50 >&2 || true
        docker rm -f \$STANDBY_NAME 2>/dev/null || true
        exit 1
      fi

      if grep -Rql \"127.0.0.1:\$ACTIVE_PORT\" /etc/nginx/sites-enabled/ /etc/nginx/conf.d/ 2>/dev/null; then
        sed -i \"s/127.0.0.1:\$ACTIVE_PORT/127.0.0.1:\$NEW_PORT/g\" /etc/nginx/sites-enabled/* /etc/nginx/conf.d/* 2>/dev/null || true
        nginx -t && systemctl reload nginx
        echo nginx cutover → \$NEW_PORT
      fi

      # Retire old container / host listeners on ACTIVE_PORT
      docker ps -q --filter publish=\$ACTIVE_PORT | xargs -r docker rm -f
      fuser -k \$ACTIVE_PORT/tcp 2>/dev/null || true

      # Rename standby → canonical name
      docker rm -f ${DOCKER_NAME} 2>/dev/null || true
      docker rename \$STANDBY_NAME ${DOCKER_NAME}
      docker update --restart always ${DOCKER_NAME} >/dev/null
      echo \$NEW_PORT > ${DEPLOY_DIR}/ACTIVE_PORT
      docker ps --filter name=${DOCKER_NAME} --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}'
    "

    ensure_autostart_docker
  else
    echo "==> Docker mode: no code/config changes — skipping image build"
    ensure_autostart_docker
  fi

  echo
  echo "==> Docker deploy finished."
  echo "    Container: ${DOCKER_NAME}  image: ${ORG_IMAGE}"
  echo "    Reboot-safe: docker --restart always + systemctl enable docker"
  exit 0
fi

# ========== SYSTEMD / BARE DLL MODE ==========
need dotnet

# --- static files (no restart) ---
if [[ "$NEED_STATIC" == "1" ]]; then
  echo
  echo "==> Syncing changed wwwroot files (no restart)"
  while IFS= read -r f; do
    [[ -f "$f" ]] || continue
    rel="${f#wwwroot/}"
    remote "mkdir -p ${DEPLOY_DIR}/wwwroot/$(dirname "$rel")"
    remote_scp "$f" "${DEPLOY_DIR}/wwwroot/${rel}"
    echo "  uploaded wwwroot/${rel}"
  done < <(git diff --name-only "${FROM_REF}" "${TO_REF}" -- wwwroot)
fi

# --- config (needs process recycle to pick up) ---
if [[ "$NEED_CONFIG" == "1" && "$NEED_CODE" != "1" ]]; then
  echo
  echo "==> Uploading appsettings (will recycle via blue-green/restart)"
  for f in appsettings.json appsettings.Production.json appsettings.Development.json; do
    if git diff --name-only "${FROM_REF}" "${TO_REF}" -- "$f" | grep -q .; then
      [[ -f "$f" ]] || continue
      remote_scp "$f" "${DEPLOY_DIR}/$f"
      echo "  uploaded $f"
      NEED_CODE=1
    fi
  done
fi

if [[ "$NEED_CODE" == "1" ]]; then
  echo
  echo "==> Building Release"
  dotnet build Application.csproj -c Release -v q

  DLL_LOCAL="bin/Release/net8.0/Application.dll"
  [[ -f "$DLL_LOCAL" ]] || { echo "Build output missing: $DLL_LOCAL" >&2; exit 1; }

  if [[ "$SKIP_BLUE_GREEN" == "1" ]]; then
    echo "==> Minimal-downtime restart deploy"
    remote_scp "$DLL_LOCAL" "${DEPLOY_DIR}/Application.dll.new"
    remote "set -e
      mv -f ${DEPLOY_DIR}/Application.dll.new ${DEPLOY_DIR}/Application.dll
      systemctl restart ${SERVICE}
      sleep 2
      systemctl is-active ${SERVICE}
      curl -sf -m 8 -o /dev/null -w 'health %{http_code}\\n' http://127.0.0.1:${PORT_A}${HEALTH_PATH} || true
    "
  else
    echo "==> Blue-green deploy (zero downtime)"
    ACTIVE_PORT="$(remote "ss -tlnp | awk '/127.0.0.1:5055/ {print 5055} /127.0.0.1:5056/ {print 5056}' | head -1" | tr -d '\r' || true)"
    if [[ -z "$ACTIVE_PORT" ]]; then
      ACTIVE_PORT="$PORT_A"
    fi
    if [[ "$ACTIVE_PORT" == "$PORT_A" ]]; then
      NEW_PORT="$PORT_B"
    else
      NEW_PORT="$PORT_A"
    fi
    echo "  active=${ACTIVE_PORT}  new=${NEW_PORT}"

    remote_scp "$DLL_LOCAL" "${DEPLOY_DIR}/Application.dll.new"

    remote "set -e
      cp -f ${DEPLOY_DIR}/Application.dll ${DEPLOY_DIR}/Application.dll.bak 2>/dev/null || true
      mv -f ${DEPLOY_DIR}/Application.dll.new ${DEPLOY_DIR}/Application.dll

      pkill -f 'ASPNETCORE_URLS=http://127.0.0.1:${NEW_PORT}' 2>/dev/null || true
      cd ${DEPLOY_DIR}
      ASPNETCORE_ENVIRONMENT=Production \\
      ASPNETCORE_URLS=http://127.0.0.1:${NEW_PORT} \\
      nohup ./Application > /tmp/org-standby.log 2>&1 &
      echo \$! > /tmp/org-standby.pid

      ok=0
      for i in \$(seq 1 30); do
        code=\$(curl -s -m 2 -o /dev/null -w '%{http_code}' http://127.0.0.1:${NEW_PORT}${HEALTH_PATH} || true)
        if [ \"\$code\" = \"200\" ] || [ \"\$code\" = \"302\" ] || [ \"\$code\" = \"301\" ]; then
          ok=1; break
        fi
        sleep 1
      done
      if [ \"\$ok\" != \"1\" ]; then
        echo 'Standby health check FAILED' >&2
        tail -40 /tmp/org-standby.log >&2 || true
        kill \$(cat /tmp/org-standby.pid) 2>/dev/null || true
        exit 1
      fi
      echo \"standby healthy on ${NEW_PORT}\"

      if grep -Rql \"127.0.0.1:${ACTIVE_PORT}\" /etc/nginx/sites-enabled/ /etc/nginx/conf.d/ 2>/dev/null; then
        sed -i \"s/127.0.0.1:${ACTIVE_PORT}/127.0.0.1:${NEW_PORT}/g\" /etc/nginx/sites-enabled/* /etc/nginx/conf.d/* 2>/dev/null || true
        nginx -t
        systemctl reload nginx
        echo \"nginx cutover → ${NEW_PORT}\"
      else
        echo \"WARN: nginx upstream ${ACTIVE_PORT} not found; cutover skipped\" >&2
      fi

      systemctl stop ${SERVICE} 2>/dev/null || true
      fuser -k ${ACTIVE_PORT}/tcp 2>/dev/null || true

      mkdir -p /etc/systemd/system/${SERVICE}.d
      cat > /etc/systemd/system/${SERVICE}.d/port.conf <<EOF
[Service]
Environment=ASPNETCORE_URLS=http://127.0.0.1:${NEW_PORT}
EOF
      kill \$(cat /tmp/org-standby.pid) 2>/dev/null || true
      sleep 1
      systemctl daemon-reload
      systemctl start ${SERVICE}
      sleep 2
      systemctl is-active ${SERVICE}
      curl -sf -m 8 -o /dev/null -w 'service health %{http_code}\\n' http://127.0.0.1:${NEW_PORT}${HEALTH_PATH} || true
      echo ${NEW_PORT} > ${DEPLOY_DIR}/ACTIVE_PORT
    "
  fi
fi

ensure_autostart_systemd

echo
echo "==> Deploy finished."
echo "    Tip: USE_DOCKER=1 DEPLOY_PASS=... ./deploy/scripts/zero-downtime-deploy.sh"
echo "    Tip: DRY_RUN=1 ...   # plan only"
echo "    Tip: ENSURE_AUTOSTART=1 is default (survives VPS reboot)"
