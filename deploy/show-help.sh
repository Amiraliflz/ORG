#!/usr/bin/env bash
# Deploy help banner for MrShoofer ORG (sale app).
# Shown when a workspace terminal opens; also: ./deploy/show-help.sh
#
set +e

BOLD=$'\033[1m'
DIM=$'\033[2m'
CYAN=$'\033[36m'
GREEN=$'\033[32m'
YELLOW=$'\033[33m'
RESET=$'\033[0m'

cat <<EOF

${CYAN}${BOLD}══════════════════════════════════════════════════════════${RESET}
${BOLD}  MrShoofer ORG — deploy help${RESET}
${CYAN}${BOLD}══════════════════════════════════════════════════════════${RESET}

  ${GREEN}${BOLD}./deploy.sh${RESET}              safe deploy (keeps :5055 for peer apps)
  ${GREEN}./deploy.sh --fast${RESET}       short same-port restart
  ${GREEN}./deploy.sh --full${RESET}       force full rebuild
  ${GREEN}./deploy.sh --dry-run${RESET}    plan only
  ${GREEN}./deploy.sh --docker${RESET}     Docker deploy (also stable :5055)

  ${DIM}One-time (already done on live if you see backends 15055/15056):${RESET}
  ${YELLOW}./deploy/scripts/install-stable-localhost-proxy.sh${RESET}

  ${DIM}Config:${RESET}  .env.deploy   (DEPLOY_HOST / DEPLOY_PASS — gitignored)
  ${DIM}Target:${RESET}  root@62.60.191.21  →  peer APIs use ${BOLD}http://127.0.0.1:5055/${RESET}

  ${DIM}Re-show this help anytime:${RESET}  ${GREEN}./deploy/show-help.sh${RESET}

${CYAN}${BOLD}══════════════════════════════════════════════════════════${RESET}

EOF
