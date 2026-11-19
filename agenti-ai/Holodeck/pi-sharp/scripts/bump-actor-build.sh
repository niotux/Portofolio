#!/usr/bin/env bash
set -euo pipefail

# Increment the build component in a project/repository's Markdown bootstrap version.
# Usage: scripts/bump-actor-build.sh [--major N] [--minor N] [--build N] path/to/VERSION.md
major=""
minor=""
build=""
VERSION_FILE=""
while [ $# -gt 0 ]; do
  case "$1" in
    --major|--minor|--build)
      [ $# -ge 2 ] || { printf '%s requires a value\n' "$1" >&2; exit 1; }
      case "$1" in
        --major) major="$2" ;;
        --minor) minor="$2" ;;
        --build) build="$2" ;;
      esac
      shift 2
      ;;
    --help|-h)
      printf 'usage: %s [--major N] [--minor N] [--build N] path/to/VERSION.md\n' "$0"
      exit 0
      ;;
    -*) printf 'unknown option: %s\n' "$1" >&2; exit 1 ;;
    *)
      [ -z "$VERSION_FILE" ] || { printf 'only one VERSION.md path is allowed\n' >&2; exit 1; }
      VERSION_FILE="$1"
      shift
      ;;
  esac
done
[ -n "$VERSION_FILE" ] || { printf 'usage: %s [--major N] [--minor N] [--build N] path/to/VERSION.md\n' "$0" >&2; exit 1; }

version=$(grep -Eo '[0-9]+\.[0-9]+\.[0-9]+' "$VERSION_FILE" | head -n 1 || true)
if [[ ! "$version" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)$ ]]; then
  printf 'version file must contain major.minor.build: %s\n' "$VERSION_FILE" >&2
  exit 1
fi

major=${major:-${BASH_REMATCH[1]}}
minor=${minor:-${BASH_REMATCH[2]}}
build=${build:-$((BASH_REMATCH[3] + 1))}
for component in "$major" "$minor" "$build"; do
  [[ "$component" =~ ^[0-9]+$ ]] || { printf 'version components must be non-negative integers\n' >&2; exit 1; }
done
next="$major.$minor.$build"
sed -E -i "0,/[0-9]+\.[0-9]+\.[0-9]+/s//${next}/" "$VERSION_FILE"
printf '%s\n' "$next"
