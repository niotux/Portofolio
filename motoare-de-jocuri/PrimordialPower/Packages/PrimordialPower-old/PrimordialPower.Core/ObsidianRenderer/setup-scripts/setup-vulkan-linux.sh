#!/bin/bash
# Setup IntelliSense for Vulkan on Linux
set -e
echo "========================================"
echo "Setting up IntelliSense for Vulkan (Linux)"
echo "========================================"
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
if [ -z "$VULKAN_SDK" ]; then
  echo "WARNING: VULKAN_SDK environment variable not set!"
  echo "Using default paths: /usr/include"
fi
cat > "${SCRIPT_DIR}/../.vscode/c_cpp_properties.json" << 'EOF'
{
  "configurations": [
    {
      "name": "Linux",
      "includePath": [
        "${workspaceFolder}/src",
        "${workspaceFolder}/lib",
        "${env:VULKAN_SDK}/include",
        "/usr/include"
      ],
      "defines": [
        "USE_VULKAN",
        "RENDERER_API_VULKAN"
      ],
      "compilerPath": "/usr/bin/gcc",
      "cStandard": "c17",
      "cppStandard": "c++17",
      "intelliSenseMode": "linux-gcc-x64"
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
