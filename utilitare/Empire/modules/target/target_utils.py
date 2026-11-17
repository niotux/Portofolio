"""Shared utilities for empire target tools."""

import json
import os
import sys
from pathlib import Path


# ── ANSI colours ──────────────────────────────────────────────────────────────

RESET  = "\033[0m"
RED    = "\033[31m"
GREEN  = "\033[32m"
YELLOW = "\033[33m"
GRAY   = "\033[90m"


def _log(prefix: str, fg: str, msg: str, detail: str | None = None) -> None:
    detail_s = f" {GRAY}[{detail}]{RESET}" if detail else ""
    print(f"{GRAY}[ {fg}{prefix}{GRAY} ]{RESET} {msg}{detail_s}")


def log_info(msg: str, detail: str | None = None) -> None:
    _log("Info", GREEN, msg, detail)


def log_warn(msg: str, detail: str | None = None) -> None:
    _log("Warn", YELLOW, msg, detail)


def log_error(msg: str, detail: str | None = None) -> None:
    _log("Error", RED, msg, detail)


# ── JSON helpers ──────────────────────────────────────────────────────────────

def load_json(path: Path) -> dict:
    with open(path) as f:
        raw = "\n".join(
            line for line in f
            if not line.lstrip().startswith("//")
        )
    return json.loads(raw)


def save_json(path: Path, data: dict) -> None:
    tmp = path.with_suffix(".tmp")
    with open(tmp, "w") as f:
        json.dump(data, f, indent=2)
        f.write("\n")
    os.replace(tmp, path)


# ── Target file discovery ────────────────────────────────────────────────────

def find_target_file(cwd: Path | None = None) -> Path:
    """Return the first target.*.json found in *cwd*."""
    cwd = cwd or Path.cwd()
    matches = list(cwd.glob("target.*.json"))
    if not matches:
        log_error("No target.*.json file found", f"directory: {cwd}")
        sys.exit(1)
    return matches[0]


def load_target(cwd: Path | None = None) -> tuple[dict, Path]:
    """Load and return (data, path)."""
    path = find_target_file(cwd)
    return load_json(path), path


# ── Module detection ──────────────────────────────────────────────────────────

def detect_module(dep_path_str: str) -> str:
    """Walk *dep_path* up the tree until a target.*.json is found.

    Returns the module name extracted from the filename.
    """
    walk = Path(dep_path_str).resolve()
    while walk != walk.parent:
        matches = list(walk.glob("target.*.json"))
        if matches:
            name = matches[0].name  # e.g. target.c.json
            # Strip "target." prefix and ".json" suffix
            return name[7:-5]
        walk = walk.parent

    log_error(
        "Could not find a target.*.json file",
        f"path: {dep_path_str}",
    )
    sys.exit(1)
