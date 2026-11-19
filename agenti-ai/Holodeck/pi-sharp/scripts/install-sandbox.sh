#!/usr/bin/env bash
# Install a framework-dependent pi-sharp tree onto an Ubuntu 24 LXC sandbox.
#
# Default layout (matches CLAUDE.md / pi-deploy):
#   PREFIX/           compiled output (/opt/pi-sharp)
#   BINDIR/pi-sharp   wrapper that execs PREFIX/pi-sharp (/usr/local/bin)
#
# Usage:
#   scripts/install-sandbox.sh [--prefix DIR] [--bindir DIR] [--from DIR|TARBALL] [--no-symlink]
#   PREFIX=/opt/pi-sharp sudo scripts/install-sandbox.sh
#
# When invoked from a packaged tarball (script sits next to pi-sharp.dll),
# --from defaults to that directory. From the git repo it publishes first.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
PREFIX="${PREFIX:-/opt/pi-sharp}"
BINDIR="${BINDIR:-/usr/local/bin}"
FROM=""
NO_SYMLINK=0

usage() {
  cat <<'EOF'
Usage: install-sandbox.sh [options]
  --prefix DIR     install tree (default /opt/pi-sharp, or $PREFIX)
  --bindir DIR     wrapper directory (default /usr/local/bin, or $BINDIR)
  --from PATH      published directory or pi-sharp-linux-x64.tar.gz
  --no-symlink     skip the BINDIR/pi-sharp wrapper
  -h, --help       this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --prefix) PREFIX="$2"; shift 2 ;;
    --bindir) BINDIR="$2"; shift 2 ;;
    --from) FROM="$2"; shift 2 ;;
    --no-symlink) NO_SYMLINK=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done

resolve_from() {
  if [[ -n "$FROM" ]]; then
    printf '%s\n' "$FROM"
    return
  fi
  if [[ -f "$HERE/pi-sharp.dll" ]]; then
    printf '%s\n' "$HERE"
    return
  fi
  if [[ -f "$HERE/../src/PiSharp.Cli/PiSharp.Cli.csproj" ]]; then
    local repo
    repo="$(cd "$HERE/.." && pwd)"
    "$repo/scripts/publish.sh" "$repo/artifacts/linux-x64" >&2
    printf '%s\n' "$repo/artifacts/linux-x64"
    return
  fi
  echo "cannot find a published tree; pass --from" >&2
  exit 1
}

FROM="$(resolve_from)"

STAGE=""
cleanup() {
  if [[ -n "$STAGE" && -d "$STAGE" ]]; then
    rm -rf "$STAGE"
  fi
}
trap cleanup EXIT

if [[ -f "$FROM" ]]; then
  STAGE="$(mktemp -d)"
  tar -xzf "$FROM" -C "$STAGE"
  if [[ -d "$STAGE/pi-sharp" ]]; then
    FROM="$STAGE/pi-sharp"
  else
    echo "tarball does not contain a pi-sharp/ directory" >&2
    exit 1
  fi
fi

if [[ ! -f "$FROM/pi-sharp.dll" ]]; then
  echo "not a pi-sharp publish tree: $FROM (missing pi-sharp.dll)" >&2
  exit 1
fi

if [[ ! -f "$FROM/pi-sharp.runtimeconfig.json" ]]; then
  echo "not a framework-dependent publish: $FROM (missing runtimeconfig.json)" >&2
  exit 1
fi

mkdir -p "$PREFIX"
# Replace contents but keep PREFIX itself (may be a mount).
find "$PREFIX" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
cp -a "$FROM"/. "$PREFIX/"
chmod a+x "$PREFIX/pi-sharp" 2>/dev/null || true

if [[ "$NO_SYMLINK" -eq 0 ]]; then
  mkdir -p "$BINDIR"
  cat > "$BINDIR/pi-sharp" <<EOF
#!/bin/sh
exec "$PREFIX/pi-sharp" "\$@"
EOF
  chmod a+x "$BINDIR/pi-sharp"
fi

echo "Installed pi-sharp to $PREFIX"
if [[ "$NO_SYMLINK" -eq 0 ]]; then
  echo "Wrapper: $BINDIR/pi-sharp -> $PREFIX/pi-sharp"
fi
echo "Requires the .NET 10 runtime (framework-dependent linux-x64)."
echo "Per-user config: ~/.config/pi-sharp/  (see docs/06-configuration.md)"
