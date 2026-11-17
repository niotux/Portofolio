#!/usr/bin/env bash
# Builds dtm in Release and installs it for the current user.
#   App files: $DTM_HOME (default ~/.local/share/dtm)
#   Command:   $DTM_BIN/dtm (default ~/.local/bin/dtm), a symlink to the app
# Re-running it upgrades in place. Requires the .NET 10 SDK.
set -euo pipefail

DTM_HOME="${DTM_HOME:-$HOME/.local/share/dtm}"
DTM_BIN="${DTM_BIN:-$HOME/.local/bin}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "error: dotnet not found; install the .NET 10 SDK first" >&2
  exit 1
fi
if ! command -v docker >/dev/null 2>&1; then
  echo "warning: docker not found on PATH; dtm needs it at runtime" >&2
fi

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

echo "building..."
dotnet publish "$ROOT/src/Dtm/Dtm.csproj" -c Release -o "$STAGE" --nologo -v quiet

# Swap the whole directory so an upgrade never leaves a mix of old and new files.
mkdir -p "$(dirname "$DTM_HOME")" "$DTM_BIN"
rm -rf "$DTM_HOME.old"
if [ -d "$DTM_HOME" ]; then
  mv "$DTM_HOME" "$DTM_HOME.old"
fi
mv "$STAGE" "$DTM_HOME"
rm -rf "$DTM_HOME.old"
chmod 755 "$DTM_HOME" "$DTM_HOME/dtm"

ln -sfn "$DTM_HOME/dtm" "$DTM_BIN/dtm"

echo "installed: $DTM_BIN/dtm -> $DTM_HOME/dtm"
case ":$PATH:" in
  *":$DTM_BIN:"*) ;;
  *) echo "note: $DTM_BIN is not on your PATH; add it to run 'dtm' directly" ;;
esac
