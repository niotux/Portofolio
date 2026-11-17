#!/bin/bash
# Setup IntelliSense for OpenGL on Linux
set -e
echo "========================================"
echo "Setting up IntelliSense for OpenGL (Linux)"
echo "========================================"
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cat > "${SCRIPT_DIR}/../.vscode/c_cpp_properties.json" << 'EOF'
{
  "configurations": [
    {
      "name": "Linux",
      "includePath": [
        "${workspaceFolder}/src",
        "${workspaceFolder}/lib",
        "/usr/include",
        "/usr/include/GL"
      ],
      "defines": [
        "USE_OPENGL",
        "RENDERER_API_OPENGL"
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
echo "OpenGL IntelliSense configured successfully!"
echo ""
echo "Please:"
echo "1. Reload VS Code window"
echo "2. Edit renderer.config.json to enable OpenGL if needed"
echo "3. Rebuild your project"
echo "========================================"
