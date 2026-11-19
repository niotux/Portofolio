#!/usr/bin/env bash
# Pack the linux-x64 publish tree into a tarball for LXC sandbox install.
# Usage: scripts/package.sh [tarball-path]
# Default tarball: <repo>/artifacts/pi-sharp-linux-x64.tar.gz
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PUBLISH="$ROOT/artifacts/linux-x64"
STAGE="$ROOT/artifacts/package-linux-x64"
OUT="${1:-"$ROOT/artifacts/pi-sharp-linux-x64.tar.gz"}"

"$ROOT/scripts/publish.sh" "$PUBLISH"

rm -rf "$STAGE"
mkdir -p "$STAGE/pi-sharp"
cp -a "$PUBLISH"/. "$STAGE/pi-sharp/"
cp "$ROOT/LICENSE" "$STAGE/pi-sharp/"
cp "$ROOT/LICENSE.pi-upstream" "$STAGE/pi-sharp/"
cp "$ROOT/packaging/models.json.example" "$STAGE/pi-sharp/"
cp "$ROOT/scripts/install-sandbox.sh" "$STAGE/pi-sharp/"
cp "$ROOT/docs/05-publish-and-deploy.md" "$STAGE/pi-sharp/INSTALL.md"

{
  echo "name=pi-sharp"
  echo "rid=linux-x64"
  echo "selfContained=false"
  echo "framework=net10.0"
  echo "commit=$(git -C "$ROOT" rev-parse --short HEAD 2>/dev/null || echo unknown)"
  echo "builtUtc=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
} > "$STAGE/pi-sharp/VERSION"

mkdir -p "$(dirname "$OUT")"
tar -C "$STAGE" -czf "$OUT" pi-sharp
echo "Packaged $OUT"
