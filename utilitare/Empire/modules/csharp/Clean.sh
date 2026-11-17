#!/bin/bash
set -e

MODE="$1"

case "$MODE" in
  -all)
    # Aggressively remove the entire base output folder
    # This covers bin/, obj/, and any other generated output
    if [ -d "$EMPIRE_TARGET_OUTBASE" ]; then
        rm -rf "$EMPIRE_TARGET_OUTBASE"
    fi
    ;;
  -cfg)
    # Aggressively remove only the specific config outputs
    rm -rf "$EMPIRE_TARGET_ART" "$EMPIRE_TARGET_OUT"
    ;;
  *)
    echo "Usage: Clean.sh [-all | -cfg]"
    exit 1
    ;;
esac