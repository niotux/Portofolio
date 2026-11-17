@echo off
REM Build script for Windows
REM This script configures and builds the project using CMake

setlocal enabledelayedexpansion

REM Detect architecture
if "%PROCESSOR_ARCHITECTURE%"=="AMD64" (
  set ARCH=x64
  set CMAKE_ARCH=x64
) else (
  set ARCH=x86
  set CMAKE_ARCH=Win32
)

set BUILD_DIR=build-win-%ARCH%

REM Check if we should build Debug or Release (default: both)
set BUILD_CONFIG=%1
if "%BUILD_CONFIG%"=="" set BUILD_CONFIG=both

echo ========================================
echo Configuring build for Windows %ARCH%...
echo ========================================

cmake -B %BUILD_DIR% ^
  -G "Visual Studio 17 2022" ^
  -A %CMAKE_ARCH%

if %ERRORLEVEL% neq 0 (
  echo Configuration failed!
  exit /b %ERRORLEVEL%
)

if "%BUILD_CONFIG%"=="both" (
  echo.
  echo ========================================
  echo Building Release...
  echo ========================================
  cmake --build %BUILD_DIR% --config Release

  if %ERRORLEVEL% neq 0 (
    echo Release build failed!
    exit /b %ERRORLEVEL%
  )

  echo.
  echo ========================================
  echo Building Debug...
  echo ========================================
  cmake --build %BUILD_DIR% --config Debug

  if %ERRORLEVEL% neq 0 (
    echo Debug build failed!
    exit /b %ERRORLEVEL%
  )

  echo.
  echo ========================================
  echo Build completed successfully!
  echo Release output: %BUILD_DIR%\bin\Release\
  echo Debug output: %BUILD_DIR%\bin\Debug\
  echo ========================================
) else (
  echo.
  echo ========================================
  echo Building %BUILD_CONFIG%...
  echo ========================================
  cmake --build %BUILD_DIR% --config %BUILD_CONFIG%

  if %ERRORLEVEL% neq 0 (
    echo %BUILD_CONFIG% build failed!
    exit /b %ERRORLEVEL%
  )

  echo.
  echo ========================================
  echo Build completed successfully!
  echo Output: %BUILD_DIR%\bin\%BUILD_CONFIG%\
  echo ========================================
)

endlocal
