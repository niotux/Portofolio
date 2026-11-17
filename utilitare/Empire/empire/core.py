"""Core utilities: platform detection, JSON loading, settings, env expansion."""

import json
import platform
import re
import sys
from pathlib import Path


EMPIRE_DIR  = Path(__file__).resolve().parent.parent
MODULES_DIR = EMPIRE_DIR / "modules"


# ── JSON with comments ────────────────────────────────────────────────────────

def load_json(path: Path) -> dict:
    """Load a JSON file that may contain `//`-style comments."""
    with open(path) as f:
        raw = "\n".join(
            line for line in f
            if not line.lstrip().startswith("//")
        )
    return json.loads(raw)


# ── Settings ──────────────────────────────────────────────────────────────────

def load_settings() -> dict:
    path = EMPIRE_DIR / "settings.json"
    if not path.exists():
        return {}
    try:
        return load_json(path)
    except Exception:
        return {}


SETTINGS = load_settings()


# ── Platform ──────────────────────────────────────────────────────────────────

def empire_os() -> str:
    s = platform.system().lower()
    if s == "darwin":
        return "macos"
    if s.startswith(("mingw", "msys")):
        return "windows"
    return s


def empire_arch() -> str:
    m = platform.machine().lower()
    if m in ("x86_64", "amd64"):
        return "x86_64"
    if m in ("i386", "i686"):
        return "x86"
    if m in ("aarch64", "arm64"):
        return "arm64"
    return m


def empire_rid() -> str:
    return f"{empire_os()}-{empire_arch()}"


# ── Env-var expansion ────────────────────────────────────────────────────────

def expand(s: str, env: dict) -> str:
    """Expand `${VAR}` / `$VAR` placeholders from *env*."""

    def replacer(m: "re.Match"):
        key = m.group(1) or m.group(2)
        return env.get(key, m.group(0))

    return re.sub(r'\$\{(\w+)\}|\$(\w+)', replacer, s)


# ── Path helpers ──────────────────────────────────────────────────────────────

def bashify_path(p: Path) -> str:
    """Normalize a path for use in shell commands (cross-platform)."""
    s = str(p).replace("\\", "/")
    if len(s) >= 2 and s[1] == ':':
        s = f"/{s[0].lower()}{s[2:]}"
    return s
