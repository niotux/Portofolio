#!/bin/bash
# Build script for Linux
# This script configures and builds the project using CMake

set -e  # Exit on error

# Detect architecture
if [ "$(uname -m)" = "x86_64" ]; then
  ARCH="x64"
else
  ARCH="x86"
fi

BUILD_DIR="build-linux-${ARCH}"

# Check if we should build Debug or Release (default: Release)
BUILD_CONFIG="${1:-Release}"

echo "========================================"
echo "Configuring build for Linux ${ARCH}..."
echo "========================================"

cmake -B ${BUILD_DIR} \
  -DCMAKE_BUILD_TYPE=${BUILD_CONFIG}

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
