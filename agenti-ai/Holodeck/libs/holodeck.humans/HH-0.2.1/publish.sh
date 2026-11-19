#!/usr/bin/env bash
# Build only this source project. Version projects are direct siblings.
set -euo pipefail
project="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ "$#" -ne 0 ]]; then
  echo "Usage: bash publish.sh (from the desired HH project)" >&2
  exit 1
fi
if [[ -f "$project/RELEASED" && -e "$project/runtime/Person" ]]; then
  echo "This retained version already has its executable; it was kept unchanged."
  exit 0
fi
staging="$(mktemp -d "$project/.publish.XXXXXX")"
trap 'rm -rf "$staging"' EXIT
cd "$project"
dotnet restore src/Humans/Humans.csproj --runtime linux-x64 --locked-mode
dotnet publish src/Humans/Humans.csproj --configuration Release --runtime linux-x64 --self-contained true --no-restore --output "$staging/runtime" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:DebugType=none -p:ContinuousIntegrationBuild=true
test -x "$staging/runtime/Person"
mkdir -p "$project/runtime"
for artifact in "$staging/runtime"/*; do
  mv -f "$artifact" "$project/runtime/$(basename "$artifact")"
done
echo "Built $(cat "$project/VERSION") in $project/runtime"
