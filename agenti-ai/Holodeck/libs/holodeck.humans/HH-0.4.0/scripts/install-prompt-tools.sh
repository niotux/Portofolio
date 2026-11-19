#!/usr/bin/env bash
# Install the Humans prompt MCP server for this user. Every MCP client on the machine can run the installed program.
set -euo pipefail
project="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
target="${1:-$HOME/.local/share/humans-prompts}"
if [[ "$#" -gt 1 ]]; then
  echo "Usage: bash scripts/install-prompt-tools.sh [install-directory]" >&2
  exit 1
fi
staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT
cd "$project"
dotnet restore src/Humans.PromptTools/Humans.PromptTools.csproj --runtime linux-x64 --locked-mode
dotnet publish src/Humans.PromptTools/Humans.PromptTools.csproj --configuration Release --runtime linux-x64 --self-contained true --no-restore --output "$staging" -p:PublishSingleFile=true -p:DebugType=none
test -x "$staging/humans-prompts"
mkdir -p "$target"
# A local appsettings.json is the user's configuration; keep it across reinstalls.
if [[ -f "$target/appsettings.json" ]]; then
  rm "$staging/appsettings.json"
fi
cp -f "$staging"/* "$target/"
echo "Installed $target/humans-prompts"
