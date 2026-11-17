@echo off
REM Setup IntelliSense for Vulkan on Windows

setlocal enabledelayedexpansion

echo ========================================
echo Setting up IntelliSense for Vulkan (Windows)
echo ========================================

set CPP_PROPERTIES="%~dp0..\.vscode\c_cpp_properties.json"

REM Check if VULKAN_SDK is set
if not defined VULKAN_SDK (
  echo ERROR: VULKAN_SDK environment variable not set!
  echo Please install Vulkan SDK and set VULKAN_SDK environment variable.
  exit /b 1
)

echo VULKAN_SDK: %VULKAN_SDK%

REM Create c_cpp_properties.json with Vulkan configuration
(
echo {
echo   "configurations": [
echo     {
echo       "name": "Win32",
echo       "includePath": [
echo         "${workspaceFolder}/src",
echo         "${workspaceFolder}/lib",
echo         "${env:VULKAN_SDK}/Include"
echo       ],
echo       "defines": [
echo         "USE_VULKAN",
echo         "RENDERER_API_VULKAN",
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
echo Vulkan IntelliSense configured successfully!
echo.
echo Please:
echo 1. Reload VS Code window (Ctrl+Shift+P -^> "Developer: Reload Window")
echo 2. Edit renderer.config.json to enable Vulkan if needed
echo 3. Rebuild your project
echo ========================================

endlocal
