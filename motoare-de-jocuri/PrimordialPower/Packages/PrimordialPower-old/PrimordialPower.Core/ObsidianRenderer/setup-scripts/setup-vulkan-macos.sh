#!/bin/bash
# Setup IntelliSense for Vulkan on macOS
set -e
echo "========================================"
echo "Setting up IntelliSense for Vulkan (macOS)"
echo "========================================"
if [ -z "$VULKAN_SDK" ]; then
  echo "ERROR: VULKAN_SDK environment variable not set!"
  exit 1
fi
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cat > "${SCRIPT_DIR}/../.vscode/c_cpp_properties.json" << 'EOF'
{
  "configurations": [
    {
      "name": "macOS",
      "includePath": [
        "${workspaceFolder}/src",
        "${workspaceFolder}/lib",
        "${env:VULKAN_SDK}/include"
      ],
      "defines": [
        "USE_VULKAN",
        "RENDERER_API_VULKAN"
      ],
      "compilerPath": "/usr/bin/clang",
      "cStandard": "c17",
      "cppStandard": "c++17",
      "intelliSenseMode": "macos-clang-x64"
    }
  ],
  "version": 4
}
EOF
echo ""
echo "========================================"
echo "Vulkan IntelliSense configured successfully!"
echo ""
echo "Please:"
echo "1. Reload VS Code window"
echo "2. Edit renderer.config.json to enable Vulkan if needed"
echo "3. Rebuild your project"
echo "========================================"
