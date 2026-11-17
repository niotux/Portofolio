"""Dependency-injection registry for OutputSink instances."""

from __future__ import annotations

from contextlib import contextmanager
from contextvars import ContextVar
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from empire.io.protocol import OutputSink


# ── Storage ────────────────────────────────────────────────────────────────────

_current_sink: ContextVar["OutputSink | None"] = ContextVar(
    "_current_sink",
    default=None,
)


# ── Public API ─────────────────────────────────────────────────────────────────

def get_sink() -> "OutputSink":
    """Return the currently active sink.

    Auto-defaults to the CLI sink if none has been installed yet.
    Raises RuntimeError if the default sink cannot be loaded.
    """
    sink = _current_sink.get()
    if sink is None:
        try:
            from cli.sink import CliSink
        except ImportError:
            raise RuntimeError(
                "No OutputSink installed and 'cli.sink' cannot be imported. "
                "Call empire.io.set_sink(...) before accessing the sink."
            ) from None

        set_sink(CliSink())
        sink = _current_sink.get()
        assert sink is not None
    return sink


def set_sink(sink: "OutputSink") -> None:
    """Install *sink* as the active output backend for the current context."""
    _current_sink.set(sink)


@contextmanager
def use_sink(sink: "OutputSink"):
    """Temporarily swap the active sink within a context."""
    token = _current_sink.set(sink)
    try:
        yield
    finally:
        _current_sink.reset(token)
