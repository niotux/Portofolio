#!/bin/bash
# Setup IntelliSense for OpenGL on macOS
set -e
echo "========================================"
echo "Setting up IntelliSense for OpenGL (macOS)"
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
        "/System/Library/Frameworks/OpenGL.framework/Headers"
      ],
      "defines": [
        "USE_OPENGL",
        "RENDERER_API_OPENGL"
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
echo "OpenGL IntelliSense configured successfully!"
echo ""
echo "Please:"
echo "1. Reload VS Code window"
echo "2. Edit renderer.config.json to enable OpenGL if needed"
echo "3. Rebuild your project"
echo "========================================"
