#!/usr/bin/env bash
# Removes what install.sh put in place. Uses the same DTM_HOME / DTM_BIN overrides.
set -euo pipefail

DTM_HOME="${DTM_HOME:-$HOME/.local/share/dtm}"
DTM_BIN="${DTM_BIN:-$HOME/.local/bin}"
LINK="$DTM_BIN/dtm"

# Only remove the command if it is our symlink, never some other 'dtm'.
if [ -L "$LINK" ] && [ "$(readlink "$LINK")" = "$DTM_HOME/dtm" ]; then
  rm "$LINK"
  echo "removed $LINK"
elif [ -e "$LINK" ]; then
  echo "left $LINK alone: it isn't the symlink install.sh created" >&2
fi

# Only remove the app directory if it looks like a dtm install.
if [ -f "$DTM_HOME/dtm.dll" ]; then
  rm -rf "$DTM_HOME"
  echo "removed $DTM_HOME"
elif [ -e "$DTM_HOME" ]; then
  echo "left $DTM_HOME alone: it doesn't look like a dtm install" >&2
else
  echo "nothing installed at $DTM_HOME"
fi
