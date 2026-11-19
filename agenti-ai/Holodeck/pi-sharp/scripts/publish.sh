#!/usr/bin/env bash
# Framework-dependent linux-x64 publish of pi-sharp.
# Usage: scripts/publish.sh [output-dir]
# Default output: <repo>/artifacts/linux-x64
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-"$ROOT/artifacts/linux-x64"}"
CONFIG="${CONFIGURATION:-Release}"

mkdir -p "$OUT"

dotnet publish "$ROOT/src/PiSharp.Cli/PiSharp.Cli.csproj" \
  --nologo \
  -c "$CONFIG" \
  -r linux-x64 \
  --self-contained false \
  -p:PublishSingleFile=false \
  -p:PublishTrimmed=false \
  -p:PublishReadyToRun=false \
  -p:DebugType=embedded \
  -o "$OUT"

echo "Published framework-dependent linux-x64 to $OUT"
