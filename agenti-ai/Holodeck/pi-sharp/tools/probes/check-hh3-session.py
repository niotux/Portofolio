#!/usr/bin/env python3
"""Verify the HH3 oversized-tool-result marker and its complete temp-file artifact."""

import json
import math
import re
import sys
from pathlib import Path

MARKER = re.compile(
    r"\[\.\.\.\] (?P<omitted>\d+) tokens omitted; original "
    r"(?P<original>\d+) tokens, (?P<lines>\d+) lines; full output: (?P<path>[^\]]+)\]"
)


def strings(value):
    if isinstance(value, str):
        yield value
    elif isinstance(value, dict):
        for child in value.values():
            yield from strings(child)
    elif isinstance(value, list):
        for child in value:
            yield from strings(child)


def verify(session_jsonl):
    session = Path(session_jsonl)
    markers = []
    for line_number, line in enumerate(session.read_text(encoding="utf-8").splitlines(), 1):
        try:
            row = json.loads(line)
        except json.JSONDecodeError as error:
            raise ValueError(f"invalid JSON at line {line_number}: {error}") from error
        markers.extend((match, line_number) for text in strings(row) for match in MARKER.finditer(text))

    for marker, line_number in markers:
        original_tokens = int(marker.group("original"))
        artifact = Path(marker.group("path"))
        if not artifact.is_file():
            continue
        text = artifact.read_text(encoding="utf-8")
        estimated_tokens = math.ceil(len(text) / 4)
        if estimated_tokens >= original_tokens and original_tokens > 10_000:
            return f"HH3 PASS: line {line_number}; {original_tokens} tokens; full output {artifact} ({len(text)} chars)"
    raise ValueError("no valid >10,000-token omission marker with its complete referenced temp file found")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit(f"usage: {Path(sys.argv[0]).name} SESSION.jsonl")
    try:
        print(verify(sys.argv[1]))
    except (OSError, ValueError) as error:
        raise SystemExit(f"HH3 FAIL: {error}")
