#!/usr/bin/env python3
"""
regression_check.py - Civil 3D MCP regression harness.

Runs a fixed sequence of operations and asserts on the results, so a future
build (v29, a new Civil 3D release, a refactor) can be checked in one shot
instead of by hand. Two independent sections:

  A) BRIDGE checks - drive the live .NET plugin bridge (default
     http://localhost:8270) and assert on the returned JSON. Covers the
     plugin paths most likely to regress:
       - COGO point round-trip (guards Northing/Easting swap)
       - layer + geometry creation (draw_line)
       - style creation + listing (guards style-collection resolution)
       - line AND arc label round-trip via add_line_label  <-- the v28 fix
         "fix(labels): resolve curve label style separately"
     Requires Civil 3D running with MCPSTART. If the bridge is unreachable the
     whole section auto-SKIPs (not a failure) so the suite still runs anywhere.

  B) INPUT-INTERPRETATION checks - guard how source data is read BEFORE it is
     sent to the bridge. This is the other frequent error source: getting a
     PNEZD vs PENZD column order wrong (N/E swap), or mis-reading values off a
     PDF / scanned plan / field-note image.
       - Deterministic file-format cases run against embedded fixtures (no CAD).
       - Vision/OCR cases are validated against LOCKED ground truth via
         validate_extraction(); you fill in ground truth per fixture, the agent
         (or an OCR step) supplies the extraction, the harness gates it.

Usage:
    python regression_check.py                     # both sections
    python regression_check.py --no-bridge         # section B only
    python regression_check.py --bridge-url URL    # custom bridge endpoint
    python regression_check.py --save-test         # also test SAVEAS round-trip

Exit code: 0 = no failures (skips are OK), 1 = one or more failures.

NOTE ON RUNNING LOCATION: the bridge listens on the Windows host where Civil 3D
runs. Run this script on that host (or any machine that can reach the bridge
URL). Section B has no such requirement.
"""

import argparse
import base64
import json
import math
import os
import sys
import tempfile
import time
import urllib.request
import urllib.error

# Minimal valid 1x1 PNG, used by the raster-attach regression test.
TINY_PNG_B64 = (
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk"
    "+M8AAAMBAQAY3Y2wAAAAAElFTkSuQmCC"
)

DEFAULT_BRIDGE_URL = os.environ.get("CAD_BRIDGE_URL", "http://localhost:8270")
TOL = 1e-3  # coordinate tolerance (drawing units)


# ---------------------------------------------------------------------------
# Bridge client (mirrors server.py call_bridge, stdlib-only so the harness has
# zero install deps and can run even without the MCP server's venv)
# ---------------------------------------------------------------------------

class BridgeError(Exception):
    pass


class BridgeDown(Exception):
    pass


def call_bridge(action, params=None, url=DEFAULT_BRIDGE_URL, timeout=30):
    payload = json.dumps({"action": action, "params": params or {}}).encode("utf-8")
    req = urllib.request.Request(
        url, data=payload, headers={"Content-Type": "application/json"}
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            result = json.loads(resp.read().decode("utf-8"))
    except urllib.error.URLError as e:
        raise BridgeDown(f"Cannot reach bridge at {url}: {e}")
    if not result.get("success"):
        raise BridgeError(result.get("error", "unknown error from bridge"))
    return result.get("result", {})


# ---------------------------------------------------------------------------
# Tiny test runner
# ---------------------------------------------------------------------------

class Skip(Exception):
    pass


class Runner:
    def __init__(self):
        self.passed = 0
        self.failed = 0
        self.skipped = 0
        self.failures = []

    def run(self, name, fn):
        try:
            fn()
        except Skip as s:
            self.skipped += 1
            print(f"  SKIP  {name}: {s}")
        except AssertionError as e:
            self.failed += 1
            self.failures.append((name, str(e)))
            print(f"  FAIL  {name}: {e}")
        except Exception as e:  # noqa: BLE001 - report anything unexpected
            self.failed += 1
            self.failures.append((name, repr(e)))
            print(f"  FAIL  {name}: {e!r}")
        else:
            self.passed += 1
            print(f"  PASS  {name}")

    def summary(self):
        print("\n" + "=" * 60)
        print(f"  {self.passed} passed, {self.failed} failed, {self.skipped} skipped")
        if self.failures:
            print("  Failures:")
            for n, m in self.failures:
                print(f"    - {n}: {m}")
        print("=" * 60)
        return 0 if self.failed == 0 else 1


class Tee:
    """Write to several streams at once (console + a results file)."""

    def __init__(self, *streams):
        self.streams = streams

    def write(self, data):
        for s in self.streams:
            s.write(data)

    def flush(self):
        for s in self.streams:
            s.flush()


def approx(a, b, tol=TOL):
    return abs(a - b) <= tol


# ---------------------------------------------------------------------------
# Shared geometry helper (the azimuth->coordinate math the agent relies on when
# feeding draw_line; mirrors server.py draw_line_by_azimuth so a change there
# can be cross-checked here)
# ---------------------------------------------------------------------------

def azimuth_to_endpoint(e, n, az_deg, az_min, az_sec, dist):
    az = math.radians(az_deg + az_min / 60.0 + az_sec / 3600.0)
    # Azimuth is clockwise from North: dE uses sin, dN uses cos.
    return (e + dist * math.sin(az), n + dist * math.cos(az))


# ===========================================================================
# SECTION A - LIVE BRIDGE CHECKS
# ===========================================================================

def section_a(runner, url, save_test):
    print("\n[A] BRIDGE CHECKS  (" + url + ")")

    # Fail fast to a clean SKIP for the whole section if the bridge is down.
    try:
        ping = call_bridge("ping", url=url)
    except BridgeDown as e:
        print(f"  -- bridge unreachable, skipping section A: {e}")
        for n in ("ping", "cogo_point_roundtrip", "layer_and_geometry",
                  "style_create_and_list", "line_label_roundtrip",
                  "arc_label_roundtrip", "raster_image_attach",
                  "import_points_file"):
            runner.run(n, lambda: (_ for _ in ()).throw(Skip("bridge down")))
        return

    tag = str(int(time.time()))  # unique suffix so reruns don't collide

    def t_ping():
        assert ping.get("status") == "ok", f"status={ping.get('status')}"
        assert ping.get("app"), "no app field"

    def t_cogo():
        # Known point with a swap-detecting signature: N != E by a wide margin.
        N, E, Z = 5000.000, 1000.000, 12.345
        desc = f"RTCHK_{tag}"
        call_bridge("create_cogo_point", {
            "northing": N, "easting": E, "elevation": Z, "rawDescription": desc
        }, url=url)
        pts = call_bridge("list_cogo_points", url=url)
        if isinstance(pts, dict):  # some builds wrap lists
            pts = pts.get("value", pts.get("points", []))
        mine = [p for p in pts if p.get("rawDescription") == desc]
        assert mine, f"created point {desc} not found in list"
        p = mine[-1]
        # The crucial guard: northing/easting must NOT be swapped.
        assert approx(p["northing"], N), f"northing={p['northing']} expected {N}"
        assert approx(p["easting"], E), f"easting={p['easting']} expected {E}"
        assert approx(p["elevation"], Z), f"elev={p['elevation']} expected {Z}"

    def t_layer_geo():
        layer = f"RTCHK-{tag}"
        call_bridge("create_layer", {"name": layer, "color": 2}, url=url)
        call_bridge("set_current_layer", {"name": layer}, url=url)
        # Draw a closed 4-leg parcel by azimuth; assert zero misclosure and that
        # each draw returns an entityId.
        legs = [(90, 0, 0, 60), (0, 0, 0, 40), (270, 0, 0, 60), (180, 0, 0, 40)]
        e, n = 1000.0, 500.0
        start = (e, n)
        for (d, m, s, dist) in legs:
            e2, n2 = azimuth_to_endpoint(e, n, d, m, s, dist)
            r = call_bridge("draw_line", {"start": [e, n, 0], "end": [e2, n2, 0]}, url=url)
            assert r.get("entityId"), f"draw_line returned no entityId: {r}"
            e, n = e2, n2
        mis = math.hypot(e - start[0], n - start[1])
        assert mis <= TOL, f"traverse misclosure {mis:.6f} > {TOL}"

    def t_styles():
        ps = f"RTCHK Pt {tag}"
        ls = f"RTCHK Line {tag}"
        call_bridge("create_point_style", {"name": ps, "markerType": "x"}, url=url)
        call_bridge("create_line_label_style", {"name": ls}, url=url)
        pstyles = call_bridge("list_point_styles", url=url)
        lstyles = call_bridge("list_line_label_styles", url=url)
        pnames = [x.get("name") for x in (pstyles if isinstance(pstyles, list) else [])]
        lnames = [x.get("name") for x in (lstyles if isinstance(lstyles, list) else [])]
        assert ps in pnames, f"point style {ps!r} not listed"
        assert ls in lnames, f"line label style {ls!r} not listed"

    def _draw_and_label(action, draw_params, style):
        r = call_bridge(action, draw_params, url=url)
        eid = r.get("entityId")
        assert eid, f"{action} returned no entityId: {r}"
        handle = format(int(eid), "X")  # decimal entityId -> hex handle
        lab = call_bridge("add_line_label", {
            "entityHandle": handle, "styleName": style, "ratio": 0.5
        }, url=url)
        assert lab.get("labelId"), f"add_line_label gave no labelId: {lab}"

    def t_line_label():
        # Built-in general LINE label style; this is the path that worked pre-v28.
        layer = f"RTCHK-LBL-{tag}"
        call_bridge("create_layer", {"name": layer, "color": 3}, url=url)
        call_bridge("set_current_layer", {"name": layer}, url=url)
        _draw_and_label("draw_line",
                        {"start": [0, 0, 0], "end": [50, 0, 0]},
                        "Bearing and Distance")

    def t_arc_label():
        # The v28 regression specifically: labeling an ARC needs the curve label
        # style collection. If that resolution breaks again, this fails.
        layer = f"RTCHK-ARC-{tag}"
        call_bridge("create_layer", {"name": layer, "color": 4}, url=url)
        call_bridge("set_current_layer", {"name": layer}, url=url)
        _draw_and_label("draw_arc",
                        {"center": [0, 0, 0], "radius": 15,
                         "startAngle": 0, "endAngle": 180},
                        "Bearing and Distance")

    def t_raster_attach():
        # Headless raster attach (the attach_raster_image tool). Writes a tiny
        # PNG to disk on this host, attaches it, asserts an entity came back.
        tmp = os.path.join(tempfile.gettempdir(), f"rtchk_{tag}.png")
        with open(tmp, "wb") as fh:
            fh.write(base64.b64decode(TINY_PNG_B64))
        layer = f"RTCHK-IMG-{tag}"
        call_bridge("create_layer", {"name": layer, "color": 5}, url=url)
        call_bridge("set_current_layer", {"name": layer}, url=url)
        r = call_bridge("attach_raster_image", {
            "path": tmp.replace("\\", "/"),
            "insertion": [200, 200, 0],
            "width": 50,
            "rotation": 0,
        }, url=url)
        assert r.get("attached") is True, f"attach result: {r}"
        assert r.get("entityId"), f"no entityId returned: {r}"

    def t_import_points():
        # Bulk import: PNEZD (comma) and PENZD (whitespace). Verify the PENZD
        # point lands with N/E un-swapped - the format-discipline guard.
        tagd = f"RTIMP{tag}"
        f1 = os.path.join(tempfile.gettempdir(), f"rtimp_pnezd_{tag}.txt")
        with open(f1, "w") as fh:
            fh.write(f"9001,4000.000,1000.000,5.0,{tagd}A\n"
                     f"9002,4001.000,1001.000,6.0,{tagd}B\n")
        r1 = call_bridge("import_points_file",
                         {"path": f1.replace("\\", "/"), "format": "PNEZD"}, url=url)
        assert r1.get("created") == 2, f"PNEZD import: {r1}"
        # PENZD columns are P E N Z D, so easting comes before northing.
        f2 = os.path.join(tempfile.gettempdir(), f"rtimp_penzd_{tag}.txt")
        with open(f2, "w") as fh:
            fh.write(f"9003 1234.000 5678.000 7.0 {tagd}C\n")   # E=1234, N=5678
        r2 = call_bridge("import_points_file",
                         {"path": f2.replace("\\", "/"), "format": "PENZD"}, url=url)
        assert r2.get("created") == 1, f"PENZD import: {r2}"
        pts = call_bridge("list_cogo_points", url=url)
        if isinstance(pts, dict):
            pts = pts.get("value", pts.get("points", []))
        mine = [pt for pt in pts if pt.get("rawDescription") == f"{tagd}C"]
        assert mine, "PENZD-imported point not found in list"
        pt = mine[-1]
        assert approx(pt["northing"], 5678.0), f"PENZD northing wrong (N/E swap?): {pt}"
        assert approx(pt["easting"], 1234.0), f"PENZD easting wrong (N/E swap?): {pt}"

    runner.run("ping", t_ping)
    runner.run("cogo_point_roundtrip", t_cogo)
    runner.run("layer_and_geometry", t_layer_geo)
    runner.run("style_create_and_list", t_styles)
    runner.run("line_label_roundtrip", t_line_label)
    runner.run("arc_label_roundtrip", t_arc_label)
    runner.run("raster_image_attach", t_raster_attach)
    runner.run("import_points_file", t_import_points)

    if save_test:
        def t_save():
            out = os.path.join(tempfile.gettempdir(), f"rtchk_{tag}.dwg").replace("\\", "/")
            lisp = (f'(progn (setvar "FILEDIA" 0)'
                    f'(command "_.SAVEAS" "" "{out}")(setvar "FILEDIA" 1)(princ))')
            call_bridge("send_command", {"command": lisp}, url=url)
            time.sleep(2)
            assert os.path.exists(out) and os.path.getsize(out) > 0, \
                f"SAVEAS to new name did not produce {out}"
        runner.run("saveas_new_name_roundtrip", t_save)


# ===========================================================================
# SECTION B - INPUT INTERPRETATION CHECKS  (no CAD required)
# ===========================================================================

# Deterministic parser under test. Frequent real-world bug: confusing PNEZD with
# PENZD and silently swapping Northing/Easting.
def parse_point_file(text, fmt):
    fmt = fmt.upper()
    order = {"PNEZD": ("P", "N", "E", "Z", "D"),
             "PENZD": ("P", "E", "N", "Z", "D")}[fmt]
    rows = []
    for line in text.strip().splitlines():
        line = line.strip()
        if not line:
            continue
        parts = line.split(",")
        # First 4 fields are numeric; the description (D) may itself contain
        # commas, so re-join the remainder.
        nums = parts[:4]
        desc = ",".join(parts[4:]).strip()
        rec = {}
        for key, val in zip(order[:4], nums):
            rec[key] = float(val) if key != "P" else int(float(val))
        rec["D"] = desc
        # Normalize to canonical fields regardless of source order.
        rows.append({"point": rec["P"], "N": rec["N"], "E": rec["E"],
                     "Z": rec["Z"], "desc": rec["D"]})
    return rows


# Real Lab 4 Survey-1 data (PNEZD). Ground truth = exact source values.
PNEZD_FIXTURE = """\
61,915.631,1110.528,167.111,IP
62,971.943,1160.370,152.222,OCM 78H4456
63,962.413,1110.095,157.362,OCM 79H4411
64,965.611,995.726,149.372,TH1
65,935.123,1069.787,155.391,TH4
"""

# Same five points re-serialized in PENZD (P,E,N,Z,D) order. Constructed swap
# fixture: proves the parser maps E/N by the declared format, not by position.
PENZD_FIXTURE = """\
61,1110.528,915.631,167.111,IP
62,1160.370,971.943,152.222,OCM 78H4456
63,1110.095,962.413,157.362,OCM 79H4411
64,995.726,965.611,149.372,TH1
65,1069.787,935.123,155.391,TH4
"""

# Canonical expected output (same for both fixtures - that's the whole point).
EXPECTED_POINTS = [
    {"point": 61, "N": 915.631, "E": 1110.528, "Z": 167.111, "desc": "IP"},
    {"point": 62, "N": 971.943, "E": 1160.370, "Z": 152.222, "desc": "OCM 78H4456"},
    {"point": 63, "N": 962.413, "E": 1110.095, "Z": 157.362, "desc": "OCM 79H4411"},
    {"point": 64, "N": 965.611, "E": 995.726, "Z": 149.372, "desc": "TH1"},
    {"point": 65, "N": 935.123, "E": 1069.787, "Z": 155.391, "desc": "TH4"},
]


def _assert_points_equal(got, expected):
    assert len(got) == len(expected), f"row count {len(got)} != {len(expected)}"
    for g, e in zip(got, expected):
        assert g["point"] == e["point"], f"point# {g['point']} != {e['point']}"
        assert approx(g["N"], e["N"]), f"pt{e['point']} N {g['N']} != {e['N']} (N/E swap?)"
        assert approx(g["E"], e["E"]), f"pt{e['point']} E {g['E']} != {e['E']} (N/E swap?)"
        assert approx(g["Z"], e["Z"]), f"pt{e['point']} Z {g['Z']} != {e['Z']}"
        assert g["desc"] == e["desc"], f"pt{e['point']} desc {g['desc']!r} != {e['desc']!r}"


# --- Vision / OCR extraction gate -----------------------------------------
# For scanned plans and field-note images, extraction is non-deterministic, so
# this harness does NOT do the OCR. Instead: lock ground truth here, have the
# agent/OCR step produce `extracted`, and gate it with validate_extraction().
# Each entry: name -> {"ground_truth": {...} or None, "tol": float}
# Set ground_truth to None until you have read the source and locked the values.
VISION_GROUND_TRUTH = {
    # EXAMPLE (filled): a coordinate listing that should OCR to these values.
    "lab4_survey1_pnezd": {
        "ground_truth": {p["point"]: [p["N"], p["E"], p["Z"]] for p in EXPECTED_POINTS},
        "tol": 1e-3,
    },
    # TEMPLATE (fill in): bearing/distance table off the scanned legal plan.
    # Read the plan once, lock the values, then this fixture starts gating.
    "lab7_legal_plan_bearings": {
        "ground_truth": None,  # e.g. {"L1": [az_deg, distance], "L2": [...], ...}
        "tol": 0.01,
    },
    # TEMPLATE (fill in): values read from a field-note image (FieldNotes.png).
    "lab4_field_notes": {
        "ground_truth": None,
        "tol": 1e-3,
    },
}


def validate_extraction(name, extracted, tol=None):
    """Compare an extraction (dict) against locked ground truth. Raises Skip if
    ground truth has not been filled in yet, AssertionError on mismatch."""
    entry = VISION_GROUND_TRUTH.get(name)
    assert entry is not None, f"no fixture registered for {name!r}"
    gt = entry["ground_truth"]
    if gt is None:
        raise Skip(f"ground truth for {name!r} not filled in yet")
    tol = tol if tol is not None else entry["tol"]
    for key, exp in gt.items():
        assert key in extracted or str(key) in extracted, f"missing key {key!r} in extraction"
        got = extracted.get(key, extracted.get(str(key)))
        if isinstance(exp, (list, tuple)):
            assert len(got) == len(exp), f"{key}: arity {len(got)} != {len(exp)}"
            for i, (a, b) in enumerate(zip(got, exp)):
                assert abs(float(a) - float(b)) <= tol, f"{key}[{i}] {a} != {b}"
        else:
            assert abs(float(got) - float(exp)) <= tol, f"{key} {got} != {exp}"


def section_b(runner):
    print("\n[B] INPUT-INTERPRETATION CHECKS  (no CAD required)")

    def t_pnezd():
        _assert_points_equal(parse_point_file(PNEZD_FIXTURE, "PNEZD"), EXPECTED_POINTS)

    def t_penzd_swap():
        # If the parser ignored the format and read positionally, N and E would
        # be swapped here and this fails - exactly the bug we want to catch.
        _assert_points_equal(parse_point_file(PENZD_FIXTURE, "PENZD"), EXPECTED_POINTS)

    def t_desc_with_space():
        rows = parse_point_file("62,971.943,1160.370,152.222,OCM 78H4456", "PNEZD")
        assert rows[0]["desc"] == "OCM 78H4456", rows[0]["desc"]

    def t_vision_example_passes():
        # Demonstrates the gate working on a fully-specified fixture: pretend the
        # OCR produced exactly the ground-truth points.
        extracted = {p["point"]: [p["N"], p["E"], p["Z"]] for p in EXPECTED_POINTS}
        validate_extraction("lab4_survey1_pnezd", extracted)

    def t_vision_example_catches_swap():
        # Same data but N/E swapped -> the gate must reject it.
        extracted = {p["point"]: [p["E"], p["N"], p["Z"]] for p in EXPECTED_POINTS}
        try:
            validate_extraction("lab4_survey1_pnezd", extracted)
        except AssertionError:
            return  # expected
        raise AssertionError("validate_extraction failed to catch an N/E swap")

    def t_vision_legal_plan():
        # Stays a SKIP until you lock the legal-plan bearing/distance ground truth.
        validate_extraction("lab7_legal_plan_bearings", {})

    def t_vision_field_notes():
        validate_extraction("lab4_field_notes", {})

    runner.run("parse_PNEZD", t_pnezd)
    runner.run("parse_PENZD_no_swap", t_penzd_swap)
    runner.run("desc_with_space", t_desc_with_space)
    runner.run("vision_gate_accepts_correct", t_vision_example_passes)
    runner.run("vision_gate_rejects_swap", t_vision_example_catches_swap)
    runner.run("vision_legal_plan_bearings", t_vision_legal_plan)
    runner.run("vision_field_notes", t_vision_field_notes)


# ===========================================================================

def main():
    ap = argparse.ArgumentParser(description="Civil 3D MCP regression harness")
    ap.add_argument("--bridge-url", default=DEFAULT_BRIDGE_URL)
    ap.add_argument("--no-bridge", action="store_true", help="skip live bridge checks")
    ap.add_argument("--save-test", action="store_true", help="also test SAVEAS round-trip")
    ap.add_argument("--out", default="regression_results.txt",
                    help="tee all output to this file (default: regression_results.txt "
                         "beside the script). Pass --out \"\" to disable.")
    args = ap.parse_args()

    out_file = None
    out_path = None
    if args.out:
        out_path = args.out
        if not os.path.isabs(out_path):
            out_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), out_path)
        out_file = open(out_path, "w", encoding="utf-8")
        sys.stdout = Tee(sys.__stdout__, out_file)

    try:
        runner = Runner()
        if not args.no_bridge:
            section_a(runner, args.bridge_url, args.save_test)
        else:
            print("\n[A] BRIDGE CHECKS  -- skipped (--no-bridge)")
        section_b(runner)
        code = runner.summary()
        if out_path:
            print(f"\n(full output written to {out_path})")
    finally:
        if out_file:
            sys.stdout = sys.__stdout__
            out_file.close()
    sys.exit(code)


if __name__ == "__main__":
    main()
