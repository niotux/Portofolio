#!/usr/bin/env bash
set -euo pipefail



PROJECT="lan2llvm"
CC="${CC:-gcc}"

SRC_DIR="$(cd "$(dirname "$0")" && pwd)"
BIN_DIR="$SRC_DIR/bin"

CFLAGS_COMMON="-Wall -Wextra -fPIC"

OS=$(uname -s | tr '[:upper:]' '[:lower:]')
ARCH=$(uname -m)
RID="${OS}-${ARCH}"

ANSI_ERROR="\033[31m"
ANSI_WARNING="\033[33m"
ANSI_INFO="\033[32m"
ANSI_TAG="\033[90m"
ANSI_RESET="\033[0m"

# Dependencies
# Format: "<path>:stlib" or "<path>:dylib"
# Paths may be relative to this script or absolute.
# Static  -> built as a .a,  linked into the output.
# Dynamic -> built as a .so, copied to the output dir and linked.
DEPS=(
  "../../libs/liblan:stlib"
  "../../libs/libclikit:stlib"
)

DEP_INCLUDE_FLAGS=""
DEP_LINK_FLAGS=""
DEP_SHARED_COPIES=()



_log()
{
  local color="$1"
  local msg="$2"
  local tag="${3:-}"

  if [[ -n "$tag" ]]; then
    echo -e "${color}--${ANSI_RESET} $msg ${ANSI_TAG}[$tag]${ANSI_RESET}"
  else
    echo -e "${color}--${ANSI_RESET} $msg"
  fi
}

log_info()
{
  _log "$ANSI_INFO" "$@";
}

log_warning()
{
  _log "$ANSI_WARNING" "$@";
}

log_error()
{
  _log "$ANSI_ERROR" "$@";
}



get_shared_ext()
{
  case "$OS" in
    darwin)
      echo "dylib" ;;
    mingw*|msys*)
      echo "dll"   ;;
    *)
      echo "so"    ;;
  esac
}

get_shared_flags()
{
  case "$OS" in
    darwin)
      echo "-dynamiclib" ;;
    mingw*|msys*)
      echo "-shared -Wl,--out-implib,${1}/lib${PROJECT}.a" ;;
    *)
      echo "-shared" ;;
  esac
}

get_exe_ext()
{
  case "$OS" in
    mingw*|msys*)
      echo ".exe" ;;
    *)
      echo ""     ;;
  esac
}



resolve_dep_name()
{
  local dep_dir="$1"
  grep -m1 '^PROJECT=' "$dep_dir/build.sh" | cut -d'"' -f2
}

build_deps()
{
  local mode="$1"
  local config="$2"

  DEP_INCLUDE_FLAGS=""
  DEP_LINK_FLAGS=""
  DEP_SHARED_COPIES=()

  [[ ${#DEPS[@]} -eq 0 ]] && return

  local entry dep_path dep_type dep_dir dep_name dep_out ext

  for entry in "${DEPS[@]}"; do
    dep_path="${entry%%:*}"
    dep_type="${entry##*:}"
    dep_dir="$(cd "$SRC_DIR/$dep_path" && pwd)"
    dep_name=$(resolve_dep_name "$dep_dir")
    dep_out="$dep_dir/bin/${mode}-${RID}"

    log_info "Building dep" "$dep_name ($dep_type)"

    case "$dep_type" in
      stlib)
        bash "$dep_dir/build.sh" "$config" -stlib
        DEP_INCLUDE_FLAGS+=" -I$dep_dir"
        DEP_LINK_FLAGS+=" $dep_out/lib${dep_name}.a"
        ;;
      dylib)
        bash "$dep_dir/build.sh" "$config" -dylib
        ext=$(get_shared_ext)
        DEP_INCLUDE_FLAGS+=" -I$dep_dir"
        DEP_LINK_FLAGS+=" -L$dep_out -l$dep_name"
        DEP_SHARED_COPIES+=("$dep_out/lib${dep_name}.${ext}")
        ;;
      *)
        log_error "Unknown dep type" "$dep_type"
        exit 1 ;;
    esac
  done
}

copy_shared_deps()
{
  local out_dir="$1"

  [[ ${#DEP_SHARED_COPIES[@]} -eq 0 ]] && return

  local lib
  for lib in "${DEP_SHARED_COPIES[@]}"; do
    log_info "Copying shared dep" "$(basename "$lib")"
    cp "$lib" "$out_dir/"
  done
}



build_objects()
{
  local out_dir="$1"
  local extra_flags="$2"
  local obj_dir="$out_dir/obj"

  mkdir -p "$obj_dir"

  for src in "$SRC_DIR"/*.c; do
    local obj="$obj_dir/$(basename "${src%.c}").o"
    log_info "Compiling" "$(basename "$src")"
    $CC $CFLAGS_COMMON $extra_flags $DEP_INCLUDE_FLAGS -I"$SRC_DIR" -c "$src" -o "$obj"
  done
}

build_exe()
{
  local out_dir="$1"
  local obj_dir="$out_dir/obj"
  local ext; ext=$(get_exe_ext)
  local out_file="$out_dir/${PROJECT}${ext}"

  log_info "Linking" "${PROJECT}${ext}"
  $CC -o "$out_file" "$obj_dir"/*.o $DEP_LINK_FLAGS -Wl,-rpath,'$ORIGIN'
}

build_static()
{
  local out_dir="$1"
  local obj_dir="$out_dir/obj"
  local out_file="$out_dir/lib${PROJECT}.a"

  log_info "Linking" "lib${PROJECT}.a"
  ar rcs "$out_file" "$obj_dir"/*.o
}

build_shared()
{
  local out_dir="$1"
  local obj_dir="$out_dir/obj"
  local ext; ext=$(get_shared_ext)
  local flags; flags=$(get_shared_flags "$out_dir")
  local out_file="$out_dir/lib${PROJECT}.${ext}"

  log_info "Linking" "lib${PROJECT}.${ext}"
  $CC $flags -o "$out_file" "$obj_dir"/*.o $DEP_LINK_FLAGS
}



build()
{
  local mode="$1"
  local extra_flags="$2"
  local artifact="$3"
  local out_dir="$BIN_DIR/${mode}-${RID}"

  log_info "Building $PROJECT" "$mode $RID"

  build_objects "$out_dir" "$extra_flags"

  case "$artifact" in
    exe)
      build_exe    "$out_dir" ;;
    stlib)
      build_static "$out_dir" ;;
    dylib)
      build_shared "$out_dir" ;;
    all)
      build_exe    "$out_dir"
      build_static "$out_dir"
      build_shared "$out_dir" ;;
    *)
      log_error "Invalid artifact type" "$artifact"
      exit 1 ;;
  esac

  copy_shared_deps "$out_dir"

  log_info "Output" "$out_dir"
  echo
}

clean()
{
  rm -rf "$BIN_DIR"
  log_info "Cleaned" "$PROJECT"
}



# Configs

CFLAGS_DEBUG="-g -O0 -DDEBUG"
config_debug()
{
  build_deps "debug" "debug"
  build "debug" "$CFLAGS_DEBUG" "$1"
}

CFLAGS_RELEASE="-O2 -DNDEBUG"
config_release()
{
  build_deps "release" "release"
  build "release" "$CFLAGS_RELEASE" "$1"
}

# CFLAGS_ASAN="-g -O1 -fsanitize=address,undefined"
# config_asan() { build "asan" "$CFLAGS_ASAN" "$1"; }



usage()
{
  local configs
  configs=$(declare -F | awk '$3 ~ /^config_/ { gsub(/^config_/, "", $3); printf "%s|", $3 }')
  echo "Usage:"
  echo "  $0 <${configs%|}> [-exe|-stlib|-dylib]"
  echo "  $0 clean"
}

CONFIG="${1:-}"
ARTIFACT="${2:-}"
ARTIFACT="${ARTIFACT#-}"
ARTIFACT="${ARTIFACT:-all}"

case "$CONFIG" in
  clean)
    clean ;;
  "")
    usage; exit 1 ;;
  *)
    if declare -f "config_${CONFIG}" > /dev/null 2>&1; then
      "config_${CONFIG}" "$ARTIFACT"
    else
      log_error "Unknown config" "$CONFIG"
      usage; exit 1
    fi ;;
esac