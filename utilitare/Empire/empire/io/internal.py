"""Convenience wrappers — what Empire's core logic imports."""

from __future__ import annotations

from empire.io.protocol import Level, Message
from empire.io.registry import get_sink


def emit(level: Level, text: str, detail: str | None = None) -> None:
    """Emit a message through the active sink."""
    get_sink().message(Message(level, text, detail))


def info(text: str, detail: str | None = None) -> None:
    """Emit an informational message."""
    emit(Level.INFO, text, detail)


def warn(text: str, detail: str | None = None) -> None:
    """Emit a warning."""
    emit(Level.WARN, text, detail)


def error(text: str, detail: str | None = None) -> None:
    """Emit an error message."""
    emit(Level.ERROR, text, detail)


def flush() -> None:
    """Drain buffered output."""
    get_sink().flush()
