"""
Civil 3D / MicroSurvey CAD MCP Server
======================================
Thin MCP server (stdio transport) that proxies tool calls to the .NET plugin
running inside Civil 3D or MicroSurvey CAD via localhost HTTP.

SETUP:
    pip install fastmcp httpx

USAGE:
    # In Claude Desktop config (claude_desktop_config.json):
    {
        "mcpServers": {
            "civil3d": {
                "command": "python",
                "args": ["path/to/server.py"],
                "env": {
                    "CAD_BRIDGE_URL": "http://localhost:8270"
                }
            }
        }
    }

    # Or run standalone for testing:
    python server.py

ARCHITECTURE:
    Claude <--stdio/MCP--> this server <--HTTP--> .NET plugin inside CAD app
"""

import os
import sys
import json
import asyncio
import logging
from typing import Optional

import httpx
from fastmcp import FastMCP

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

BRIDGE_URL = os.environ.get("CAD_BRIDGE_URL", "http://localhost:8270")
APP_NAME = os.environ.get("CAD_APP_NAME", "Civil3D")  # or "MicroSurveyCAD"

# Logging to stderr (stdout is reserved for MCP protocol)
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    stream=sys.stderr,
)
logger = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# MCP Server
# ---------------------------------------------------------------------------

mcp = FastMCP(
    f"{APP_NAME} MCP Bridge",
    instructions=(
        f"Controls {APP_NAME} via a .NET plugin bridge. "
        "Provides tools for drawing geometry, managing layers, "
        "working with COGO points, creating/editing Civil 3D styles, "
        "and configuring drawing settings."
    ),
)

# Workflow tracing (survey-domain API grammar). Captures the MCP-tool layer
# (e.g. draw_line_by_bearing) — a different abstraction level from the C#
# bridge trace (which only sees primitive bridge actions). Each record is
# tagged layer="mcp_tool"; the bridge tags layer="bridge". Shares the
# CAD_MCP_TRACE/CAD_MCP_TRACE_DIR env knobs and default dir with the bridge
# so both corpora co-locate. Must run before the @mcp.tool() defs below.
import mcp_trace  # noqa: E402

mcp_trace.install_tracing(
    mcp,
    app_name="Civil3D-py",
    env_prefix="CAD_MCP",
    default_dir=os.path.abspath(
        os.path.join(os.path.dirname(__file__), "..", "plugin", "traces")
    ),
    layer="mcp_tool",
)


def _format_dialog_summary(dialogs: list) -> str:
    """One-line per dialog summary used in error messages and post-call notes."""
    parts = []
    for d in dialogs or []:
        title = (d.get("title") or "").strip() or "(untitled)"
        msg = (d.get("message") or "").replace("\n", " | ")[:160]
        btns = ", ".join(b.get("text", "") for b in d.get("buttons") or [])
        parts.append(f"[{title}] {msg} (buttons: {btns or 'none'})")
    return " ; ".join(parts) if parts else "(none)"


async def call_bridge(action: str, params: dict = None) -> dict:
    """Send a request to the .NET plugin bridge running inside the CAD app.

    Dialog handling:
    - If the bridge reports `blocked_by_dialog`, raise a Python Exception
      whose message names the open dialog(s) and their buttons, so the
      caller can act (call list_dialogs / dismiss_dialog and retry).
    - If the action succeeds but Civil 3D *now* has a dialog open
      (post-check), the dialogs are attached to the returned result
      under the `_dialogs` key so the next-step agent can see them.
    """
    payload = {"action": action, "params": params or {}}
    try:
        async with httpx.AsyncClient(timeout=30.0) as client:
            resp = await client.post(BRIDGE_URL, json=payload)
            resp.raise_for_status()
            result = resp.json()
            if not result.get("success"):
                err = result.get("error", "Unknown error from bridge")
                dialogs = result.get("dialogs")
                if err == "blocked_by_dialog" and dialogs:
                    raise Exception(
                        "blocked_by_dialog: A modal dialog is open in "
                        f"{APP_NAME}. Open dialogs: "
                        f"{_format_dialog_summary(dialogs)}. "
                        "Call list_dialogs to inspect, dismiss_dialog to "
                        "click a button, or cancel_command to send ESC."
                    )
                raise Exception(err)
            inner = result.get("result", {})
            # Attach post-check dialogs (if any) so the next caller sees them.
            post_dialogs = result.get("dialogs")
            if post_dialogs:
                if isinstance(inner, dict):
                    inner.setdefault("_dialogs", post_dialogs)
                else:
                    # Wrap non-dict results so we can still expose dialogs.
                    inner = {"value": inner, "_dialogs": post_dialogs}
            return inner
    except httpx.ConnectError:
        raise Exception(
            f"Cannot connect to {APP_NAME} bridge at {BRIDGE_URL}. "
            f"Is {APP_NAME} running with the MCP plugin loaded? "
            f"Run MCPSTART in the {APP_NAME} command line."
        )


# ===========================================================================
# TOOL: Connection / Status
# ===========================================================================


@mcp.tool()
async def ping() -> str:
    """Check if the CAD application is running and the bridge is active."""
    result = await call_bridge("ping")
    return json.dumps(result, indent=2)


@mcp.tool()
async def get_drawing_info() -> str:
    """Get information about the currently open drawing (filename, path, units)."""
    result = await call_bridge("get_drawing_info")
    return json.dumps(result, indent=2)


# ===========================================================================
# TOOL: Drawing Settings (Civil 3D specific)
# ===========================================================================


@mcp.tool()
async def get_drawing_settings() -> str:
    """Get Civil 3D drawing settings including units, scale, zone, and coordinate system."""
    result = await call_bridge("get_drawing_settings")
    return json.dumps(result, indent=2)


@mcp.tool()
async def set_drawing_settings(
    scale: Optional[float] = None,
    coordinate_precision: Optional[int] = None,
    direction_type: Optional[str] = None,
    tangent_curve_intersect: Optional[str] = None,
    curve_tangent_intersect: Optional[str] = None,
) -> str:
    """Configure Civil 3D drawing settings.

    Args:
        scale: Drawing scale (e.g., 500 for 1:500)
        coordinate_precision: Number of decimal places for coordinates (e.g., 3)
        direction_type: "NorthAzimuth" or "Bearing"
        tangent_curve_intersect: Abbreviation text (e.g., "BC" instead of "PC")
        curve_tangent_intersect: Abbreviation text (e.g., "EC" instead of "PT")
    """
    params = {}
    if scale is not None:
        params["scale"] = scale
    if coordinate_precision is not None:
        params["coordinatePrecision"] = coordinate_precision
    if direction_type is not None:
        params["directionType"] = direction_type
    if tangent_curve_intersect is not None:
        params["tangentCurveIntersect"] = tangent_curve_intersect
    if curve_tangent_intersect is not None:
        params["curveTangentIntersect"] = curve_tangent_intersect

    result = await call_bridge("set_drawing_settings", params)
    return json.dumps(result, indent=2)


# ===========================================================================
# TOOL: Layer Management
# ===========================================================================


@mcp.tool()
async def list_layers() -> str:
    """List all layers in the current drawing with their properties."""
    result = await call_bridge("list_layers")
    return json.dumps(result, indent=2)


@mcp.tool()
async def create_layer(name: str, color: int = 7) -> str:
    """Create a new layer in the drawing.

    Args:
        name: Layer name (e.g., "C-PROP-LINE", "A-BLDG-FPRT")
        color: AutoCAD Color Index (1=red, 2=yellow, 3=green, 4=cyan,
               5=blue, 6=magenta, 7=white)
    """
    result = await call_bridge("create_layer", {"name": name, "color": color})
    return json.dumps(result, indent=2)


@mcp.tool()
async def set_current_layer(name: str) -> str:
    """Set the active/current layer for new entities.

    Args:
        name: Name of existing layer to make current
    """
    result = await call_bridge("set_current_layer", {"name": name})
    return json.dumps(result, indent=2)


@mcp.tool()
async def list_parcels(site_name: Optional[str] = None) -> str:
    """List Civil 3D parcels grouped by Site.

    Each parcel reports its number, name, Civil 3D's own computed area (m^2),
    and centroid. The area is an independent cross-check on reconstructed lot
    geometry (it matches a correct reconstruction to the micron).

    Args:
        site_name: Optional Site name filter. Omit to list all sites.
    """
    params = {}
    if site_name is not None:
        params["site_name"] = site_name
    result = await call_bridge("list_parcels", params)
    return json.dumps(result, indent=2)


@mcp.tool()
async def get_parcel_layout_settings() -> str:
    """Read the Parcel Layout sizing rules from the drawing Settings tree.

    Returns minimum_area_sq_m, minimum_frontage_m, minimum_width_m,
    minimum_depth_m, maximum_depth_m, frontage_offset_m, solution_preference,
    remainder_distribution, and automatic_mode — the design parameters behind a
    subdivision's lot dimensions.
    """
    result = await call_bridge("get_parcel_layout_settings")
    return json.dumps(result, indent=2)


@mcp.tool()
async def create_parcels_from_objects(layer: Optional[str] = None) -> str:
    """Create Civil 3D Parcels from the closed polylines in model space.

    Drives CREATEPARCELFROMOBJECTS headlessly: fires the command with an ALL
    selection through the bridge's scripted command channel (one string so
    SendStringToExecute plays it through the prompts), dismisses the resulting
    "Create Parcels - From objects" modal with OK (accepting the current default
    site/style/labels), then reports the new parcels (with areas) via
    list_parcels. Best run in a drawing whose model space contains only the
    boundary polylines you want converted.

    Args:
        layer: Optional layer to make current before creation.
    """
    if layer:
        await call_bridge("set_current_layer", {"name": layer})
    await call_bridge("send_command",
                      {"command": "CREATEPARCELFROMOBJECTS\nALL\n\n"})
    dismissed = False
    for _ in range(10):
        await asyncio.sleep(0.5)
        dlgs = await call_bridge("list_dialogs")
        for d in (dlgs.get("dialogs") or []):
            if "Create Parcels" in (d.get("title") or ""):
                await call_bridge("dismiss_dialog",
                                  {"button": "[OK]", "hwnd": d.get("hwnd")})
                dismissed = True
                break
        if dismissed:
            break
    await asyncio.sleep(0.5)
    parcels = await call_bridge("list_parcels")
    return json.dumps({"dialog_dismissed": dismissed, "parcels": parcels},
                      indent=2)


# ===========================================================================
# TOOL: Geometry Drawing
# ===========================================================================


@mcp.tool()
async def draw_line(
    start_x: float,
    start_y: float,
    end_x: float,
    end_y: float,
    start_z: float = 0.0,
    end_z: float = 0.0,
) -> str:
    """Draw a line between two points.

    Coordinates use AutoCAD convention: X=Easting, Y=Northing, Z=Elevation.

    Args:
        start_x: Start point X (Easting)
        start_y: Start point Y (Northing)
        end_x: End point X (Easting)
        end_y: End point Y (Northing)
        start_z: Start point Z (Elevation), default 0
        end_z: End point Z (Elevation), default 0
    """
    result = await call_bridge(
        "draw_line",
        {
            "start": [start_x, start_y, start_z],
            "end": [end_x, end_y, end_z],
        },
    )
    return json.dumps(result, indent=2)


@mcp.tool()
async def draw_line_by_bearing(
    start_easting: float,
    start_northing: float,
    quadrant: int,
    degrees: float,
    minutes: float,
    seconds: float,
    distance: float,
    elevation: float = 0.0,
) -> str:
    """Draw a line from a start point using surveyor bearing and distance.

    This is the programmatic equivalent of Civil 3D's Bearing Distance
    transparent command ('BD).

    Args:
        start_easting: Start point Easting (X)
        start_northing: Start point Northing (Y)
        quadrant: Bearing quadrant (1=NE, 2=SE, 3=SW, 4=NW)
        degrees: Bearing degrees
        minutes: Bearing minutes
        seconds: Bearing seconds
        distance: Line distance in drawing units
        elevation: Z elevation for both endpoints, default 0
    """
    import math

    # Convert bearing to AutoCAD angle (from East, CCW)
    bearing_decimal = degrees + minutes / 60.0 + seconds / 3600.0
    if quadrant == 1:
        acad_angle = 90.0 - bearing_decimal
    elif quadrant == 2:
        acad_angle = 270.0 + bearing_decimal
    elif quadrant == 3:
        acad_angle = 270.0 - bearing_decimal
    elif quadrant == 4:
        acad_angle = 90.0 + bearing_decimal
    else:
        raise ValueError(f"Invalid quadrant: {quadrant}")

    angle_rad = math.radians(acad_angle)
    end_x = start_easting + distance * math.cos(angle_rad)
    end_y = start_northing + distance * math.sin(angle_rad)

    result = await call_bridge(
        "draw_line",
        {
            "start": [start_easting, start_northing, elevation],
            "end": [end_x, end_y, elevation],
        },
    )
    # Include computed endpoint in response
    result["endpoint"] = {"easting": end_x, "northing": end_y}
    result["bearing"] = f"{'NESW'[quadrant-1]}{degrees}d{minutes}'{seconds}\""
    return json.dumps(result, indent=2)


@mcp.tool()
async def draw_line_by_azimuth(
    start_easting: float,
    start_northing: float,
    azimuth_degrees: float,
    azimuth_minutes: float,
    azimuth_seconds: float,
    distance: float,
    elevation: float = 0.0,
) -> str:
    """Draw a line from a start point using azimuth and distance.

    Azimuth is measured clockwise from North (0/360 = North, 90 = East, etc.).
    This is the programmatic equivalent of Civil 3D's Azimuth Distance
    transparent command ('AD).

    Args:
        start_easting: Start point Easting (X)
        start_northing: Start point Northing (Y)
        azimuth_degrees: Azimuth degrees (0-360, from North clockwise)
        azimuth_minutes: Azimuth minutes
        azimuth_seconds: Azimuth seconds
        distance: Line distance in drawing units
        elevation: Z elevation, default 0
    """
    import math

    az_decimal = azimuth_degrees + azimuth_minutes / 60.0 + azimuth_seconds / 3600.0
    # Convert azimuth (from North CW) to AutoCAD angle (from East CCW)
    acad_angle = 90.0 - az_decimal
    if acad_angle < 0:
        acad_angle += 360.0

    angle_rad = math.radians(acad_angle)
    end_x = start_easting + distance * math.cos(angle_rad)
    end_y = start_northing + distance * math.sin(angle_rad)

    result = await call_bridge(
        "draw_line",
        {
            "start": [start_easting, start_northing, elevation],
            "end": [end_x, end_y, elevation],
        },
    )
    result["endpoint"] = {"easting": end_x, "northing": end_y}
    result["azimuth"] = f"{azimuth_degrees}d{azimuth_minutes}'{azimuth_seconds}\""
    return json.dumps(result, indent=2)


@mcp.tool()
async def draw_arc(
    center_x: float,
    center_y: float,
    radius: float,
    start_angle: float,
    end_angle: float,
    center_z: float = 0.0,
) -> str:
    """Draw an arc by center point, radius, and angles.

    Args:
        center_x: Arc center X (Easting)
        center_y: Arc center Y (Northing)
        radius: Arc radius
        start_angle: Start angle in degrees (AutoCAD convention, from East CCW)
        end_angle: End angle in degrees
        center_z: Center Z elevation, default 0
    """
    import math

    result = await call_bridge(
        "draw_arc",
        {
            "center": [center_x, center_y, center_z],
            "radius": radius,
            "startAngle": math.radians(start_angle),
            "endAngle": math.radians(end_angle),
        },
    )
    return json.dumps(result, indent=2)


@mcp.tool()
async def draw_polyline(
    points: list[list[float]], closed: bool = False
) -> str:
    """Draw a polyline through a list of points.

    Args:
        points: List of [x, y] or [x, y, z] coordinate arrays
        closed: Whether to close the polyline (connect last to first point)
    """
    result = await call_bridge(
        "draw_polyline", {"points": points, "closed": closed}
    )
    return json.dumps(result, indent=2)


def _plan_ground(x, y, theta_deg, scale):
    """plat2json page pt (y-down) -> ground (E, N): flip north-up, rotate the
    plan's fitted sheet rotation (azimuth sense, cw positive), scale."""
    import math

    e, n = x, -y
    t = math.radians(theta_deg)
    return (scale * (e * math.cos(t) + n * math.sin(t)),
            scale * (-e * math.sin(t) + n * math.cos(t)))


@mcp.tool()
async def import_plan_json(
    path: str,
    seed_easting: float = 5000.0,
    seed_northing: float = 5000.0,
    ref_layer: str = "PLAN_REF",
    cogo_layer: str = "PLAN_COGO",
    allow_flagged: bool = False,
    draw_reference: bool = True,
) -> str:
    """Import a plat2json plan JSON (cogo_assemble.py "plan2/assoc-v1" output,
    or a legacy {"lines": [...]} geometry plan) and draw it on named layers.

    Reference linework (every extracted segment, fitted page->ground
    transform) goes on ref_layer. Every ring whose traverse-closure passed is
    re-drawn as a CLOSED POLYLINE on cogo_layer, COGO-exact from the published
    bearing/distance values — ready for create_parcels_from_objects. A plan
    whose report says needs_human is refused unless allow_flagged=True, so an
    unvalidated sheet never gets drawn silently.

    Args:
        path: Plan JSON file path.
        seed_easting: Easting for the extent's lower-left corner.
        seed_northing: Northing for the extent's lower-left corner.
        ref_layer: Layer for reference (approximate, extracted) linework.
        cogo_layer: Layer for closure-validated COGO ring polylines.
        allow_flagged: Draw even when the plan is flagged needs_human.
        draw_reference: Also draw the reference linework (default True).
    """
    import math

    if not os.path.exists(path):
        return json.dumps({"status": "error", "error": f"no such file: {path}"})
    with open(path, encoding="utf-8") as fh:
        plan = json.load(fh)

    # legacy geometry-only plan: coordinates are already ground, draw as-is
    if "courses" not in plan and "lines" in plan:
        await call_bridge("create_layer", {"name": ref_layer, "color": 8})
        await call_bridge("set_current_layer", {"name": ref_layer})
        for L in plan["lines"]:
            await call_bridge("draw_line",
                              {"start": [L[0], L[1], 0.0],
                               "end": [L[2], L[3], 0.0]})
        return json.dumps({"status": "ok", "schema": "legacy",
                           "reference_lines": len(plan["lines"]),
                           "layers": [ref_layer]})

    report = plan.get("report", {})
    if report.get("needs_human") and not allow_flagged:
        return json.dumps({
            "status": "refused",
            "reason": "plan report says needs_human (closure or rotation "
                      "fit failed upstream)",
            "report": report,
            "hint": "pass allow_flagged=True to draw anyway",
        }, indent=2)

    theta = plan.get("rotation_deg", 0.0)
    scale = plan.get("scale_unit_per_pt")
    if not scale:
        return json.dumps({"status": "error",
                           "error": "plan has no fitted scale"})

    glines = [(_plan_ground(L[0], L[1], theta, scale) +
               _plan_ground(L[2], L[3], theta, scale))
              for L in plan.get("lines", [])]
    if not glines:
        return json.dumps({"status": "error", "error": "plan has no linework"})
    de = seed_easting - min(min(g[0], g[2]) for g in glines)
    dn = seed_northing - min(min(g[1], g[3]) for g in glines)

    drawn_ref = 0
    if draw_reference:
        await call_bridge("create_layer", {"name": ref_layer, "color": 8})
        await call_bridge("set_current_layer", {"name": ref_layer})
        for g in glines:
            await call_bridge("draw_line",
                              {"start": [g[0] + de, g[1] + dn, 0.0],
                               "end": [g[2] + de, g[3] + dn, 0.0]})
            drawn_ref += 1

    segs = {i: tuple(s) for i, s in enumerate(plan.get("lines", []))}
    by_seg = {}
    for c in plan.get("courses", []):
        if "azimuth" in c and "distance" in c and not c.get("flags"):
            by_seg.setdefault(c["seg"], []).append(c)
    ring_results, drawn_rings = [], 0
    for ring in plan.get("rings", []):
        mis_ratio = (ring["misclosure"] / ring["perimeter"]
                     if ring["perimeter"] else 1.0)
        ok = mis_ratio <= 1e-4 or ring["misclosure"] <= 0.05
        ring_results.append({**ring, "cogo_drawn": ok})
        if not ok:
            continue
        first = ring["segs"][0]
        e, n = _plan_ground(segs[first][0], segs[first][1], theta, scale)
        e, n = e + de, n + dn
        pts = [[round(e, 4), round(n, 4)]]
        for s in ring["segs"][:-1]:      # closed polyline supplies the last leg
            c = by_seg[s][0]
            az = c["azimuth"] if c.get("sense", 1) > 0 \
                else (c["azimuth"] + 180) % 360
            az_r = math.radians(az)
            e += c["distance"] * math.sin(az_r)
            n += c["distance"] * math.cos(az_r)
            pts.append([round(e, 4), round(n, 4)])
        if drawn_rings == 0:
            await call_bridge("create_layer", {"name": cogo_layer, "color": 3})
        await call_bridge("set_current_layer", {"name": cogo_layer})
        await call_bridge("draw_polyline", {"points": pts, "closed": True})
        drawn_rings += 1

    return json.dumps({
        "status": "ok",
        "schema": plan.get("schema"),
        "reference_lines": drawn_ref,
        "cogo_rings": drawn_rings,
        "rings": ring_results,
        "transform": {"rotation_deg": theta, "scale": scale,
                      "translate": [round(de, 3), round(dn, 3)]},
        "layers": [ref_layer] + ([cogo_layer] if drawn_rings else []),
        "next": "create_parcels_from_objects on the cogo layer turns the "
                "closed ring polylines into Civil 3D parcels",
    }, indent=2)


# ===========================================================================
# TOOL: COGO Points
# ===========================================================================


@mcp.tool()
async def list_cogo_points() -> str:
    """List all COGO (coordinate geometry) points in the drawing.

    Returns point number, northing, easting, elevation, and descriptions.
    """
    result = await call_bridge("list_cogo_points")
    return json.dumps(result, indent=2)


@mcp.tool()
async def get_cogo_point(point_number: int) -> str:
    """Get details of a specific COGO point by its point number.

    Args:
        point_number: The Civil 3D point number (e.g., 273)
    """
    result = await call_bridge("get_cogo_point", {"number": point_number})
    return json.dumps(result, indent=2)


@mcp.tool()
async def edit_cogo_point(
    point_number: int,
    raw_description: Optional[str] = None,
    elevation: Optional[float] = None,
    northing: Optional[float] = None,
    easting: Optional[float] = None,
) -> str:
    """Edit properties of an existing COGO point.

    Args:
        point_number: The point number to edit
        raw_description: New raw description (e.g., "PROPERTY CORNER")
        elevation: New elevation value
        northing: New northing value
        easting: New easting value
    """
    params = {"number": point_number}
    if raw_description is not None:
        params["rawDescription"] = raw_description
    if elevation is not None:
        params["elevation"] = elevation
    if northing is not None:
        params["northing"] = northing
    if easting is not None:
        params["easting"] = easting

    result = await call_bridge("edit_cogo_point", params)
    return json.dumps(result, indent=2)


@mcp.tool()
async def create_cogo_point(
    northing: float,
    easting: float,
    elevation: float = 0.0,
    raw_description: str = "",
) -> str:
    """Create a new COGO point in the drawing.

    Args:
        northing: Point Northing coordinate
        easting: Point Easting coordinate
        elevation: Point elevation, default 0
        raw_description: Point description text
    """
    result = await call_bridge(
        "create_cogo_point",
        {
            "northing": northing,
            "easting": easting,
            "elevation": elevation,
            "rawDescription": raw_description,
        },
    )
    return json.dumps(result, indent=2)


# ===========================================================================
# TOOL: Point Styles (Civil 3D)
# ===========================================================================


@mcp.tool()
async def list_point_styles() -> str:
    """List all point object styles in the drawing."""
    result = await call_bridge("list_point_styles")
    return json.dumps(result, indent=2)


@mcp.tool()
async def create_point_style(
    name: str,
    description: str = "",
    marker_type: str = "plus",
    marker_size: float = 2.0,
    use_drawing_scale: bool = True,
    rotation: Optional[float] = None,
    block_name: Optional[str] = None,
) -> str:
    """Create a new point object style in Civil 3D.

    Controls how COGO points are displayed in the drawing.

    Args:
        name: Style name (e.g., "Calculated Point", "Wall Marker")
        description: Style description
        marker_type: Marker symbol: "plus", "x", "circle", "dot"
        marker_size: Marker size in mm
        use_drawing_scale: If true, size scales with drawing; if false, fixed size
        rotation: Marker rotation angle in degrees (e.g., 45)
        block_name: AutoCAD block name for block-based markers
                   (e.g., "AeccTickTriangle"). Overrides marker_type.
    """
    params = {
        "name": name,
        "description": description,
        "markerType": marker_type,
        "markerSize": marker_size,
        "useDrawingScale": use_drawing_scale,
    }
    if rotation is not None:
        params["rotation"] = rotation
    if block_name is not None:
        params["blockName"] = block_name

    result = await call_bridge("create_point_style", params)
    return json.dumps(result, indent=2)


# ===========================================================================
# TOOL: Point Label Styles
# ===========================================================================


@mcp.tool()
async def list_point_label_styles() -> str:
    """List all point label styles in the drawing."""
    result = await call_bridge("list_point_label_styles")
    return json.dumps(result, indent=2)


@mcp.tool()
async def create_point_label_style(
    name: str,
    description: str = "",
    components: Optional[list[dict]] = None,
) -> str:
    """Create a new point label style in Civil 3D.

    Controls how point labels (text annotations) appear.

    Args:
        name: Style name (e.g., "Raw Description Only", "Topog Elevation (m)")
        description: Style description
        components: List of label components, each a dict with:
            - name: Component name
            - type: "text" (default)
            - anchorPoint: "TopLeft", "MiddleRight", "BottomCenter", etc.
            - textHeight: Height in mm (default 2.0)
            - xOffset: X offset in mm
            - yOffset: Y offset in mm
            - contents: Property to display (e.g., "RawDescription", "PointElevation")
            - attachment: Text attachment point
    """
    params = {"name": name, "description": description}
    if components:
        params["components"] = components

    result = await call_bridge("create_point_label_style", params)
    return json.dumps(result, indent=2)


# ===========================================================================
# TOOL: Line Label Styles
# ===========================================================================


@mcp.tool()
async def list_line_label_styles() -> str:
    """List all general line label styles in the drawing."""
    result = await call_bridge("list_line_label_styles")
    return json.dumps(result, indent=2)


@mcp.tool()
async def create_line_label_style(name: str) -> str:
    """Create a new line label style in Civil 3D.

    Used for labeling lines with bearing/azimuth and distance.

    Args:
        name: Style name (e.g., "Azimuth over Distance", "Distance Only (m)")
    """
    result = await call_bridge("create_line_label_style", {"name": name})
    return json.dumps(result, indent=2)


# ===========================================================================
# TOOL: Entity Operations
# ===========================================================================


@mcp.tool()
async def offset_entity(
    entity_handle: str, distance: float, side: str = "right"
) -> str:
    """Create an offset copy of a line, arc, or polyline.

    Args:
        entity_handle: Hex handle of the entity to offset
        distance: Offset distance in drawing units
        side: "right" or "left" relative to entity direction
    """
    result = await call_bridge(
        "offset_entity",
        {"entityHandle": entity_handle, "distance": distance, "side": side},
    )
    return json.dumps(result, indent=2)


# ===========================================================================
# TOOL: Raw Command (escape hatch)
# ===========================================================================


@mcp.tool()
async def send_command(command: str) -> str:
    """Send a raw command string to the CAD command line.

    Use this as a fallback for operations not covered by specific tools.
    The command is sent as if typed at the command prompt.

    Args:
        command: AutoCAD/Civil 3D command string (e.g., "ZOOM E", "REGEN")
    """
    result = await call_bridge("send_command", {"command": command})
    return json.dumps(result, indent=2)


# ===========================================================================
# TOOL: Set text style font
# ===========================================================================

@mcp.tool()
async def set_text_style_font(style_name: str, font: str) -> str:
    """Set the font for an AutoCAD text style.

    Args:
        style_name: Name of text style (e.g., "Standard")
        font: Font filename (e.g., "simplex.shx", "arial.ttf")
    """
    lisp = f'(vl-load-com)(vla-put-fontfile (vla-item (vla-get-textstyles (vla-get-activedocument (vlax-get-acad-object))) "{style_name}") "{font}")(princ)'
    result = await call_bridge("send_command", {"command": lisp})
    return json.dumps({"updated": True, "styleName": style_name, "font": font}, indent=2)


# ===========================================================================
# TOOL: Get entity handles by layer
# ===========================================================================

@mcp.tool()
async def get_entity_handles(layer_name: str) -> str:
    """Get hex handles and types of all entities on a given layer.

    Uses a LISP script + temp file to retrieve entity data from the drawing.

    Args:
        layer_name: Layer name to query (e.g., "C-PROP-LINE")
    """
    import tempfile, os, asyncio

    out_path = os.path.join(tempfile.gettempdir(), "c3dmcp_entity_handles.txt")
    out_path_escaped = out_path.replace("\\", "\\\\")

    lisp = (
        f'(vl-load-com)'
        f'(setq _fh (open "{out_path_escaped}" "w"))'
        f'(vlax-for _obj (vla-get-modelspace (vla-get-activedocument (vlax-get-acad-object)))'
        f'  (if (= (vla-get-layer _obj) "{layer_name}")'
        f'    (write-line (strcat (vla-get-objectname _obj) "|" (vla-get-handle _obj)) _fh)'
        f'  )'
        f')'
        f'(close _fh)(princ)'
    )
    await call_bridge("send_command", {"command": lisp})
    # Give Civil 3D time to execute
    await asyncio.sleep(2)

    # Read the output file via a separate bridge call
    read_lisp = f'(if (findfile "{out_path_escaped}") (princ "found") (princ "missing"))'
    await call_bridge("send_command", {"command": read_lisp})
    await asyncio.sleep(1)

    # Try reading the file
    try:
        with open(out_path, "r") as f:
            lines = f.readlines()
        entities = []
        for line in lines:
            line = line.strip()
            if "|" in line:
                parts = line.split("|")
                entities.append({"type": parts[0], "handle": parts[1]})
        return json.dumps({"layer": layer_name, "count": len(entities), "entities": entities}, indent=2)
    except FileNotFoundError:
        return json.dumps({"error": f"Output file not found. LISP may not have executed yet.", "path": out_path}, indent=2)


# ===========================================================================
# TOOL: Add line labels to all entities on a layer
# ===========================================================================

@mcp.tool()
async def add_labels_to_layer(layer_name: str, line_style_name: str, ratio: float = 0.5) -> str:
    """Add line/arc labels to all entities on a given layer.

    First calls get_entity_handles to find entities, then labels each one.

    Args:
        layer_name: Layer to label (e.g., "C-PROP-LINE")
        line_style_name: Label style name (e.g., "Azimuth over Distance")
        ratio: Position along entity for label placement (0.0-1.0, default 0.5)
    """
    import json as _json
    import tempfile, os

    _hp = os.path.join(tempfile.gettempdir(), "c3dmcp_entity_handles.txt")
    _hp_lisp = _hp.replace("\\", "\\\\")
    handles_result = await call_bridge("send_command", {
        "command": f'(vl-load-com)(setq _fh (open "{_hp_lisp}" "w"))(vlax-for _obj (vla-get-modelspace (vla-get-activedocument (vlax-get-acad-object)))(if (= (vla-get-layer _obj) "{layer_name}")(write-line (strcat (vla-get-objectname _obj) "|" (vla-get-handle _obj)) _fh)))(close _fh)(princ)'
    })

    import asyncio
    await asyncio.sleep(3)

    try:
        with open(_hp, "r") as f:
            lines = f.readlines()
    except FileNotFoundError:
        return json.dumps({"error": "Handle file not created — LISP may not have run"}, indent=2)

    results = []
    for line in lines:
        line = line.strip()
        if "|" in line:
            etype, handle = line.split("|", 1)
            try:
                result = await call_bridge("add_line_label", {
                    "entityHandle": handle,
                    "styleName": line_style_name,
                    "ratio": ratio
                })
                results.append({"handle": handle, "type": etype, "labeled": True, "labelId": result.get("labelId")})
            except Exception as e:
                results.append({"handle": handle, "type": etype, "labeled": False, "error": str(e)})

    return json.dumps({"layer": layer_name, "style": line_style_name, "labeled": len([r for r in results if r.get("labeled")]), "results": results}, indent=2)


@mcp.tool()
async def import_points_file(path: str, format: str = "PNEZD") -> str:
    """Bulk-import COGO points from an ASCII file (PNEZD / PENZD / etc.).

    The fast equivalent of many create_cogo_point calls. Auto-detects comma vs.
    whitespace delimiter. `format` is the column order; D (description) is the
    trailing text field and may contain spaces. Preserves each point's number
    (P column) when that number is free, otherwise auto-numbers.

    Args:
        path: Full path to the points file ON THE MACHINE RUNNING CIVIL 3D.
        format: Column order, e.g. "PNEZD" (default) or "PENZD" (N/E swapped).
    """
    result = await call_bridge("import_points_file", {"path": path, "format": format})
    return json.dumps(result, indent=2)


@mcp.tool()
async def create_surface_from_points(
    name: str,
    point_group: str = "",
    style: str = "",
    layer: str = "",
) -> str:
    """Build a TIN surface from COGO points already in the drawing.

    Civil 3D builds the surface from a point GROUP, not loose points. If
    point_group is omitted, the surface uses the drawing's "_All Points"
    group (every COGO point). Import or create your points first
    (import_points_file / create_cogo_point), then call this.

    Args:
        name: Unique surface name (fails if one with this name exists).
        point_group: Point-group name to build from. Empty = "_All Points".
        style: Surface style name (optional; empty = drawing default).
        layer: Layer to place the surface on (optional; must already exist).
    """
    params = {"name": name}
    if point_group:
        params["point_group"] = point_group
    if style:
        params["style"] = style
    if layer:
        params["layer"] = layer
    result = await call_bridge("create_surface_from_points", params)
    return json.dumps(result, indent=2)


@mcp.tool()
async def surface_volume(
    comparison_surface: str,
    elevation: Optional[float] = None,
    base_surface: str = "",
    pad: float = 50.0,
) -> str:
    """Authoritative Civil 3D TIN volume (cut/fill/net), in two modes.

    - Surface-vs-flat-datum (give `elevation`): builds a flat base surface at
      that elevation spanning the comparison extent (+pad).
    - Surface-vs-surface (give `base_surface`): uses an existing surface as the
      base (e.g. existing ground vs a design surface).

    Creates a Civil 3D TIN volume surface and returns its cut/fill/net
    (Unadjusted and Adjusted). Civil 3D's sign convention is
    volume = comparison - base, so FillVolume is material where the comparison
    is ABOVE the base and CutVolume where it is BELOW.

    Use this to bank a ground-truth earthwork volume for cross-checking a clone
    engine — the values are computed by Civil 3D itself.

    Args:
        comparison_surface: Name of an existing TIN surface (e.g. imported via
            LANDXMLIN or built with create_surface_from_points).
        elevation: Datum plane elevation (surface-vs-flat-datum mode).
        base_surface: Existing base surface name (surface-vs-surface mode).
            Exactly one of elevation / base_surface should be given.
        pad: Flat-base overhang beyond the surface extent (datum mode only).
    """
    if elevation is None and not base_surface:
        raise ValueError("either base_surface or elevation is required")
    params = {"comparison_surface": comparison_surface, "pad": pad}
    if base_surface:
        params["base_surface"] = base_surface
    if elevation is not None:
        params["elevation"] = elevation
    result = await call_bridge("surface_volume", params)
    return json.dumps(result, indent=2)


@mcp.tool()
async def generate_contours(
    surface: str,
    minor_interval: float,
    major_interval: float,
    style: str = "",
) -> str:
    """Turn on contour display for an existing surface at the given intervals.

    In Civil 3D, contours are a property of the surface STYLE, not separate
    geometry: this sets the minor/major contour intervals on the surface's
    style and makes both contour layers visible in plan view. If the managed
    API can't set the interval values on this build, contour visibility is
    still enabled and the result includes a note to set the intervals
    manually in the Surface Style > Contours tab.

    Args:
        surface: Name of an existing surface (see create_surface_from_points).
        minor_interval: Minor contour interval (drawing units, > 0).
        major_interval: Major contour interval (>= minor_interval).
        style: Surface style name to apply first (optional; empty = current).
    """
    params = {
        "surface": surface,
        "minor_interval": minor_interval,
        "major_interval": major_interval,
    }
    if style:
        params["style"] = style
    result = await call_bridge("generate_contours", params)
    return json.dumps(result, indent=2)


@mcp.tool()
async def attach_raster_image(
    path: str,
    insertion_x: float,
    insertion_y: float,
    width: float = 100.0,
    rotation: float = 0.0,
    insertion_z: float = 0.0,
) -> str:
    """Attach a raster image (PNG/JPG/TIF) into the current drawing.

    Headless equivalent of IMAGEATTACH: creates the RasterImageDef + RasterImage
    via the .NET API, with no command-line prompts or modal dialogs. Use for
    tasks like Lab 9 - placing monument-location GIFs near their plan locations.

    Args:
        path: Full path to the image file ON THE MACHINE RUNNING CIVIL 3D.
        insertion_x: Lower-left corner X (Easting) in drawing units.
        insertion_y: Lower-left corner Y (Northing) in drawing units.
        width: Placed image width in drawing units; height keeps aspect ratio.
        rotation: Rotation angle in degrees (counter-clockwise).
        insertion_z: Lower-left corner Z (elevation), default 0.
    """
    result = await call_bridge("attach_raster_image", {
        "path": path,
        "insertion": [insertion_x, insertion_y, insertion_z],
        "width": width,
        "rotation": rotation,
    })
    return json.dumps(result, indent=2)


@mcp.tool()
async def zoom_to_point(point_number: int, zoom_factor: float = 20.0) -> str:
    """Zoom the Civil 3D viewport to a specific COGO point.

    Equivalent to right-click > Zoom To in Prospector.

    Args:
        point_number: The COGO point number to zoom to
        zoom_factor: View height/width in drawing units (smaller = more zoomed in)
    """
    result = await call_bridge("zoom_to_point", {
        "pointNumber": point_number,
        "zoomFactor": zoom_factor,
    })
    return json.dumps(result, indent=2)


# ===========================================================================
# TOOL: Survey (COM Interop — AeccXSurveyLib v13.8)
# ===========================================================================
#
# The Civil 3D Survey API has no managed .NET surface — it is exposed only
# via COM. The bridge reaches it through Autodesk.AECC.Interop.Survey.dll
# and the ProgID "AeccXSurvey.AeccSurveyDatabase.13.x". These tools cover
# the Lab 08 "Survey Toolspace" workflow starting from the working folder.


@mcp.tool()
async def get_survey_working_folder() -> str:
    """Get the current Civil 3D Survey working folder.

    The working folder is where all Survey Databases (SDB files) are stored.
    Lab 08 step: Toolspace > Survey tab > Survey Databases > right-click >
    "Set working folder".
    """
    result = await call_bridge("get_survey_working_folder")
    return json.dumps(result, indent=2)


@mcp.tool()
async def set_survey_working_folder(path: str) -> str:
    """Set the Civil 3D Survey working folder (creates it if missing).

    All Survey Databases created afterwards will live inside this folder.

    Args:
        path: Absolute path to the desired working folder.
              e.g. "C:/Users/You/Documents/Civil3D/SurveyDB"
    """
    result = await call_bridge("set_survey_working_folder", {"path": path})
    return json.dumps(result, indent=2)


@mcp.tool()
async def create_survey_project(name: str, description: str = "") -> str:
    """Create a new Survey Database (called AeccSurveyProject in the API).

    Equivalent to right-clicking "Survey Databases" in the Toolspace > Survey
    tab and choosing "New local survey database...". Creates an SDB folder
    under the current working folder and opens it for read/write. If a project
    with the same name already exists, returns its metadata instead of erroring.

    Args:
        name: Name of the survey database (e.g. "Lab08-TopoSurvey")
        description: Optional description to store in project settings
    """
    params = {"name": name}
    if description:
        params["description"] = description
    result = await call_bridge("create_survey_project", params)
    return json.dumps(result, indent=2)


@mcp.tool()
async def list_survey_projects() -> str:
    """List all Survey Databases found in the current working folder.

    Returns working folder path, project count, and for each project:
    name, on-disk path, and project GUID.
    """
    result = await call_bridge("list_survey_projects")
    return json.dumps(result, indent=2)


@mcp.tool()
async def create_survey_network(
    projectName: str,
    networkName: str,
    description: str = "",
) -> str:
    """Create (or re-open) a Survey Network inside the given Survey Database.

    A Survey Network is the container for setups, observations, and
    control points — it's what IMPORTFIELDBOOK writes into. Networks are
    stored on disk under the survey project folder.

    Args:
        projectName: Existing survey database name (must have been created
                     via create_survey_project).
        networkName: Name for the new network (e.g. "TopoSurvey_Network").
        description: Optional description stored on the network.
    """
    params = {"projectName": projectName, "networkName": networkName}
    if description:
        params["description"] = description
    result = await call_bridge("create_survey_network", params)
    return json.dumps(result, indent=2)


@mcp.tool()
async def list_survey_networks(projectName: str) -> str:
    """List all Survey Networks inside the given Survey Database.

    Args:
        projectName: Existing survey database name.
    """
    result = await call_bridge(
        "list_survey_networks", {"projectName": projectName})
    return json.dumps(result, indent=2)


@mcp.tool()
async def cancel_command(escCount: int = 5) -> str:
    """Send N escape characters to Civil 3D's command processor.

    Safety valve for recovering from a wedged command state — e.g. when
    IMPORTFIELDBOOK left a modal dialog open. Uses SendStringToExecute
    directly and returns immediately; does not wait for the ESCs to be
    consumed.

    Args:
        escCount: Number of ESCs to queue (default 5).
    """
    result = await call_bridge("cancel_command", {"escCount": escCount})
    return json.dumps(result, indent=2)


# ===========================================================================
# TOOL: Dialog inspection / dismissal
# ===========================================================================
#
# Civil 3D's command thread blocks while a modal dialog is open (Project
# Manager, Save-changes prompt, FBK error, etc.). The bridge wraps every
# action with a pre-check: if a dialog is open, the call short-circuits with
# error "blocked_by_dialog" and a structured `dialogs` payload, so this
# agent can clear the dialog and retry instead of hanging on a 5-minute
# ExecuteOnMainThread timeout.
#
# Dialogs are filtered strictly by Civil 3D's Process ID, so popups from
# other apps (e.g. GlobalProtect reauth) are invisible to these tools.


@mcp.tool()
async def list_dialogs() -> str:
    """List currently-open modal dialogs in the Civil 3D process.

    Returns a JSON array of dialogs, each with: hwnd (numeric handle),
    title, className, message (concatenated static text), ownedByMain
    (true if the dialog's owner is Civil 3D's main MDI frame), and
    buttons (each with hwnd, id, text). An empty list means nothing is
    blocking the command thread.

    The detection rule is "Civil 3D main window is currently disabled" —
    Windows disables a modal's parent, so palettes/toolbars are correctly
    excluded. Cross-process popups (GlobalProtect, browser, etc.) are
    excluded by Process ID filter.
    """
    result = await call_bridge("list_dialogs")
    return json.dumps(result, indent=2)


@mcp.tool()
async def dismiss_dialog(button: str, hwnd: Optional[int] = None) -> str:
    """Dismiss an open Civil 3D dialog.

    Args:
        button: How to dismiss. Supports several forms:
            - "[CLOSE]"  → send WM_CLOSE to the dialog (catch-all; works on
              almost any window, including ones whose Close button is the
              system X with no caption).
            - "[OK]"     → click the standard IDOK control (id=1).
            - "[CANCEL]" → click the standard IDCANCEL control (id=2).
            - "#NNN"     → click the child control with that numeric id
              (use ids returned by `list_dialogs`).
            - Any other string → match a button by visible caption
              (case-insensitive; '&' accelerators stripped). E.g. "OK",
              "Cancel", "Yes", "No", "Save", "Apply".
        hwnd: Optional dialog window handle. If omitted, the topmost dialog
              is used. Get hwnd values from `list_dialogs`.

    Returns JSON: { dismissed: bool, error?: str, clickedButton?: str,
                    remaining: [DialogInfo, ...] }

    Tip: when in doubt, "[CLOSE]" is the safest dismiss — it's the
    moral equivalent of clicking the X.
    """
    params = {"button": button}
    if hwnd is not None:
        params["hwnd"] = hwnd
    result = await call_bridge("dismiss_dialog", params)
    return json.dumps(result, indent=2)


@mcp.tool()
async def parse_fbk(fbkPath: str) -> str:
    """Dry-run FBK parser — reads a FieldGenius .fbk file and returns
    structured records + inferred setups as JSON without touching any
    Civil 3D COM object. Safe to call repeatedly while debugging.

    Supported FBK subset:
        JOB/UNIT/EDM/SCALE/HORIZ/VERT (header noise, ignored)
        NEZ   pt N E Z "desc"
        STN   pt HI "desc"
        AZ    from to dms
        BS    pt circle
        PRISM height
        F1/F2 [VA|ZA] pt HA SD VA "desc"

    Args:
        fbkPath: Absolute path to the .fbk file.
    """
    result = await call_bridge("parse_fbk", {"fbkPath": fbkPath})
    return json.dumps(result, indent=2)


@mcp.tool()
async def import_fbk_parsed(
    projectName: str,
    networkName: str,
    fbkPath: str,
    dryRun: bool = False,
    stopOnError: bool = False,
) -> str:
    """Parser-driven FBK import — bypasses IMPORTFIELDBOOK entirely.

    Parses the FBK file in-process, then drives the Civil 3D Survey COM
    API directly: ControlPoints.Create for pre-STN NEZ lines,
    Setups.Create + InstrumentHeight/BacksightFace1/BacksightOrientation
    for each STN block, and Observations.Create for every F1/F2 line.
    Observations on unknown point numbers cause Civil 3D to compute
    non-control points from the measurements.

    Args:
        projectName: Existing survey database name.
        networkName: Existing survey network name inside the project.
                     Create it first via create_survey_network.
        fbkPath: Absolute path to the .fbk file.
        dryRun: If True, parse + validate only. No COM calls.
        stopOnError: If True, abort at the first creation failure.
                     If False (default), collect errors and continue.
    """
    params = {
        "projectName": projectName,
        "networkName": networkName,
        "fbkPath": fbkPath,
        "dryRun": dryRun,
        "stopOnError": stopOnError,
    }
    result = await call_bridge("import_fbk_parsed", params)
    return json.dumps(result, indent=2)


@mcp.tool()
async def survey_network_summary(
    projectName: str,
    networkName: str,
) -> str:
    """Quick header-level summary of a survey network: control-point count,
    non-control-point count, setup count, total observation count, and
    drawing-state flags. Fast sanity check after an FBK import.

    Args:
        projectName: Survey database name.
        networkName: Network inside that project.
    """
    result = await call_bridge(
        "survey_network_summary",
        {"projectName": projectName, "networkName": networkName},
    )
    return json.dumps(result, indent=2)


@mcp.tool()
async def list_survey_control_points(
    projectName: str,
    networkName: str,
) -> str:
    """List all control points in a survey network with number, name,
    description, E/N/Z, and adjustment state.

    Args:
        projectName: Survey database name.
        networkName: Network inside that project.
    """
    result = await call_bridge(
        "list_survey_control_points",
        {"projectName": projectName, "networkName": networkName},
    )
    return json.dumps(result, indent=2)


@mcp.tool()
async def list_survey_non_control_points(
    projectName: str,
    networkName: str,
) -> str:
    """List all non-control points (observation-derived points) in a survey
    network. Returns number, name, description, and E/N/Z. Note: after an
    FBK import, observation points live under the Setup's Observations
    collection, not NonControlPoints — use list_survey_observations for
    those.

    Args:
        projectName: Survey database name.
        networkName: Network inside that project.
    """
    result = await call_bridge(
        "list_survey_non_control_points",
        {"projectName": projectName, "networkName": networkName},
    )
    return json.dumps(result, indent=2)


@mcp.tool()
async def list_survey_setups(
    projectName: str,
    networkName: str,
) -> str:
    """List all setups (instrument stations) in a survey network. Returns
    station number, backsight number, instrument height, backsight
    direction/orientation (radians), and per-setup observation count.

    Args:
        projectName: Survey database name.
        networkName: Network inside that project.
    """
    result = await call_bridge(
        "list_survey_setups",
        {"projectName": projectName, "networkName": networkName},
    )
    return json.dumps(result, indent=2)


@mcp.tool()
async def list_survey_observations(
    projectName: str,
    networkName: str,
    setupIndex: int | None = None,
    stationPoint: int | None = None,
) -> str:
    """List survey observations inside a network's setups. Returns each
    observation's point number, angle/distance/vertical (radians/meters),
    target height, description, and computed N/E/Z.

    Args:
        projectName: Survey database name.
        networkName: Network inside that project.
        setupIndex: Optional zero-based setup index filter. If given, only
                    that setup's observations are returned.
        stationPoint: Optional station point number filter. If given, only
                      setups whose station point matches are returned.
    """
    params = {
        "projectName": projectName,
        "networkName": networkName,
    }
    if setupIndex is not None:
        params["setupIndex"] = setupIndex
    if stationPoint is not None:
        params["stationPoint"] = stationPoint
    result = await call_bridge("list_survey_observations", params)
    return json.dumps(result, indent=2)


@mcp.tool()
async def add_survey_points_to_drawing(
    projectName: str,
    networkName: str,
    includeControl: bool = True,
    includeNonControl: bool = True,
) -> str:
    """Materialize a survey network's points into the active drawing as
    CogoPoints via the COM network's AddPointsToDrawing() method (falls
    back to AddToDrawing() if that fails). Returns before/after CogoPoint
    counts.

    Args:
        projectName: Survey database name.
        networkName: Network inside that project.
        includeControl: Advisory flag (currently AddPointsToDrawing adds
                        all points).
        includeNonControl: Advisory flag (same note as includeControl).
    """
    result = await call_bridge(
        "add_survey_points_to_drawing",
        {
            "projectName": projectName,
            "networkName": networkName,
            "includeControl": includeControl,
            "includeNonControl": includeNonControl,
        },
    )
    return json.dumps(result, indent=2)


# ===========================================================================
# Entry point
# ===========================================================================

if __name__ == "__main__":
    logger.info(f"Starting {APP_NAME} MCP Server, bridge URL: {BRIDGE_URL}")
    mcp.run(transport="stdio")  # Explicit stdio for Claude Desktop

