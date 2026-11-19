#!/usr/bin/env bash
# Native AOT linux-x64 verification publish of pi-sharp.
# Usage: scripts/publish-aot.sh [output-dir]
# Default output: <repo>/artifacts/linux-x64-aot
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-"$ROOT/artifacts/linux-x64-aot"}"

mkdir -p "$OUT"

dotnet publish "$ROOT/src/PiSharp.Cli/PiSharp.Cli.csproj" \
  --nologo \
  -c Aot \
  -r linux-x64 \
  --self-contained true \
  -p:PublishAot=true \
  -p:PublishTrimmed=true \
  -p:DebugType=embedded \
  -o "$OUT"

echo "Published Native AOT linux-x64 to $OUT"
