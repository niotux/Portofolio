#!/usr/bin/env bash
# Removes what install.sh put in place. Uses the same TEXP_HOME / TEXP_BIN overrides.
set -euo pipefail

TEXP_HOME="${TEXP_HOME:-$HOME/.local/share/texp}"
TEXP_BIN="${TEXP_BIN:-$HOME/.local/bin}"
LINK="$TEXP_BIN/texp"

# Only remove the command if it is our symlink, never some other 'texp'.
if [ -L "$LINK" ] && [ "$(readlink "$LINK")" = "$TEXP_HOME/texp" ]; then
  rm "$LINK"
  echo "removed $LINK"
elif [ -e "$LINK" ]; then
  echo "left $LINK alone: it isn't the symlink install.sh created" >&2
fi

# Only remove the app directory if it looks like a texp install.
if [ -f "$TEXP_HOME/texp.dll" ]; then
  rm -rf "$TEXP_HOME"
  echo "removed $TEXP_HOME"
elif [ -e "$TEXP_HOME" ]; then
  echo "left $TEXP_HOME alone: it doesn't look like a texp install" >&2
else
  echo "nothing installed at $TEXP_HOME"
fi
