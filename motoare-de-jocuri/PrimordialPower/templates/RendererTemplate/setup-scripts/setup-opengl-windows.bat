@echo off
REM Setup IntelliSense for OpenGL on Windows

setlocal enabledelayedexpansion

echo ========================================
echo Setting up IntelliSense for OpenGL (Windows)
echo ========================================

set CPP_PROPERTIES="%~dp0..\.vscode\c_cpp_properties.json"

REM Create c_cpp_properties.json with OpenGL configuration
(
echo {
echo   "configurations": [
echo     {
echo       "name": "Win32",
echo       "includePath": [
echo         "${workspaceFolder}/src",
echo         "${workspaceFolder}/lib"
echo       ],
echo       "defines": [
echo         "USE_OPENGL",
echo         "RENDERER_API_OPENGL",
echo         "BUILDING_DLL",
echo         "_DEBUG",
echo         "UNICODE",
echo         "_UNICODE"
echo       ],
echo       "windowsSdkVersion": "10.0.22000.0",
echo       "compilerPath": "cl.exe",
echo       "cStandard": "c17",
echo       "cppStandard": "c++17",
echo       "intelliSenseMode": "windows-msvc-x64"
echo     }
echo   ],
echo   "version": 4
echo }
) > %CPP_PROPERTIES%

echo.
echo ========================================
echo OpenGL IntelliSense configured successfully!
echo.
echo Please:
echo 1. Reload VS Code window (Ctrl+Shift+P -^> "Developer: Reload Window")
echo 2. Edit renderer.config.json to enable OpenGL if needed
echo 3. Rebuild your project
echo ========================================

endlocal
