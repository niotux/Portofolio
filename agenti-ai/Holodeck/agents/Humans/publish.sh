#!/usr/bin/env bash
# Compatibility entry point for explicitly publishing the development Person.
set -euo pipefail
source_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd "$source_dir/../.." && pwd)"
exec bash "$project_root/libs/holodeck.humans/HH-0.1.0/scripts/publish-legacy.sh" "$project_root/libs/holodeck.show-writing" "$@"
