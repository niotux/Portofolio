#!/usr/bin/env bash
# Builds texp in Release and installs it for the current user.
#   App files: $TEXP_HOME (default ~/.local/share/texp)
#   Command:   $TEXP_BIN/texp (default ~/.local/bin/texp), a symlink to the app
# Re-running it upgrades in place. Requires the .NET 10 SDK.
set -euo pipefail

TEXP_HOME="${TEXP_HOME:-$HOME/.local/share/texp}"
TEXP_BIN="${TEXP_BIN:-$HOME/.local/bin}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "error: dotnet not found; install the .NET 10 SDK first" >&2
  exit 1
fi

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

echo "building..."
dotnet publish "$ROOT/src/Texp/Texp.csproj" -c Release -o "$STAGE" --nologo -v quiet

# Swap the whole directory so an upgrade never leaves a mix of old and new files.
mkdir -p "$(dirname "$TEXP_HOME")" "$TEXP_BIN"
rm -rf "$TEXP_HOME.old"
if [ -d "$TEXP_HOME" ]; then
  mv "$TEXP_HOME" "$TEXP_HOME.old"
fi
mv "$STAGE" "$TEXP_HOME"
rm -rf "$TEXP_HOME.old"
chmod 755 "$TEXP_HOME" "$TEXP_HOME/texp"

ln -sfn "$TEXP_HOME/texp" "$TEXP_BIN/texp"

echo "installed: $TEXP_BIN/texp -> $TEXP_HOME/texp"
case ":$PATH:" in
  *":$TEXP_BIN:"*) ;;
  *) echo "note: $TEXP_BIN is not on your PATH; add it to run 'texp' directly" ;;
esac
