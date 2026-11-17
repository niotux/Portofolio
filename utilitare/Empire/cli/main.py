"""CLI frontend — entry point and dispatch."""

from __future__ import annotations

import sys
from pathlib import Path

# Ensure project root is on sys.path
if str(Path(__file__).resolve().parent.parent) not in sys.path:
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from empire.io import set_sink
from cli.sink import CliSink
from cli.args import dispatch


def main() -> None:
    """CLI entry point."""
    set_sink(CliSink())
    dispatch(sys.argv[1:])


if __name__ == "__main__":
    main()
