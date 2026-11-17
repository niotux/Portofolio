#!/usr/bin/env bash
EMPIRE_SRC="$(cd "$(dirname "$0")" && pwd)"
EMPIRE_BIN="/usr/local/bin/empire"
 
case "${1:-}" in
  install)
    chmod +x "$EMPIRE_SRC/empire.py"
    sudo ln -sf "$EMPIRE_SRC/empire.py" "$EMPIRE_BIN"
    echo "-- Linked  $EMPIRE_BIN -> $EMPIRE_SRC/empire.py"
    echo "-- Done. try: empire --help"
    ;;
  uninstall)
    sudo rm -f "$EMPIRE_BIN"
    echo "-- Removed $EMPIRE_BIN"
    echo "-- Done"
    ;;
  *)
    echo "-- Usage: ./install.sh [install|uninstall]"
    exit 1
    ;;
esac

