#!/usr/bin/env bash
# Rebuild the retained source project in this folder; never check out another version inside it.
set -euo pipefail
project="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ "$#" -ne 0 || ! -f "$project/RELEASED" ]]; then
  echo "Run this script without arguments from a retained HH project." >&2
  exit 1
fi
if [[ -e "$project/runtime/Person" ]]; then
  echo "The retained executable is already present; it was kept unchanged."
  exit 0
fi
if [[ -n "$(git -C "$project" status --porcelain --untracked-files=all)" ]]; then
  echo "The retained source checkout has changes. Resolve them before rebuilding." >&2
  exit 1
fi
exec bash "$project/publish.sh"
