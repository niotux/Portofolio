#!/bin/bash
set -e

MODE="$1"
CSPROJ="$EMPIRE_TARGET_SRC/$EMPIRE_TARGET_NAME.csproj"

translate_rid() {
  case "$1" in
    windows-x86_64)  echo "win-x64"     ;;
    windows-aarch64) echo "win-arm64"   ;;
    linux-x86_64)    echo "linux-x64"   ;;
    linux-aarch64)   echo "linux-arm64" ;;
    macos-x86_64)    echo "osx-x64"     ;;
    macos-aarch64)   echo "osx-arm64"   ;;
    *)               echo "$1"          ;;  # pass through unknown, let dotnet complain
  esac
}

# Write a .csproj only — no .cs files touched
generate_csproj() {
  local type="$1"   # exe | lib
  [ -f "$CSPROJ" ] && return

  mkdir -p "$EMPIRE_TARGET_SRC"

  if [ "$type" = "exe" ]; then
    cat > "$CSPROJ" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
EOF
  else
    cat > "$CSPROJ" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
EOF
  fi

  echo "[csharp] generated $CSPROJ"
}

DRID=$(translate_rid "$EMPIRE_RID")

case "$MODE" in
  -exe)
    generate_csproj exe
    dotnet publish "$CSPROJ" $EMPIRE_FLAGS_CONFIG \
      --runtime "$DRID" \
      --no-self-contained \
      --output "$EMPIRE_TARGET_OUT"
    ;;
  -lib)
    generate_csproj lib
    dotnet build "$CSPROJ" $EMPIRE_FLAGS_CONFIG \
      --output "$EMPIRE_TARGET_OUT" \
      $EMPIRE_FLAGS_LINKING
    ;;
  -nuget)
    generate_csproj lib
    dotnet pack "$CSPROJ" $EMPIRE_FLAGS_CONFIG \
      --output "$EMPIRE_TARGET_OUT" \
      $EMPIRE_FLAGS_LINKING
    ;;
  *)
    echo "Usage: Build.sh [-exe | -lib | -nuget]"
    exit 1
    ;;
esac
