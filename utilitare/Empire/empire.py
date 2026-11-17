#!/usr/bin/env python3
"""
empire — runtime dispatcher.

Reads runtimes from settings.json and dispatches to the selected one.
No runtime names are hardcoded; everything is discovered from the config.

Usage:
  empire                     (uses default runtime from settings.json)
  empire -r|--runtime <name> (explicitly select a runtime)
"""

from __future__ import annotations

import importlib
import json
import sys
from pathlib import Path

# ── paths ──────────────────────────────────────────────────────────────────────

_PROJECT_ROOT = Path(__file__).resolve().parent
_SETTINGS     = _PROJECT_ROOT / "settings.json"

# Ensure project root is on sys.path so top-level packages (cli, gui, tui, …)
# are importable regardless of how this script is launched.
if str(_PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(_PROJECT_ROOT))


# ── config ─────────────────────────────────────────────────────────────────────

def _load_runtimes() -> dict:
    """Load the Runtimes section from settings.json."""
    if not _SETTINGS.exists():
        return {}
    try:
        return json.loads(_SETTINGS.read_text()).get("Runtimes", {})
    except Exception:
        return {}


def _find_default(runtimes: dict) -> str | None:
    """Return the name of the runtime marked as default, or None."""
    for name, cfg in runtimes.items():
        if cfg.get("Default"):
            return name
    return None


# ── dispatch ───────────────────────────────────────────────────────────────────

def main(runtime_name: str | None = None) -> None:
    from cli.sink import CliSink
    from empire.io import set_sink, info, error, flush, warn

    set_sink(CliSink())

    runtimes = _load_runtimes()

    if not runtimes:
        error("No runtimes configured in settings.json.")
        flush()
        sys.exit(1)

    # Resolve which runtime to use.
    if runtime_name:
        if runtime_name not in runtimes:
            error(f"Unknown runtime '{runtime_name}'.  Available: {', '.join(runtimes)}")
            flush()
            sys.exit(1)
    else:
        default = _find_default(runtimes)
        if len(runtimes) == 1:
            runtime_name = next(iter(runtimes))
        elif default:
            runtime_name = default
        else:
            # Interactive prompt when no default is set.
            info("Available runtimes:")
            for name, cfg in runtimes.items():
                label = cfg.get("Label", name.title())
                tag = f"  (default)" if name == default else ""
                info(f"  {name:<10}  {label}{tag}")
            flush()

            while True:
                choice = input("Select runtime: ").strip()
                if not choice:
                    choice = default or ""
                if choice and choice in runtimes:
                    runtime_name = choice
                    break
                warn(f"Unknown runtime '{choice}'.  Available: {', '.join(runtimes)}")
                flush()

    runtime_cfg  = runtimes[runtime_name]
    module_path  = runtime_cfg.get("Module", f"{runtime_name}.main")
    label        = runtime_cfg.get("Label", runtime_name.title())

    info(f"Loading '{label}'", module_path)
    flush()

    # Dynamically import and call main().
    try:
        mod = importlib.import_module(module_path)
    except ImportError as exc:
        error(f"Cannot load runtime '{runtime_name}': {exc}")
        flush()
        sys.exit(1)

    entry = getattr(mod, "main", None)
    if not callable(entry):
        error(f"Runtime '{runtime_name}' module '{module_path}' has no main() function.")
        flush()
        sys.exit(1)

    entry()


# ── CLI parsing ────────────────────────────────────────────────────────────────

if __name__ == "__main__":
    _runtime   = None
    _remaining: list[str] = []

    args = sys.argv[1:]
    i    = 0
    while i < len(args):
        arg = args[i]
        if arg in ("-r", "--runtime"):
            if i + 1 < len(args):
                _runtime = args[i + 1]
                i += 2
            else:
                print("Flag -r requires a runtime name.")
                sys.exit(1)
        else:
            _remaining.append(arg)
            i += 1

    sys.argv[:] = [sys.argv[0]] + _remaining

    main(runtime_name=_runtime)
