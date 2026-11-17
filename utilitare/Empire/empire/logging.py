"""Logging helpers — thin shim to empire.io."""

import sys

from empire.io import info, warn, error, flush
from empire.io.protocol import Level


def log(level: str, msg: str, detail: str | None = None) -> None:
    """Emit a log message through the active OutputSink."""
    mapping = {
        "info":  info,
        "warn":  warn,
        "error": error,
    }
    fn = mapping.get(level, info)
    fn(msg, detail)


def die(msg: str, detail: str | None = None) -> None:
    """Emit an error and exit."""
    error(msg, detail)
    flush()
    sys.exit(1)
