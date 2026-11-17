"""CLI OutputSink — routes messages to stdout with ANSI formatting."""

from __future__ import annotations

import sys
from pathlib import Path
from typing import TYPE_CHECKING

# Ensure empire/ is importable from cli/
_project_root = Path(__file__).resolve().parent.parent
if str(_project_root) not in sys.path:
    sys.path.insert(0, str(_project_root))

if TYPE_CHECKING:
    from empire.io.protocol import Message

from empire.ansi import ANSI, ANSI as COLORS
from empire.core import SETTINGS


# ── Prefix resolution ──────────────────────────────────────────────────────────

_LEVEL_KEY = {
    "INFO":  "InfoPrefix",
    "WARN":  "WarnPrefix",
    "ERROR": "ErrorPrefix",
}


def _resolve_prefix(level_name: str) -> str:
    key = _LEVEL_KEY.get(level_name)
    if not key:
        return f"[ {level_name} ]"
    template = SETTINGS.get("Logging", {}).get(key)
    if not template:
        return f"[ {level_name} ]"
    for name, code in COLORS.items():
        template = template.replace(f"{{{name}}}", code)
    return template


def _detail(detail: str | None) -> str:
    if not detail:
        return ""
    return f" {ANSI.get('GRAY', '')}[{detail}]{ANSI.get('RESET', '')}"


# ── Sink ───────────────────────────────────────────────────────────────────────

class CliSink:
    """OutputSink implementation that writes to stdout (terminal)."""

    def message(self, msg: Message) -> None:
        prefix = _resolve_prefix(msg.level.name)
        detail = _detail(msg.detail)
        print(f"{prefix} {msg.text}{detail}")

    def flush(self) -> None:
        sys.stdout.flush()
