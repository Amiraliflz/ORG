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
#   STABLE_PROXY=auto             # auto|1|0 — blue/green only if stable :5055 proxy installed
#   USE_DOCKER=1                  # build/push Docker image instead of bare DLL
#   ENSURE_AUTOSTART=1            # default ON — survive VPS reboot
#   ORG_IMAGE=mrshoofer-org:latest
#
# Localhost API mesh: peer apps must always call http://127.0.0.1:5055/
# Install once: ./deploy/scripts/install-stable-localhost-proxy.sh
#
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

DEPLOY_HOST="${DEPLOY_HOST:-62.60.191.21}"
DEPLOY_USER="${DEPLOY_USER:-root}"
DEPLOY_DIR="${DEPLOY_DIR:-/var/www/org}"
SERVICE="${SERVICE:-org.service}"
# Peer apps / public nginx always call STABLE_PORT (never changes).
# Blue/green only flips BACKEND_A <-> BACKEND_B behind the stable proxy.
STABLE_PORT="${STABLE_PORT:-5055}"
PORT_A="${PORT_A:-${BACKEND_A:-15055}}"
PORT_B="${PORT_B:-${BACKEND_B:-15056}}"
BACKEND_A="$PORT_A"
BACKEND_B="$PORT_B"
FROM_REF="${FROM_REF:-HEAD~1}"
TO_REF="${TO_REF:-HEAD}"
DRY_RUN="${DRY_RUN:-0}"
FORCE_FULL="${FORCE_FULL:-0}"
# Default: same-port restart (safe for localhost API mesh). Use STABLE_PROXY=1 after install.
SKIP_BLUE_GREEN="${SKIP_BLUE_GREEN:-0}"
STABLE_PROXY="${STABLE_PROXY:-auto}"
USE_DOCKER="${USE_DOCKER:-0}"
ENSURE_AUTOSTART="${ENSURE_AUTOSTART:-1}"
ORG_IMAGE="${ORG_IMAGE:-mrshoofer-org:latest}"
DOCKER_NAME="${DOCKER_NAME:-mrshoofer-org}"
HEALTH_PATH="${HEALTH_PATH:-/health}"
SSH_OPTS="-o StrictHostKeyChecking=no -o PreferredAuthentications=password -o PubkeyAuthentication=no -o ConnectTimeout=30"

need() { command -v "$1" >/dev/null || { echo "Missing dependency: $1" >&2; exit 1; }; }
need git

remote() {
  if [[ -n "${DEPLOY_PASS:-}" ]]; then
    need sshpass
    SSHPASS="$DEPLOY_PASS" sshpass -e ssh -n -T $SSH_OPTS "${DEPLOY_USER}@${DEPLOY_HOST}" "$@"
  else
    ssh -n -T -o StrictHostKeyChecking=no -o ConnectTimeout=30 "${DEPLOY_USER}@${DEPLOY_HOST}" "$@"
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

# --full ships all commits not on origin/main (app + wwwroot), not only HEAD~1
if [[ "$FORCE_FULL" == "1" ]] && git rev-parse --verify origin/main >/dev/null 2>&1; then
  FROM_REF="origin/main"
fi

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

    echo "==> Docker cutover on VPS (stable :${STABLE_PORT} for peer apps)"
    remote "set -e
      docker load < /tmp/mrshoofer-org-deploy.tar.gz
      rm -f /tmp/mrshoofer-org-deploy.tar.gz

      # Stop conflicting systemd host process
      systemctl stop ${SERVICE} 2>/dev/null || true
      systemctl disable ${SERVICE} 2>/dev/null || true

      HAS_STABLE=0
      if [ -f /etc/nginx/conf.d/org-stable-localhost.conf ] && [ -f /etc/nginx/conf.d/org-backend-active.conf ]; then
        HAS_STABLE=1
      fi

      STANDBY_NAME=${DOCKER_NAME}-next
      docker rm -f \$STANDBY_NAME 2>/dev/null || true

      run_container() {
        local name=\$1 port=\$2
        docker run -d --name \$name --restart always \\
          -p 127.0.0.1:\$port:5000 \\
          -e ASPNETCORE_ENVIRONMENT=Production \\
          -e ASPNETCORE_URLS=http://0.0.0.0:5000 \\
          -e TZ=Asia/Tehran \\
          -v ${DEPLOY_DIR}/appsettings.json:/app/appsettings.json:ro \\
          ${ORG_IMAGE}
      }

      wait_healthy() {
        local port=\$1 name=\$2
        local ok=0 code
        for i in \$(seq 1 40); do
          code=\$(curl -s -m 2 -o /dev/null -w '%{http_code}' http://127.0.0.1:\$port${HEALTH_PATH} || true)
          if [ \"\$code\" = \"200\" ] || [ \"\$code\" = \"302\" ] || [ \"\$code\" = \"301\" ]; then ok=1; break; fi
          sleep 1
        done
        if [ \"\$ok\" != \"1\" ]; then
          echo \"Container health FAILED on :\$port\" >&2
          docker logs \$name 2>&1 | tail -50 >&2 || true
          docker rm -f \$name 2>/dev/null || true
          exit 1
        fi
      }

      promote_standby() {
        local port=\$1
        docker rm -f ${DOCKER_NAME} 2>/dev/null || true
        docker rename \$STANDBY_NAME ${DOCKER_NAME}
        docker update --restart always ${DOCKER_NAME} >/dev/null
        echo \$port > ${DEPLOY_DIR}/ACTIVE_BACKEND_PORT
        echo ${STABLE_PORT} > ${DEPLOY_DIR}/STABLE_PORT
        docker ps --filter name=${DOCKER_NAME} --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}'
      }

      if [ \"\$HAS_STABLE\" = \"1\" ]; then
        # Blue/green on backends only — peer apps keep calling :${STABLE_PORT}
        if [ -f ${DEPLOY_DIR}/ACTIVE_BACKEND_PORT ]; then
          ACTIVE_PORT=\$(cat ${DEPLOY_DIR}/ACTIVE_BACKEND_PORT | tr -d '\\r')
        elif ss -tlnp | grep -q '127.0.0.1:${BACKEND_B}'; then
          ACTIVE_PORT=${BACKEND_B}
        else
          ACTIVE_PORT=${BACKEND_A}
        fi
        if [ \"\$ACTIVE_PORT\" = \"${BACKEND_A}\" ]; then NEW_PORT=${BACKEND_B}; else NEW_PORT=${BACKEND_A}; fi
        echo \"docker blue-green: backend \$ACTIVE_PORT → \$NEW_PORT (stable ${STABLE_PORT})\"

        run_container \$STANDBY_NAME \$NEW_PORT
        wait_healthy \$NEW_PORT \$STANDBY_NAME

        cat > /etc/nginx/conf.d/org-backend-active.conf <<EOF
upstream org_app_backend {
    server 127.0.0.1:\$NEW_PORT;
}
EOF
        nginx -t && systemctl reload nginx
        echo \"stable proxy :${STABLE_PORT} → backend \$NEW_PORT\"

        docker ps -q --filter publish=\$ACTIVE_PORT | xargs -r docker rm -f
        fuser -k \$ACTIVE_PORT/tcp 2>/dev/null || true
        promote_standby \$NEW_PORT
        curl -sf -m 8 -o /dev/null -w 'stable health %{http_code}\\n' http://127.0.0.1:${STABLE_PORT}${HEALTH_PATH} || true
      else
        # Same-port restart on :${STABLE_PORT} — safe for localhost API mesh (short blip)
        echo \"docker same-port restart on :${STABLE_PORT} (install stable proxy for zero-downtime)\"
        docker ps -q --filter publish=${STABLE_PORT} | xargs -r docker rm -f
        docker rm -f ${DOCKER_NAME} 2>/dev/null || true
        fuser -k ${STABLE_PORT}/tcp 2>/dev/null || true
        sleep 1
        run_container \$STANDBY_NAME ${STABLE_PORT}
        wait_healthy ${STABLE_PORT} \$STANDBY_NAME
        promote_standby ${STABLE_PORT}
        curl -sf -m 8 -o /dev/null -w 'health %{http_code}\\n' http://127.0.0.1:${STABLE_PORT}${HEALTH_PATH} || true
      fi
    "

    ensure_autostart_docker
  else
    echo "==> Docker mode: no code/config changes — skipping image build"
    ensure_autostart_docker
  fi

  echo
  echo "==> Docker deploy finished."
  echo "    Container: ${DOCKER_NAME}  image: ${ORG_IMAGE}"
  echo "    Peer apps: always http://127.0.0.1:${STABLE_PORT}/"
  echo "    Reboot-safe: docker --restart always + systemctl enable docker"
  exit 0
fi

# ========== SYSTEMD / BARE DLL MODE ==========
need dotnet

# --- working-tree app files (so --full ships uncommitted views/css too) ---
if [[ "$FORCE_FULL" == "1" ]]; then
  echo
  echo "==> Syncing dirty app files from working tree (FORCE_FULL)"
  while IFS= read -r f; do
    [[ -z "$f" || ! -f "$f" ]] && continue
    case "$f" in
      wwwroot/*)
        rel="${f#wwwroot/}"
        remote "mkdir -p ${DEPLOY_DIR}/wwwroot/$(dirname "$rel")"
        remote_scp "$f" "${DEPLOY_DIR}/wwwroot/${rel}"
        echo "  uploaded wwwroot/${rel}"
        ;;
      *.cshtml)
        remote "mkdir -p ${DEPLOY_DIR}/$(dirname "$f")"
        remote_scp "$f" "${DEPLOY_DIR}/$f"
        echo "  uploaded $f"
        ;;
    esac
  done < <(git status --porcelain -u | awk '{print substr($0,4)}' | sed 's#^"##;s#"$##')
fi


# --- static files (no restart) ---
if [[ "$FORCE_FULL" == "1" ]]; then
  echo
  echo "==> FORCE_FULL: syncing ALL tracked wwwroot files (tar)"
  WWW_TAR="$(mktemp -t org-wwwroot.XXXXXX.tar.gz)"
  git ls-files -z wwwroot | tar -czf "$WWW_TAR" --null -T -
  remote "mkdir -p ${DEPLOY_DIR}"
  remote_scp "$WWW_TAR" /tmp/org-wwwroot-sync.tar.gz
  remote "set -e
    mkdir -p ${DEPLOY_DIR}
    tar -xzf /tmp/org-wwwroot-sync.tar.gz -C ${DEPLOY_DIR}
    rm -f /tmp/org-wwwroot-sync.tar.gz
    test -f ${DEPLOY_DIR}/wwwroot/cta-taxi.jpg
    test -f ${DEPLOY_DIR}/wwwroot/css/IndexPage.css
    test -f ${DEPLOY_DIR}/wwwroot/css/TaxiTripsIndex.css
    wc -c ${DEPLOY_DIR}/wwwroot/cta-taxi.jpg ${DEPLOY_DIR}/wwwroot/css/IndexPage.css ${DEPLOY_DIR}/wwwroot/css/TaxiTripsIndex.css
  "
  rm -f "$WWW_TAR"
  echo "  wwwroot full sync done"
elif [[ "$NEED_STATIC" == "1" ]]; then
  echo
  echo "==> Syncing changed wwwroot files (no restart)"
  while IFS= read -r f; do
    [[ -f "$f" ]] || continue
    rel="${f#wwwroot/}"
    remote "mkdir -p ${DEPLOY_DIR}/wwwroot/$(dirname "$rel")"
    remote_scp "$f" "${DEPLOY_DIR}/wwwroot/${rel}"
    echo "  uploaded wwwroot/${rel}"
  done < <(git diff --name-only "${FROM_REF}" "${TO_REF}" -- wwwroot || true)
fi

# --- config (upload whenever changed; recycle happens with code deploy / restart) ---
if [[ "$NEED_CONFIG" == "1" ]]; then
  echo
  echo "==> Uploading changed appsettings"
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

  # Detect stable localhost proxy (keeps :5055 fixed for peer apps)
  HAS_STABLE="$(remote "test -f /etc/nginx/conf.d/org-stable-localhost.conf && test -f /etc/nginx/conf.d/org-backend-active.conf && echo yes || echo no" | tr -d '\r' || echo no)"
  USE_STABLE=0
  if [[ "$HAS_STABLE" == "yes" && "$SKIP_BLUE_GREEN" != "1" ]]; then
    if [[ "$STABLE_PROXY" == "1" || "$STABLE_PROXY" == "yes" || "$STABLE_PROXY" == "auto" ]]; then
      USE_STABLE=1
    fi
  fi

  if [[ "$USE_STABLE" != "1" ]]; then
    if [[ "$HAS_STABLE" != "yes" ]]; then
      echo "==> Same-port restart (safe for localhost API mesh)"
      echo "    Tip: install stable proxy once, then zero-downtime blue/green works:"
      echo "         ./deploy/scripts/install-stable-localhost-proxy.sh"
    else
      echo "==> Minimal-downtime restart deploy"
    fi
    echo "==> Uploading Application.dll"
    remote_scp "$DLL_LOCAL" "${DEPLOY_DIR}/Application.dll.new"
    remote "set -e
      mv -f ${DEPLOY_DIR}/Application.dll.new ${DEPLOY_DIR}/Application.dll
      systemctl restart ${SERVICE}
      sleep 2
      systemctl is-active ${SERVICE}
      curl -sf -m 8 -o /dev/null -w 'health %{http_code}\\n' http://127.0.0.1:${STABLE_PORT}${HEALTH_PATH} || true
    "
  else
    echo "==> Blue-green behind stable :${STABLE_PORT} (backends ${BACKEND_A}/${BACKEND_B})"
    ACTIVE_PORT="$(remote "if [ -f ${DEPLOY_DIR}/ACTIVE_BACKEND_PORT ]; then cat ${DEPLOY_DIR}/ACTIVE_BACKEND_PORT; elif ss -tlnp | grep -q '127.0.0.1:${BACKEND_B}'; then echo ${BACKEND_B}; else echo ${BACKEND_A}; fi" | tr -d '\r' | head -1 || true)"
    if [[ -z "$ACTIVE_PORT" ]]; then
      ACTIVE_PORT="$BACKEND_A"
    fi
    if [[ "$ACTIVE_PORT" == "$BACKEND_A" ]]; then
      NEW_PORT="$BACKEND_B"
    else
      NEW_PORT="$BACKEND_A"
    fi
    echo "  active backend=${ACTIVE_PORT}  new backend=${NEW_PORT}  stable=${STABLE_PORT}"

    echo "==> Uploading Application.dll"
    remote_scp "$DLL_LOCAL" "${DEPLOY_DIR}/Application.dll.new"
    echo "==> Starting standby + cutover on VPS"

    # NOTE: never pkill -f ASPNETCORE_URLS=... — that matches this SSH session and kills deploy.
    remote "set -e
      cp -f ${DEPLOY_DIR}/Application.dll ${DEPLOY_DIR}/Application.dll.bak 2>/dev/null || true
      mv -f ${DEPLOY_DIR}/Application.dll.new ${DEPLOY_DIR}/Application.dll

      # Free new backend port only (do not match this shell's cmdline)
      fuser -k ${NEW_PORT}/tcp 2>/dev/null || true
      sleep 1

      cd ${DEPLOY_DIR}
      ASPNETCORE_ENVIRONMENT=Production \\
      ASPNETCORE_URLS=http://127.0.0.1:${NEW_PORT} \\
      nohup ./Application > /tmp/org-standby.log 2>&1 &
      echo \$! > /tmp/org-standby.pid
      echo \"standby pid=\$(cat /tmp/org-standby.pid) on ${NEW_PORT}\"

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

      # Cut over ONLY the stable proxy upstream — never rewrite peer/public :${STABLE_PORT}
      cat > /etc/nginx/conf.d/org-backend-active.conf <<EOF
upstream org_app_backend {
    server 127.0.0.1:${NEW_PORT};
}
EOF
      nginx -t
      systemctl reload nginx
      echo \"stable proxy :${STABLE_PORT} → backend ${NEW_PORT}\"

      # Stop old backend only — keep the healthy standby serving NEW_PORT.
      systemctl stop ${SERVICE} 2>/dev/null || true
      fuser -k ${ACTIVE_PORT}/tcp 2>/dev/null || true

      mkdir -p /etc/systemd/system/${SERVICE}.d
      cat > /etc/systemd/system/${SERVICE}.d/port.conf <<EOF
[Service]
Environment=ASPNETCORE_URLS=http://127.0.0.1:${NEW_PORT}
EOF
      systemctl daemon-reload

      # Prefer keeping the already-warm standby. Only hand off to systemd if standby dies.
      live_ok=0
      for i in \$(seq 1 10); do
        code=\$(curl -s -m 2 -o /dev/null -w '%{http_code}' http://127.0.0.1:${NEW_PORT}${HEALTH_PATH} || true)
        if [ \"\$code\" = \"200\" ] || [ \"\$code\" = \"302\" ] || [ \"\$code\" = \"301\" ]; then
          live_ok=1; break
        fi
        sleep 1
      done

      if [ \"\$live_ok\" != \"1\" ]; then
        echo 'Standby lost after cutover — starting systemd on ${NEW_PORT}' >&2
        kill \$(cat /tmp/org-standby.pid) 2>/dev/null || true
        fuser -k ${NEW_PORT}/tcp 2>/dev/null || true
        sleep 1
        systemctl start ${SERVICE}
        live_ok=0
        for i in \$(seq 1 30); do
          code=\$(curl -s -m 2 -o /dev/null -w '%{http_code}' http://127.0.0.1:${NEW_PORT}${HEALTH_PATH} || true)
          if [ \"\$code\" = \"200\" ] || [ \"\$code\" = \"302\" ] || [ \"\$code\" = \"301\" ]; then
            live_ok=1; break
          fi
          sleep 1
        done
      else
        echo \"keeping warm standby pid=\$(cat /tmp/org-standby.pid) on ${NEW_PORT}\"
        # Align systemd unit state for reboot; do not steal the live port now.
        systemctl reset-failed ${SERVICE} 2>/dev/null || true
      fi

      stab=\$(curl -s -m 8 -o /dev/null -w '%{http_code}' http://127.0.0.1:${STABLE_PORT}${HEALTH_PATH} || true)
      back=\$(curl -s -m 8 -o /dev/null -w '%{http_code}' http://127.0.0.1:${NEW_PORT}${HEALTH_PATH} || true)
      echo \"backend health \$back\"
      echo \"stable health \$stab\"

      if [ \"\$live_ok\" != \"1\" ] || { [ \"\$stab\" != \"200\" ] && [ \"\$stab\" != \"302\" ] && [ \"\$stab\" != \"301\" ]; }; then
        echo 'Post-cutover health check FAILED' >&2
        tail -40 /tmp/org-standby.log >&2 || true
        journalctl -u ${SERVICE} -n 40 --no-pager >&2 || true
        exit 1
      fi

      echo ${NEW_PORT} > ${DEPLOY_DIR}/ACTIVE_BACKEND_PORT
      echo ${STABLE_PORT} > ${DEPLOY_DIR}/STABLE_PORT
      echo cutover done
    "
  fi
fi

ensure_autostart_systemd

echo "==> Ensuring Ops restart sudoers + log directory"
remote_scp "$ROOT/deploy/scripts/install-ops-restart.sh" "/tmp/install-ops-restart.sh"
remote "bash /tmp/install-ops-restart.sh && rm -f /tmp/install-ops-restart.sh" || echo "    (ops restart install skipped or already configured)"

echo
echo "==> Deploy finished."
echo "    Peer apps: http://127.0.0.1:${STABLE_PORT}/ (unchanged)"
echo "    Tip: DRY_RUN=1 ...   # plan only"
echo "    Tip: ENSURE_AUTOSTART=1 is default (survives VPS reboot)"
