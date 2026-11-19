#!/usr/bin/env bash
# Copied from ai-factory scripts/build-actor.sh: the ai-factory builders run the product's own scripts/package-actor.sh.
set -euo pipefail

# Deterministic release entry point for a .NET actor. It updates the project's bootstrap version,
# publishes the requested project, and prints machine-readable release metadata for a future build
# actor or CI job. Artifactory publication remains a separate authenticated step.
usage() {
  printf 'usage: %s --project path/to/project.csproj --version-file path/to/VERSION.md [--output DIR] [--major N] [--minor N] [--build N]\n' "$0" >&2
  exit 2
}

PROJECT=""
VERSION_FILE=""
OUTPUT=""
VERSION_ARGS=()
while [ $# -gt 0 ]; do
  case "$1" in
    --project) PROJECT=${2:-}; shift 2 ;;
    --version-file) VERSION_FILE=${2:-}; shift 2 ;;
    --output) OUTPUT=${2:-}; shift 2 ;;
    --major|--minor|--build)
      [ $# -ge 2 ] || usage
      VERSION_ARGS+=("$1" "$2")
      shift 2
      ;;
    --help|-h) usage ;;
    *) usage ;;
  esac
done

[ -n "$PROJECT" ] && [ -n "$VERSION_FILE" ] || usage
[ -n "$OUTPUT" ] || OUTPUT="artifacts/$(basename "$PROJECT" .csproj)"

version=$(bash "$(dirname "$0")/bump-actor-build.sh" "${VERSION_ARGS[@]}" "$VERSION_FILE")
# The CLI reports this version (pi-sharp --version); its csproj only holds the fallback for local builds.
# Self-contained (digital-scribe, 2026-10-30: "pi-sharp should carry it's own... be self-contained"): the build carries its
# own .NET, so an update of the box's .NET can never pull files from under a running seat (strata's overnight Ubuntu update
# left the running seats unable to load System.Formats.Tar). `dotnet pi-sharp.dll` still runs it, on the .NET it carries.
dotnet publish "$PROJECT" -c Release -r linux-x64 --self-contained true -o "$OUTPUT" -p:Version="$version" -p:InformationalVersion="$version"
printf 'actor_version=%s\nproject=%s\noutput=%s\n' "$version" "$PROJECT" "$OUTPUT"
