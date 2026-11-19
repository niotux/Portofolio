#!/usr/bin/env bash
set -euo pipefail

# Packages a .NET actor as an Artifactory artifact:
#   bin/        dotnet publish output (the version is stamped from --version)
#   deploy/     the product's deploy folder
#   install.sh  <deploy>/artifact-install.sh, the install_entry pi-update runs as root
#   VERSION     the version
# The one sha256 of the tarball therefore covers the installer as well as the payload.
usage() {
  printf 'usage: %s --project X.csproj --version-file VERSION.md --version major.minor.build --deploy DIR --output out.tar.gz [--native-only]\n' "$0" >&2
  exit 2
}

PROJECT='' VERSION_FILE='' VERSION='' DEPLOY='' OUTPUT='' NATIVE_ONLY=0
while [ $# -gt 0 ]; do
  case "$1" in
    --project) PROJECT=${2:-}; shift 2 ;;
    --version-file) VERSION_FILE=${2:-}; shift 2 ;;
    --version) VERSION=${2:-}; shift 2 ;;
    --deploy) DEPLOY=${2:-}; shift 2 ;;
    --output) OUTPUT=${2:-}; shift 2 ;;
    --native-only) NATIVE_ONLY=1; shift ;;
    *) usage ;;
  esac
done
[ -n "$PROJECT" ] && [ -n "$VERSION_FILE" ] && [ -n "$DEPLOY" ] && [ -n "$OUTPUT" ] || usage
[[ "$VERSION" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)$ ]] || usage
[ -f "$DEPLOY/artifact-install.sh" ] || { printf '%s/artifact-install.sh is missing: an artifact must carry its installer\n' "$DEPLOY" >&2; exit 1; }

stage=$(mktemp -d)
trap 'rm -rf -- "$stage"' EXIT
bash "$(dirname "$0")/build-actor.sh" --project "$PROJECT" --version-file "$VERSION_FILE" --output "$stage/bin" \
  --major "${BASH_REMATCH[1]}" --minor "${BASH_REMATCH[2]}" --build "${BASH_REMATCH[3]}"
cp -a "$DEPLOY" "$stage/deploy"
# Browser/playwright is part of the CLI seat artifact only; --native-only skips it, so the
# CLI project also packages the browserless native sidecar artifact (contract v3).
if [ "$(basename "$PROJECT")" = "PiSharp.Cli.csproj" ] && [ "$NATIVE_ONLY" -eq 0 ]; then
  # Self-contained like the CLI: the browser host runs as long as its seat does.
  dotnet publish tools/PiSharp.Browser/PiSharp.Browser.csproj -c Release -r linux-x64 --self-contained true \
    -o "$stage/browser" -p:Version="$VERSION" -p:InformationalVersion="$VERSION"
  # The house network has no internet: install into a cache kept across builds (a no-op when the browser
  # is already there) and copy it in; seed the cache from an installed /opt/pi-sharp-browser/*/browsers.
  cache="${PI_SHARP_BROWSERS_CACHE:-$HOME/.cache/pi-sharp-browsers}"
  mkdir -p "$cache"
  PLAYWRIGHT_BROWSERS_PATH="$cache" "$stage/browser/PiSharp.Browser" install chromium --only-shell
  mkdir -p "$stage/browsers" && cp -a "$cache/." "$stage/browsers/"
fi
install -m 0755 "$DEPLOY/artifact-install.sh" "$stage/install.sh"
printf '%s\n' "$VERSION" > "$stage/VERSION"
tar -C "$stage" -czf "$OUTPUT" .
printf 'artifact=%s\nversion=%s\n' "$OUTPUT" "$VERSION"
