#!/usr/bin/env bash
# Builder downloads hook (ai-factory's BuildRunner invokes it when the deploy dir holds one,
# with the repo root as its working directory):
#   bash deploy/downloads.sh --component pi-sharp --version V --output DIR
# Packages ONLY the native sidecar (contract v3 §8 as corrected by tl-producer-builder-correction):
# package-actor.sh with --native-only, whose artifact holds bin/, deploy/, install.sh, VERSION
# with no browser/ or browsers/, so the extracted archive's unchanged ./install.sh is the
# zero-option native invocation. The builder already packages, uploads and registers the
# browser-bearing full primary before this hook runs: this hook never generates, overwrites,
# lists or uploads it. It writes exactly one downloads.txt line, and the builder uploads the
# file and registers it with its own immutable sha on the same release.
set -euo pipefail

usage() {
  printf 'usage: %s --component pi-sharp --version major.minor.build --output DIR\n' "$0" >&2
  exit 2
}

COMPONENT='' VERSION='' OUTPUT=''
while [ $# -gt 0 ]; do
  case "$1" in
    --component) COMPONENT=${2:-}; shift 2 ;;
    --version) VERSION=${2:-}; shift 2 ;;
    --output) OUTPUT=${2:-}; shift 2 ;;
    *) usage ;;
  esac
done
[ -n "$COMPONENT" ] && [ -n "$VERSION" ] && [ -n "$OUTPUT" ] || usage
[ "$COMPONENT" = pi-sharp ] || { printf 'downloads.sh packages the pi-sharp component only\n' >&2; exit 2; }
[[ "$VERSION" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)$ ]] || usage

mkdir -p "$OUTPUT"
bash "$(dirname "$0")/../scripts/package-actor.sh" --project src/PiSharp.Cli/PiSharp.Cli.csproj \
  --version-file src/PiSharp.Cli/VERSION.md --version "$VERSION" \
  --deploy deploy --output "$OUTPUT/pi-sharp-$VERSION-native.tar.gz" --native-only
printf 'linux-x64 pi-sharp-%s-native.tar.gz\n' "$VERSION" > "$OUTPUT/downloads.txt"
