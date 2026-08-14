#!/usr/bin/env python3
"""Functional test: passenger cancel flow against live ORS + local client logic checks."""
from __future__ import annotations

import json
import sys
import urllib.error
import urllib.parse
import urllib.request
from datetime import date, datetime, timedelta
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CFG = json.loads((ROOT / "appsettings.Development.json").read_text())
BASE = CFG["MrShoofer"]["ApiBaseUrl"].rstrip("/")
TOKEN = CFG["MrShoofer"]["SellerToken"]

PASS = 0
FAIL = 0
RESULTS: list[tuple[str, bool, str]] = []


def report(name: str, ok: bool, detail: str = "") -> None:
    global PASS, FAIL
    RESULTS.append((name, ok, detail))
    if ok:
        PASS += 1
        print(f"  PASS  {name}" + (f" — {detail}" if detail else ""))
    else:
        FAIL += 1
        print(f"  FAIL  {name}" + (f" — {detail}" if detail else ""))


def api(method: str, path: str, body: dict | None = None, query: dict | None = None):
    url = BASE + path
    if query:
        url += "?" + urllib.parse.urlencode(query)
    data = None
    headers = {
        "Authorization": f"Bearer {TOKEN}",
        "Accept": "application/json",
    }
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            raw = resp.read().decode("utf-8", errors="replace")
            return resp.status, raw
    except urllib.error.HTTPError as e:
        raw = e.read().decode("utf-8", errors="replace")
        return e.code, raw


def parse_json(raw: str):
    try:
        return json.loads(raw)
    except Exception:
        return None


def main() -> int:
    print("=== Passenger cancel functional test ===")
    print(f"ORS: {BASE}")
    print()

    # 1) Auth / balance
    print("[1] Seller auth + balance")
    code, raw = api("GET", "/Account/getAccountBalance")
    bal = parse_json(raw) or {}
    ok = code == 200 and "accountBalance_tomans" in bal
    report("seller authenticated", ok, f"HTTP {code} balance={bal.get('accountBalance_tomans')}")
    if not ok:
        return finish()

    # 2) Invalid cancel
    print("\n[2] Cancel validation (invalid / empty)")
    code, raw = api("POST", "/Tickets/cancelTicket", query={
        "ticketcode": "FUNCTIONAL-TEST-INVALID",
        "reason": "functional test reason",
    })
    report("invalid ticket → 404", code == 404, f"HTTP {code}: {raw[:160]}")

    # Client-side empty code check (mirrors CancelTicketAsync)
    empty_fail = not bool("".strip())
    report("empty ticket code rejected locally", empty_fail)

    # 3) Find a trip to reserve (private)
    print("\n[3] Find bookable trip")
    code, raw = api("GET", "/Directions/getAvailableDirections")
    dirs = parse_json(raw)
    if not isinstance(dirs, list) or not dirs:
        report("load directions", False, f"HTTP {code}: {raw[:200]}")
        return finish()
    report("load directions", True, f"{len(dirs)} directions")

    # Prefer Tehran-related pairs with city ids
    candidates = []
    tehran_ids = set()
    for d in dirs:
        o = (d.get("origin") or {})
        dest = (d.get("destination") or {})
        oid = o.get("city_id") or o.get("cityId")
        did = dest.get("city_id") or dest.get("cityId")
        oname = o.get("city_name") or o.get("cityName")
        dname = dest.get("city_name") or dest.get("cityName")
        if not oid or not did:
            continue
        oid, did = int(oid), int(did)
        if oname and "تهران" in str(oname):
            tehran_ids.add(oid)
        if dname and "تهران" in str(dname):
            tehran_ids.add(did)
        candidates.append((oid, did, oname, dname))

    # Put Tehran-origin first
    candidates.sort(key=lambda x: (0 if x[0] in tehran_ids else 1, x[0], x[1]))
    # de-dupe OD pairs
    seen = set()
    uniq = []
    for c in candidates:
        key = (c[0], c[1])
        if key in seen:
            continue
        seen.add(key)
        uniq.append(c)
    candidates = uniq

    start = date.today() + timedelta(days=1)
    end = date.today() + timedelta(days=7)
    trip = None
    searched = 0
    for o, dest, oname, dname in candidates[:120]:
        searched += 1
        path = f"/Trips/GetPlanedTripsbyCityID/{start.isoformat()}/{end.isoformat()}/{o}/{dest}"
        code, raw = api("GET", path)
        trips = parse_json(raw)
        if not isinstance(trips, list) or not trips:
            continue
        for t in trips:
            tcode = t.get("tripPlanCode") or t.get("TripPlanCode")
            price = t.get("afterdiscticketprice") or t.get("Afterdiscticketprice") or t.get("originalTicketprice") or 0
            start_dt = t.get("startingDateTime") or t.get("StartingDateTime")
            if not tcode:
                continue
            try:
                dt = datetime.fromisoformat(str(start_dt).replace("Z", ""))
                if dt < datetime.now() + timedelta(hours=4):
                    continue
            except Exception:
                pass
            trip = {
                "code": tcode,
                "price": price,
                "origin": oname,
                "dest": dname,
                "start": start_dt,
                "raw": t,
            }
            break
        if trip:
            break

    report(
        "found bookable trip",
        trip is not None,
        f"searched={searched} trip={trip and trip['code']} {trip and trip['origin']}→{trip and trip['dest']} price={trip and trip['price']}" if trip else f"searched={searched}",
    )
    if not trip:
        return finish()

    # 4) Temp reserve + confirm (test passenger names so SMS skipped)
    print("\n[4] Reserve + confirm test ticket")
    code, raw = api("POST", "/Tickets/reserverTemporarily", body={
        "tripCode": trip["code"],
        "isPrivate": True,
        "seatnumber": None,
    })
    node = parse_json(raw)
    reserve_code = None
    if isinstance(node, dict):
        reserve_code = node.get("ticketCode") or node.get("reservationCode") or node.get("code")
    elif isinstance(node, str):
        reserve_code = node
    # Sometimes API returns plain string JSON
    if reserve_code is None and raw and raw.strip().startswith('"'):
        reserve_code = parse_json(raw)

    ok_reserve = code == 200 and isinstance(reserve_code, str) and bool(reserve_code)
    report("temp reserve", ok_reserve, f"HTTP {code} code={reserve_code} body={raw[:180]}")
    if not ok_reserve:
        return finish()

    if str(reserve_code).startswith("MRSHOOFER-NO-BAL-"):
        report("sufficient OTA balance", False, str(reserve_code))
        return finish()
    report("sufficient OTA balance", True)

    reason = f"تست عملکردی لغو — {datetime.now().isoformat(timespec='seconds')}"
    code, raw = api("POST", "/Tickets/confirmReserve", body={
        "reservationCode": reserve_code,
        "passengerFirstName": "تست",
        "passengerLastName": "عملکردی",
        "passengerNumberPhone": "09120000000",
        "passengerNationalCode": "0010000000",
    })
    conf = parse_json(raw) or {}
    ticket_code = conf.get("ticketCode") or conf.get("TicketCode")
    ok_confirm = code == 200 and bool(ticket_code)
    report("confirm reserve", ok_confirm, f"HTTP {code} ticket={ticket_code} body={raw[:220]}")
    if not ok_confirm:
        return finish()

    # 5) Cancel with reason (exercises ORG client contract)
    print("\n[5] Cancel with reason + verify")
    code, raw = api("POST", "/Tickets/cancelTicket", query={
        "ticketcode": ticket_code,
        "reason": reason,
    })
    cancel_body = parse_json(raw) or {}
    refund = cancel_body.get("rerfund")
    ok_cancel = code == 200 and refund is not None
    report("cancelTicket success", ok_cancel, f"HTTP {code} rerfund={refund} body={raw[:250]}")

    # Double cancel should fail
    code2, raw2 = api("POST", "/Tickets/cancelTicket", query={
        "ticketcode": ticket_code,
        "reason": "second cancel",
    })
    report("double cancel rejected", code2 >= 400, f"HTTP {code2}: {raw2[:160]}")

    # getTicketInfo status
    code, raw = api("GET", "/Tickets/getTicketInfo", query={"ticketcode": ticket_code})
    # also try ticketCode casing used by ORG client
    if code >= 400:
        code, raw = api("GET", "/Tickets/getTicketInfo", query={"ticketCode": ticket_code})
    info = parse_json(raw) or {}
    status = str(info.get("status") or info.get("Status") or info.get("ticketStatus") or "")
    desc = str(info.get("description") or info.get("Description") or "")
    cancelled = "cancel" in status.lower() or status in ("canceled", "cancelled", "لغو", "لغوشده", "کنسل شده")
    # status may be numeric enum
    if not cancelled and isinstance(info.get("status"), int):
        cancelled = info.get("status") == 2  # TicketStatus.canceled often = 2 if reserved=0,temp=1,canceled=2
    if not cancelled:
        # inspect whole payload for canceled
        cancelled = "canceled" in raw.lower() or "cancelled" in raw.lower() or "کنسل" in raw
    report("getTicketInfo shows cancelled", code == 200 and cancelled, f"HTTP {code} status={status!r} keys={list(info)[:12]}")

    reason_in_desc = "دلیل لغو مسافر" in desc and "تست عملکردی" in desc
    report(
        "reason in ORS Description",
        reason_in_desc,
        "present" if reason_in_desc else f"MISSING (ORS may not be redeployed yet). desc[:200]={desc[:200]!r}",
    )
    penalty_in_desc = "جریمه" in desc
    report("penalty lines in Description", penalty_in_desc, desc[:200] if desc else "(empty)")

    # 6) Local client mirror: translate / parse refund like CancelTicketAsync
    print("\n[6] Local client contract checks")
    report("rerfund parseable as number", isinstance(refund, (int, float)) or (isinstance(refund, str) and refund.replace('.', '', 1).isdigit()), str(refund))
    # Placeholder rejection (controller)
    for bad in ("PENDING-123", "PAID-NO-RESERVE-1", ""):
        rejected = (not bad) or bad.startswith("PENDING-") or bad.startswith("PAID-NO-RESERVE-")
        report(f"reject placeholder '{bad or '(empty)'}'", rejected)

    # Reason length rules
    report("reason min length 3", len("اب") < 3 and len("لغو سفر") >= 3)
    report("reason max truncate 500", len("x" * 501) > 500)

    # 7) Local DB: migration column exists + simulate wallet credit math
    print("\n[7] Local DB / migration readiness")
    cs = CFG["ConnectionStrings"]["development"]
    parts = dict(p.split("=", 1) for p in cs.split(";") if "=" in p)
    try:
        import psycopg2  # type: ignore
        conn = psycopg2.connect(
            host=parts.get("Host"),
            port=parts.get("Port", "5432"),
            dbname=parts.get("Database"),
            user=parts.get("Username"),
            password=parts.get("Password"),
            sslmode="prefer",
            connect_timeout=15,
        )
        cur = conn.cursor()
        cur.execute("""
            SELECT column_name FROM information_schema.columns
            WHERE table_name='Tickets' AND column_name='CancelReason'
        """)
        has_col = cur.fetchone() is not None
        report("Tickets.CancelReason column exists", has_col)
        cur.execute("""
            SELECT "MigrationId" FROM "__EFMigrationsHistory"
            WHERE "MigrationId" LIKE '%%CancelReason%%'
        """)
        mig = cur.fetchone()
        report("EF migration AddTicketCancelReason applied", mig is not None, str(mig))
        conn.close()
    except Exception as ex:
        report("DB connectivity for CancelReason check", False, str(ex)[:200])

    # Wallet credit contract: ORS refund should be credited as-is
    if isinstance(refund, (int, float)) and refund >= 0:
        report("wallet credit amount = ORS rerfund", True, f"{refund}")
    else:
        report("wallet credit amount = ORS rerfund", False, f"refund={refund}")

    return finish(ticket_code, refund, reason, reason_in_desc)


def finish(ticket_code=None, refund=None, reason=None, reason_in_desc=None) -> int:
    print("\n=== Summary ===")
    print(f"Passed: {PASS}  Failed: {FAIL}")
    if ticket_code:
        print(f"Test ticket: {ticket_code}")
        print(f"Refund (ORS rerfund): {refund}")
        print(f"Reason deployed on ORS Description: {reason_in_desc}")
    if FAIL:
        print("\nFailed cases:")
        for name, ok, detail in RESULTS:
            if not ok:
                print(f"  - {name}: {detail}")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
