#!/usr/bin/env bash
set -euo pipefail

source_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd "$source_dir/../.." && pwd)"
output="$project_root/libs/holodeck.creative-studio/runtime"
staging="$(mktemp -d "$project_root/libs/holodeck.creative-studio/.publish.XXXXXX")"
trap 'rm -rf "$staging"' EXIT

dotnet restore "$source_dir/CreativeStudio.csproj" --runtime linux-x64 --locked-mode
dotnet publish "$source_dir/CreativeStudio.csproj" --configuration Release --runtime linux-x64 --self-contained true --no-restore --output "$staging" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:DebugType=none -p:ContinuousIntegrationBuild=true
mkdir -p "$output"
for artifact in "$staging"/*; do
  mv -f "$artifact" "$output/"
done
test -x "$output/CreativeStudio"
