"""OutputSink protocol — execution feedback during tool application."""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
from typing import Protocol, runtime_checkable


# ── Levels ─────────────────────────────────────────────────────────────────────

class Level(Enum):
    """Message severity."""

    INFO  = 1  # informational (tool starting, dependency resolved)
    WARN  = 2  # warning (skipped dep, deprecated option)
    ERROR = 3  # error (missing file, invalid config, command failed)


# ── Events ─────────────────────────────────────────────────────────────────────

@dataclass(frozen=True)
class Message:
    """A single unit of execution feedback."""

    level: Level
    text: str
    detail: str | None = None


# ── Protocol ───────────────────────────────────────────────────────────────────

@runtime_checkable
class OutputSink(Protocol):
    """
    Receives execution feedback from Empire during tool application.

    The core library emits Messages. The sink decides how to display them
    (print to terminal, append to Tkinter Text widget, scroll in a Textual
    log window, etc).
    """

    def message(self, msg: Message) -> None: ...

    def flush(self) -> None: ...
