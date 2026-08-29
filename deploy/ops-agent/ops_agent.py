#!/usr/bin/env python3
"""Always-on Ops agent — starts/restarts the web app when the main process is dead.

Independent of ASP.NET. Listens on 127.0.0.1 only; nginx exposes /ops-agent/.
Auth: same HMAC bearer tokens as Ops mobile (Ops:MobileTokenSecret).
"""
from __future__ import annotations

import base64
import hashlib
import hmac
import json
import os
import subprocess
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

HOST = os.environ.get("OPS_AGENT_HOST", "127.0.0.1")
PORT = int(os.environ.get("OPS_AGENT_PORT", "15057"))
SECRET = (
    os.environ.get("OPS_MOBILE_TOKEN_SECRET")
    or "mrshoofer-ops-mobile-2026-stable"
).encode("utf-8")
RESTART_SCRIPT = os.environ.get(
    "OPS_RESTART_SCRIPT", "/usr/local/bin/org-ops-restart.sh"
)
WATCHDOG_SECRET = os.environ.get("OPS_WATCHDOG_SECRET", "").strip()
RECOVER_SCRIPT = os.environ.get(
    "OPS_WATCHDOG_SCRIPT", "/usr/local/bin/org-watchdog.sh"
)


def _sign(payload: str) -> str:
    return hmac.new(SECRET, payload.encode("utf-8"), hashlib.sha256).hexdigest().upper()


def validate_token(token: str | None) -> bool:
    if not token:
        return False
    try:
        raw = base64.b64decode(token.strip()).decode("utf-8")
        parts = raw.split("|")
        if len(parts) != 4:
            return False
        payload = f"{parts[0]}|{parts[1]}|{parts[2]}"
        if not hmac.compare_digest(_sign(payload), parts[3]):
            return False
        exp = int(parts[2])
        return time.time() <= exp and bool(parts[0])
    except Exception:
        return False


def run_recover() -> tuple[bool, str]:
    script = RECOVER_SCRIPT
    if not script or not os.path.isfile(script):
        return run_restart()
    try:
        proc = subprocess.run(
            [script],
            capture_output=True,
            text=True,
            timeout=180,
        )
        out = ((proc.stdout or "") + "\n" + (proc.stderr or "")).strip()
        if proc.returncode == 0:
            return True, "watchdog recovery OK"
        return False, out[-800:] or f"watchdog failed ({proc.returncode})"
    except subprocess.TimeoutExpired:
        return False, "timeout waiting for watchdog"
    except Exception as e:
        return False, str(e)


def run_restart() -> tuple[bool, str]:
    try:
        proc = subprocess.run(
            [RESTART_SCRIPT],
            capture_output=True,
            text=True,
            timeout=120,
        )
        out = ((proc.stdout or "") + "\n" + (proc.stderr or "")).strip()
        if proc.returncode == 0:
            return True, "وب‌اپ از طریق Ops Agent راه‌اندازی شد"
        return False, out[-800:] or f"restart failed ({proc.returncode})"
    except subprocess.TimeoutExpired:
        return False, "timeout waiting for restart"
    except Exception as e:
        return False, str(e)


def watchdog_authorized(headers) -> bool:
    if not WATCHDOG_SECRET:
        return False
    token = headers.get("X-Watchdog-Secret") or headers.get("Authorization", "")
    if token.lower().startswith("bearer "):
        token = token[7:].strip()
    return token == WATCHDOG_SECRET


class Handler(BaseHTTPRequestHandler):
    server_version = "OrgOpsAgent/1.0"

    def log_message(self, fmt: str, *args) -> None:
        print(f"[ops-agent] {self.address_string()} {fmt % args}")

    def _json(self, code: int, payload: dict) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def _path(self) -> str:
        return self.path.split("?", 1)[0].rstrip("/") or "/"

    def _bearer(self) -> str | None:
        auth = self.headers.get("Authorization", "")
        if auth.lower().startswith("bearer "):
            return auth[7:].strip()
        return self.headers.get("X-Ops-Token")

    def do_GET(self) -> None:
        p = self._path()
        if p in ("/health", "/ops-agent/health", "/"):
            self._json(200, {"ok": True, "service": "ops-agent", "ready": True})
            return
        self._json(404, {"ok": False, "message": "not found"})

    def do_POST(self) -> None:
        p = self._path()
        if p in ("/recover", "/ops-agent/recover"):
            if not watchdog_authorized(self.headers):
                self._json(401, {"success": False, "message": "unauthorized"})
                return
            ok, msg = run_recover()
            self._json(200 if ok else 500, {"success": ok, "message": msg})
            return

        if p not in ("/restart", "/ops-agent/restart"):
            self._json(404, {"ok": False, "message": "not found"})
            return

        if not validate_token(self._bearer()):
            self._json(401, {"success": False, "message": "نشست منقضی شده"})
            return

        length = int(self.headers.get("Content-Length") or 0)
        raw = self.rfile.read(length) if length > 0 else b"{}"
        try:
            body = json.loads(raw.decode("utf-8") or "{}")
        except Exception:
            body = {}
        if str(body.get("confirm", "")).upper() != "RESTART":
            self._json(400, {"success": False, "message": "confirm=RESTART لازم است"})
            return

        ok, msg = run_restart()
        self._json(200 if ok else 500, {"success": ok, "message": msg})


def main() -> None:
    httpd = ThreadingHTTPServer((HOST, PORT), Handler)
    print(f"[ops-agent] listening on http://{HOST}:{PORT}")
    httpd.serve_forever()


if __name__ == "__main__":
    main()
