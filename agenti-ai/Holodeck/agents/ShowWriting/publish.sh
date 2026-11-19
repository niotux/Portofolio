#!/usr/bin/env bash
# Compatibility entry point; Person now belongs to Holodeck Humans.
set -euo pipefail
source_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec bash "$source_dir/../../libs/holodeck.humans/HH-0.1.0/scripts/publish-legacy.sh" "$source_dir/../../libs/holodeck.show-writing" "$@"
