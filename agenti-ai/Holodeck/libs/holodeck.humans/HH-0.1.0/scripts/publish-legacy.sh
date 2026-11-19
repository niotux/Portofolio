#!/usr/bin/env bash
# Explicitly refresh legacy paths from THIS version; normal builds do not touch them.
set -euo pipefail
project="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
library="$(cd "$project/.." && pwd)"
legacy="${1:-}"
if [[ "$#" -gt 1 || "${1:-}" == "--help" ]]; then
  echo "Usage: bash scripts/publish-legacy.sh [show-writing-library-directory]"
  exit 0
fi
if [[ ! -f "$library/Library.json" || "$(basename "$project")" != HH-* ]]; then
  echo "The legacy publisher requires this project's outer Humans library." >&2
  exit 1
fi
if [[ -n "$legacy" ]]; then
  python3 - "$legacy/Library.json" <<'CHECK'
import json, sys
with open(sys.argv[1]) as manifest:
    if json.load(manifest).get('Id') != 'holodeck.show-writing':
        raise SystemExit('The compatibility destination must be the Show Writing library.')
CHECK
fi
bash "$project/publish.sh"
staging="$(mktemp -d "$library/.publish.XXXXXX")"
trap 'rm -rf "$staging"' EXIT
mkdir -p "$library/runtime"
cp "$project/runtime/Person" "$staging/Person"
mv -f "$staging/Person" "$library/runtime/Person"
if [[ -n "$legacy" ]]; then
  mkdir -p "$legacy/runtime"
  cp "$project/runtime/Person" "$staging/ShowWriting"
  mv -f "$staging/ShowWriting" "$legacy/runtime/ShowWriting"
fi
for target in "$library" ${legacy:+"$legacy"}; do
  mkdir -p "$target/prompts"
  for prompt in "$project/prompts"/*; do
    name="$(basename "$prompt")"
    cp "$prompt" "$staging/$name"
    mv -f "$staging/$name" "$target/prompts/$name"
  done
  for name in runtime.json VERSION; do
    cp "$project/$name" "$staging/$name"
    mv -f "$staging/$name" "$target/$name"
  done
done
echo "Explicitly refreshed legacy paths from $(cat "$project/VERSION"). Existing containers were retained."
