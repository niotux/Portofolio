#!/bin/bash
# Setup IntelliSense for Metal on macOS
set -e
echo "========================================"
echo "Setting up IntelliSense for Metal (macOS)"
echo "========================================"
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cat > "${SCRIPT_DIR}/../.vscode/c_cpp_properties.json" << 'EOF'
{
  "configurations": [
    {
      "name": "macOS",
      "includePath": [
        "${workspaceFolder}/src",
        "${workspaceFolder}/lib",
        "/System/Library/Frameworks/Metal.framework/Headers",
        "/System/Library/Frameworks/MetalKit.framework/Headers"
      ],
      "defines": [
        "USE_METAL",
        "RENDERER_API_METAL"
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
echo "Metal IntelliSense configured successfully!"
echo ""
echo "Please:"
echo "1. Reload VS Code window"
echo "2. Edit renderer.config.json to enable Metal if needed"
echo "3. Rebuild your project"
echo "========================================"
