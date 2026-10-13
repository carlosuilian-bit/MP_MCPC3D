"""Workflow tracing for the MCP server.

Captures every tool/bridge call — name, arguments, result summary, status, and
duration — to a per-session JSONL file. The goal is to build a corpus of real
survey/CAD workflows ("which operations chain, in what order, with what
defaults") while the licensed software is still available, so an open-source
clone can later be specified and validated against it.

This server's tools are async, so install_tracing() wraps each @mcp.tool()
with an *async-aware* wrapper (it awaits coroutine functions, calls sync ones
directly). This captures the MCP-tool-level grammar (e.g. draw_line_by_bearing,
add_labels_to_layer) — a DIFFERENT abstraction level from the C# bridge trace,
which only ever sees the primitive bridge actions (draw_line, add_line_label).
Both layers are wanted; each record is tagged with `layer` so they can be
told apart when the two corpora are merged. The Python layer here is
`mcp_tool`; the C# bridge layer is `bridge`.

Environment
-----------
- {PREFIX}_TRACE      "0"/"off"/"false" disables tracing (default: enabled).
- {PREFIX}_TRACE_DIR  directory for JSONL files (default: <project>/traces).
"""

from __future__ import annotations

import os
import json
import time
import uuid
import inspect
import functools
import threading
import datetime as _dt

_LOCK = threading.Lock()
_STATE: dict = {
    "enabled": False,
    "app": "unknown",
    "layer": "mcp_tool",
    "path": None,
    "session": None,
    "seq": 0,
}

_MAX_RESULT_STR = 4000


def _truthy_disabled(val: str | None) -> bool:
    return (val or "").strip().lower() in {"0", "off", "false", "no"}


def configure(app_name: str, env_prefix: str, default_dir: str,
              layer: str = "mcp_tool") -> None:
    """Initialise tracing state and write a session-start record.

    Safe to call once at server startup. No-op (apart from disabling) if the
    {env_prefix}_TRACE env var is falsy. `layer` tags every record so this
    corpus can be merged with the C# bridge trace (layer="bridge") without
    losing the abstraction-level distinction.
    """
    enabled = not _truthy_disabled(os.environ.get(f"{env_prefix}_TRACE"))
    _STATE["enabled"] = enabled
    _STATE["app"] = app_name
    _STATE["layer"] = layer
    if not enabled:
        return

    trace_dir = os.environ.get(f"{env_prefix}_TRACE_DIR") or default_dir
    try:
        os.makedirs(trace_dir, exist_ok=True)
    except Exception:
        _STATE["enabled"] = False
        return

    session = _dt.datetime.now().strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:6]
    _STATE["session"] = session
    _STATE["path"] = os.path.join(trace_dir, f"{app_name}-{session}.jsonl")
    _STATE["seq"] = 0

    _append({
        "kind": "session_start",
        "app": app_name,
        "pid": os.getpid(),
        "cwd": os.getcwd(),
    })


def _append(record: dict) -> None:
    """Append one record as a JSONL line. Never raises."""
    if not _STATE["enabled"] or not _STATE["path"]:
        return
    try:
        with _LOCK:
            _STATE["seq"] += 1
            record = {
                "ts": _dt.datetime.now(_dt.timezone.utc).isoformat(),
                "session": _STATE["session"],
                "app": _STATE["app"],
                "layer": _STATE["layer"],
                "seq": _STATE["seq"],
                **record,
            }
            with open(_STATE["path"], "a", encoding="utf-8") as f:
                f.write(json.dumps(record, default=str, ensure_ascii=False) + "\n")
    except Exception:
        pass


def _summarise(value):
    if isinstance(value, str):
        if len(value) > _MAX_RESULT_STR:
            return value[:_MAX_RESULT_STR] + f"...<+{len(value) - _MAX_RESULT_STR} chars>"
        return value
    if isinstance(value, dict):
        return {k: _summarise(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        if len(value) > 200:
            return [_summarise(v) for v in value[:200]] + [f"...<+{len(value) - 200} items>"]
        return [_summarise(v) for v in value]
    return value


def _infer_status(result) -> str:
    if isinstance(result, dict):
        s = result.get("status")
        if isinstance(s, str):
            return s
        if result.get("error"):
            return "error"
        if result.get("_dialogs"):
            return "ok_with_dialog"
    return "ok"


def log_call(tool: str, params: dict, result=None, *,
             duration_ms: float | None = None, error: str | None = None) -> None:
    """Log a single tool/bridge call. Use this at the call_bridge chokepoint."""
    _append({
        "kind": "call",
        "tool": tool,
        "params": _summarise(params or {}),
        "result": None if error else _summarise(result),
        "status": "error" if error else _infer_status(result),
        "duration_ms": round(duration_ms, 1) if duration_ms is not None else None,
        "error": error,
    })


def _bind_args(fn, args, kwargs) -> dict:
    try:
        bound = inspect.signature(fn).bind(*args, **kwargs)
        bound.apply_defaults()
        return dict(bound.arguments)
    except Exception:
        return {"_args": _summarise(list(args)), "_kwargs": _summarise(dict(kwargs))}


def _wrap_fn(fn):
    """Logging wrapper that preserves fn's signature. Async-aware: coroutine
    functions get an async wrapper (awaited), sync functions a sync one."""
    if inspect.iscoroutinefunction(fn):
        @functools.wraps(fn)
        async def awrapper(*args, **kwargs):
            t0 = time.perf_counter()
            params = _bind_args(fn, args, kwargs)
            try:
                result = await fn(*args, **kwargs)
            except Exception as e:
                log_call(fn.__name__, params, None,
                         duration_ms=(time.perf_counter() - t0) * 1000, error=repr(e))
                raise
            log_call(fn.__name__, params, result,
                     duration_ms=(time.perf_counter() - t0) * 1000)
            return result
        wrapper = awrapper
    else:
        @functools.wraps(fn)
        def swrapper(*args, **kwargs):
            t0 = time.perf_counter()
            params = _bind_args(fn, args, kwargs)
            try:
                result = fn(*args, **kwargs)
            except Exception as e:
                log_call(fn.__name__, params, None,
                         duration_ms=(time.perf_counter() - t0) * 1000, error=repr(e))
                raise
            log_call(fn.__name__, params, result,
                     duration_ms=(time.perf_counter() - t0) * 1000)
            return result
        wrapper = swrapper

    try:
        wrapper.__signature__ = inspect.signature(fn)
    except (ValueError, TypeError):
        pass
    return wrapper


def install_tracing(mcp, app_name: str, env_prefix: str, default_dir: str,
                    layer: str = "mcp_tool") -> None:
    """Monkeypatch mcp.tool so every @mcp.tool()-decorated function is traced.

    Must run AFTER `mcp = FastMCP(...)` and BEFORE any @mcp.tool() decorator
    executes (i.e. before the tool defs in this module run / before tool
    modules are imported), so the wrapped decorator is the one in effect.
    The wrapper is async-aware, so it is safe for this server's async tools.
    """
    configure(app_name, env_prefix, default_dir, layer=layer)
    if not _STATE["enabled"]:
        return
    original_tool = mcp.tool

    def traced_tool(*d_args, **d_kwargs):
        real_decorator = original_tool(*d_args, **d_kwargs)

        def decorator(fn):
            return real_decorator(_wrap_fn(fn))

        return decorator

    mcp.tool = traced_tool
