@echo off
REM Setup IntelliSense for DirectX 12 on Windows

setlocal enabledelayedexpansion

echo ========================================
echo Setting up IntelliSense for DirectX 12 (Windows)
echo ========================================

set CPP_PROPERTIES="%~dp0..\.vscode\c_cpp_properties.json"

REM Create c_cpp_properties.json with DirectX 12 configuration
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
echo         "USE_DIRECTX12",
echo         "RENDERER_API_DX12",
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
echo DirectX 12 IntelliSense configured successfully!
echo.
echo Please:
echo 1. Reload VS Code window (Ctrl+Shift+P -^> "Developer: Reload Window")
echo 2. Edit renderer.config.json to enable DirectX 12 if needed
echo 3. Rebuild your project
echo ========================================

endlocal
