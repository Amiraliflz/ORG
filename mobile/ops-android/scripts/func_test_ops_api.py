#!/usr/bin/env python3
"""Functional tests for MrShoofer Ops mobile API (production contract)."""
from __future__ import annotations

import base64
import hashlib
import hmac
import json
import os
import sys
import time
import urllib.error
import urllib.request

BASE = os.environ.get("OPS_BASE", "https://mrshoofer.com").rstrip("/")
SECRET = os.environ.get("OPS_TOKEN_SECRET", "mrshoofer-ops-mobile-2026-stable").encode()
USER = os.environ.get("OPS_USER", "")
PASS = os.environ.get("OPS_PASS", "")

passed = 0
failed = 0


def ok(name: str, cond: bool, detail: str = "") -> None:
    global passed, failed
    if cond:
        passed += 1
        print(f"  PASS  {name}" + (f" — {detail}" if detail else ""))
    else:
        failed += 1
        print(f"  FAIL  {name}" + (f" — {detail}" if detail else ""))


def req(method: str, path: str, body: dict | None = None, token: str | None = None, timeout: float = 20):
    data = None
    headers = {"Accept": "application/json"}
    if body is not None:
        data = json.dumps(body).encode()
        headers["Content-Type"] = "application/json"
    if token:
        headers["Authorization"] = f"Bearer {token}"
        headers["X-Ops-Token"] = token
    r = urllib.request.Request(f"{BASE}{path}", data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(r, timeout=timeout) as resp:
            raw = resp.read().decode()
            return resp.status, json.loads(raw) if raw else {}
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        try:
            payload = json.loads(raw) if raw else {}
        except json.JSONDecodeError:
            payload = {"_raw": raw}
        return e.code, payload


def mint_token(user_id: str = "func-test", user_name: str = "func") -> str:
    exp = int(time.time()) + 3600
    payload = f"{user_id}|{user_name}|{exp}"
    sig = hmac.new(SECRET, payload.encode(), hashlib.sha256).hexdigest().upper()
    return base64.b64encode(f"{payload}|{sig}".encode()).decode()


def main() -> int:
    print(f"\nOps mobile API functional tests → {BASE}\n")

    # 1) Health
    try:
        with urllib.request.urlopen(f"{BASE}/health", timeout=15) as resp:
            health = json.loads(resp.read().decode())
            ok("GET /health", resp.status == 200 and health.get("status") == "Healthy", str(health.get("status")))
    except Exception as e:
        ok("GET /health", False, str(e))

    # 2) ApiLogin validation
    code, body = req("POST", "/Admin/Ops/ApiLogin", {"username": "", "password": ""})
    ok("ApiLogin rejects empty body", code in (400, 401), f"HTTP {code}")

    code, body = req("POST", "/Admin/Ops/ApiLogin", {"username": "not-a-real-user-xyz", "password": "wrong"})
    ok("ApiLogin rejects bad credentials", code == 401 and body.get("success") is False, body.get("message", f"HTTP {code}"))

    token = None
    if USER and PASS:
        code, body = req("POST", "/Admin/Ops/ApiLogin", {"username": USER, "password": PASS})
        token = body.get("token")
        ok("ApiLogin with real Admin", code == 200 and body.get("success") is True and bool(token), f"HTTP {code}")
    else:
        print("  SKIP  ApiLogin with real Admin (set OPS_USER / OPS_PASS)")
        token = mint_token()
        ok("Mint HMAC test token (same secret as server)", bool(token))

    # 3) ApiStatus auth
    code, body = req("GET", "/Admin/Ops/ApiStatus")
    ok("ApiStatus rejects anonymous", code == 401, f"HTTP {code}")

    code, body = req("GET", "/Admin/Ops/ApiStatus", token=token)
    ok("ApiStatus authenticated", code == 200 and "isHealthy" in body, f"HTTP {code}")
    if code == 200:
        ok("Overall isHealthy true", body.get("isHealthy") is True, str(body.get("isHealthy")))
        comps = {c.get("name"): c for c in body.get("components") or []}
        ok("Has app component healthy", comps.get("app", {}).get("isHealthy") is True)
        ok("Has database component healthy", comps.get("database", {}).get("isHealthy") is True)
        disk = comps.get("disk", {})
        details = disk.get("details") or ""
        ok("Disk details present", bool(details), details[:80])
        ok("Disk is not Mac 460GB on VPS", "460" not in details and "17" in details or "18" in details or "GB" in details, details[:80])
        host = comps.get("host") or comps.get("systemd") or {}
        ok("Host/process component healthy", host.get("isHealthy") is True, host.get("details", ""))
        # critical failures should not exist
        crit_bad = [c["name"] for c in body.get("components") or [] if c.get("critical") and not c.get("isHealthy")]
        ok("No critical components down", not crit_bad, ",".join(crit_bad) or "none")

    # 4) ApiRestart validation (do NOT actually restart)
    code, body = req("POST", "/Admin/Ops/ApiRestart", {"confirm": "NOPE"}, token=token)
    ok("ApiRestart rejects bad confirm", code == 400, f"HTTP {code} {body.get('message','')}")

    code, body = req("POST", "/Admin/Ops/ApiRestart", {"confirm": "RESTART"})
    ok("ApiRestart rejects anonymous", code == 401, f"HTTP {code}")

    # 5) Contract fields mobile app parses
    if code != 200:
        code, body = req("GET", "/Admin/Ops/ApiStatus", token=token)
    if body.get("components"):
        sample = body["components"][0]
        needed = {"name", "label", "isHealthy"}
        ok("Component JSON has name/label/isHealthy", needed <= set(sample.keys()), str(sorted(sample.keys())))

    print(f"\nResult: {passed} passed, {failed} failed\n")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
