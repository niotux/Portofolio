#!/bin/bash
# Build script for macOS
# This script configures and builds the project using CMake

set -e  # Exit on error

# Detect architecture
if [ "$(uname -m)" = "arm64" ]; then
  ARCH="arm64"
  CMAKE_ARCH="arm64"
else
  ARCH="x64"
  CMAKE_ARCH="x86_64"
fi

BUILD_DIR="build-macos-${ARCH}"

# Check if we should build Debug or Release (default: Release)
BUILD_CONFIG="${1:-Release}"

echo "========================================"
echo "Configuring build for macOS ${ARCH}..."
echo "========================================"

cmake -B ${BUILD_DIR} \
  -DCMAKE_BUILD_TYPE=${BUILD_CONFIG} \
  -DCMAKE_OSX_ARCHITECTURES=${CMAKE_ARCH}

echo ""
echo "========================================"
echo "Building ${BUILD_CONFIG}..."
echo "========================================"

cmake --build ${BUILD_DIR} --config ${BUILD_CONFIG}

echo ""
echo "========================================"
echo "Build completed successfully!"
echo "Output: ${BUILD_DIR}/bin/${BUILD_CONFIG}/"
echo "========================================"
