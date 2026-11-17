"""I/O abstraction layer — shared by all frontends."""

from empire.io.internal import emit, info, warn, error, flush
from empire.io.protocol import Level, Message, OutputSink
from empire.io.registry import get_sink, set_sink, use_sink

__all__ = [
    "Level",
    "Message",
    "OutputSink",
    "emit",
    "info",
    "warn",
    "error",
    "flush",
    "get_sink",
    "set_sink",
    "use_sink",
]
