# Minimum CMake version (3.19+ required for JSON support)
cmake_minimum_required(VERSION 3.19)

# Load renderer configuration to get project metadata
file(READ "${CMAKE_CURRENT_SOURCE_DIR}/renderer.config.json" RENDERER_CONFIG_CONTENT)
string(JSON PROJECT_NAME GET ${RENDERER_CONFIG_CONTENT} renderer name)
string(JSON PROJECT_VERSION GET ${RENDERER_CONFIG_CONTENT} renderer version)
string(JSON PROJECT_DESCRIPTION GET ${RENDERER_CONFIG_CONTENT} renderer description)

# Load build configuration
file(READ "${CMAKE_CURRENT_SOURCE_DIR}/build.config.json" BUILD_CONFIG_CONTENT)
string(JSON BUILD_OUTPUT_DIR GET ${BUILD_CONFIG_CONTENT} build output_directory)
string(JSON BUILD_BIN_DIR GET ${BUILD_CONFIG_CONTENT} build bin_directory)
string(JSON BUILD_DEFAULT_CONFIG GET ${BUILD_CONFIG_CONTENT} build default_configuration)

# Project name and version
project(
  ${PROJECT_NAME}
  VERSION ${PROJECT_VERSION}
  DESCRIPTION "${PROJECT_DESCRIPTION}"
  LANGUAGES CXX
)

# Set C++ standard
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Export compile commands for IDEs/tools (works with Makefile/Ninja generators)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

# Platform and architecture detection
if(CMAKE_SIZEOF_VOID_P EQUAL 8)
  set(ARCH "x64")
else()
  set(ARCH "x86")
endif()

if(WIN32)
  set(PLATFORM "win")
elseif(APPLE)
  set(PLATFORM "macos")
elseif(UNIX)
  set(PLATFORM "linux")
else()
  set(PLATFORM "unknown")
endif()

# Set build configuration
if(NOT CMAKE_BUILD_TYPE)
  set(CMAKE_BUILD_TYPE "${BUILD_DEFAULT_CONFIG}" CACHE STRING "Build type" FORCE)
endif()
set(CONFIG ${CMAKE_BUILD_TYPE})

# Expand template variables in output directory paths
string(REPLACE "\${PLATFORM}" "${PLATFORM}" OUTPUT_DIR_EXPANDED "${BUILD_OUTPUT_DIR}")
string(REPLACE "\${ARCH}" "${ARCH}" OUTPUT_DIR_EXPANDED "${OUTPUT_DIR_EXPANDED}")
string(REPLACE "\${CONFIG}" "${CONFIG}" BIN_DIR_EXPANDED "${BUILD_BIN_DIR}")

# Set output directories for single-config generators (Make, Ninja)
set(OUTPUT_BASE_DIR "${CMAKE_SOURCE_DIR}/${OUTPUT_DIR_EXPANDED}")
set(OUTPUT_BIN_DIR "${OUTPUT_BASE_DIR}/${BIN_DIR_EXPANDED}")

set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${OUTPUT_BIN_DIR})
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${OUTPUT_BIN_DIR})
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${OUTPUT_BIN_DIR})

# For multi-config generators (Visual Studio, Xcode)
foreach(CONFIG_TYPE ${CMAKE_CONFIGURATION_TYPES})
  string(REPLACE "\${CONFIG}" "${CONFIG_TYPE}" BIN_DIR_CONFIG "${BUILD_BIN_DIR}")
  set(OUTPUT_BIN_DIR_CONFIG "${OUTPUT_BASE_DIR}/${BIN_DIR_CONFIG}")

  string(TOUPPER ${CONFIG_TYPE} CONFIG_UPPER)
  set(CMAKE_RUNTIME_OUTPUT_DIRECTORY_${CONFIG_UPPER} ${OUTPUT_BIN_DIR_CONFIG})
  set(CMAKE_LIBRARY_OUTPUT_DIRECTORY_${CONFIG_UPPER} ${OUTPUT_BIN_DIR_CONFIG})
  set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY_${CONFIG_UPPER} ${OUTPUT_BIN_DIR_CONFIG})
endforeach()

# Collect source files
file(GLOB_RECURSE SOURCES "src/*.cpp" "src/*.hpp")

# Create shared library (DLL on Windows, .so on Linux, .dylib on macOS)
add_library(${PROJECT_NAME} SHARED ${SOURCES})

# Include directories
target_include_directories(${PROJECT_NAME}
  PRIVATE
  src
  ${CMAKE_SOURCE_DIR}/lib
)

# Link external libraries from lib/ directory
set(LIB_DIR "${CMAKE_SOURCE_DIR}/lib")

if(EXISTS ${LIB_DIR})
  message(STATUS "Searching for libraries in: ${LIB_DIR}")

  # Find all library files recursively
  if(WIN32)
    file(GLOB_RECURSE LIB_FILES "${LIB_DIR}/*.lib")
    file(GLOB_RECURSE DLL_FILES "${LIB_DIR}/*.dll")
  elseif(APPLE)
    file(GLOB_RECURSE LIB_FILES "${LIB_DIR}/*.a" "${LIB_DIR}/*.dylib")
  else() # Linux
    file(GLOB_RECURSE LIB_FILES "${LIB_DIR}/*.a" "${LIB_DIR}/*.so")
  endif()

  # Link found libraries
  if(LIB_FILES)
    foreach(LIB_FILE ${LIB_FILES})
      get_filename_component(LIB_NAME ${LIB_FILE} NAME)
      target_link_libraries(${PROJECT_NAME} PRIVATE ${LIB_FILE})
      message(STATUS "  Linked library: ${LIB_NAME}")
    endforeach()
  else()
    message(STATUS "  No libraries found in ${LIB_DIR}")
  endif()

  # Copy DLLs to output directory on Windows
  if(WIN32 AND DLL_FILES)
    foreach(DLL_FILE ${DLL_FILES})
      get_filename_component(DLL_NAME ${DLL_FILE} NAME)

      add_custom_command(TARGET ${PROJECT_NAME} POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
          ${DLL_FILE}
          $<TARGET_FILE_DIR:${PROJECT_NAME}>
        COMMENT "Copying ${DLL_NAME} to output directory"
      )

      message(STATUS "  Will copy DLL: ${DLL_NAME}")
    endforeach()
  endif()
else()
  message(STATUS "Library directory not found: ${LIB_DIR}")
endif()

# Platform-specific configurations
if(WIN32)
  # Windows-specific: Export symbols for DLL
  target_compile_definitions(${PROJECT_NAME} PRIVATE BUILDING_DLL)
  set_target_properties(${PROJECT_NAME} PROPERTIES
  WINDOWS_EXPORT_ALL_SYMBOLS ON
  # Avoid the lib prefix on Windows
  PREFIX ""
  )
  
  # Link against Windows libraries if needed
  # target_link_libraries(${PROJECT_NAME} PRIVATE ws2_32 kernel32)
  
elseif(APPLE)
  # macOS-specific settings
  set_target_properties(${PROJECT_NAME} PROPERTIES
  MACOSX_RPATH ON
  INSTALL_RPATH "@loader_path"
  )
  
  # Set minimum macOS version
  set(CMAKE_OSX_DEPLOYMENT_TARGET "10.14" CACHE STRING "Minimum macOS deployment version")
  
elseif(UNIX)
  # Linux-specific settings
  set_target_properties(${PROJECT_NAME} PROPERTIES
  INSTALL_RPATH "$ORIGIN"
  )
  
  # Link against common Linux libraries if needed
  # target_link_libraries(${PROJECT_NAME} PRIVATE pthread dl)
endif()

# Set library version properties
set_target_properties(${PROJECT_NAME} PROPERTIES
  VERSION ${PROJECT_VERSION}
  SOVERSION ${PROJECT_VERSION_MAJOR}
)

# Read optimization flags for both Release and Debug configurations
string(JSON MSVC_OPT_RELEASE GET ${BUILD_CONFIG_CONTENT} configurations Release optimization msvc)
string(JSON GCC_OPT_RELEASE GET ${BUILD_CONFIG_CONTENT} configurations Release optimization gcc)
string(JSON CLANG_OPT_RELEASE GET ${BUILD_CONFIG_CONTENT} configurations Release optimization clang)

string(JSON MSVC_OPT_DEBUG GET ${BUILD_CONFIG_CONTENT} configurations Debug optimization msvc)
string(JSON GCC_OPT_DEBUG GET ${BUILD_CONFIG_CONTENT} configurations Debug optimization gcc)
string(JSON CLANG_OPT_DEBUG GET ${BUILD_CONFIG_CONTENT} configurations Debug optimization clang)

string(JSON DEBUG_INFO_RELEASE GET ${BUILD_CONFIG_CONTENT} configurations Release debug_info)
string(JSON DEBUG_INFO_DEBUG GET ${BUILD_CONFIG_CONTENT} configurations Debug debug_info)

# Compiler-specific options with build-type-specific optimization
target_compile_options(${PROJECT_NAME}
  PRIVATE
  # Warning flags
  $<$<CXX_COMPILER_ID:MSVC>:/W4>
  $<$<CXX_COMPILER_ID:GNU>:-Wall -Wextra -Wpedantic -fPIC>
  $<$<CXX_COMPILER_ID:Clang>:-Wall -Wextra -Wpedantic -fPIC>
  # Optimization flags from config (build-type aware)
  # Using SHELL: to properly handle space-separated flags
  $<$<AND:$<CXX_COMPILER_ID:MSVC>,$<CONFIG:Release>>:SHELL:${MSVC_OPT_RELEASE}>
  $<$<AND:$<CXX_COMPILER_ID:MSVC>,$<CONFIG:Debug>>:SHELL:${MSVC_OPT_DEBUG}>
  $<$<AND:$<CXX_COMPILER_ID:GNU>,$<CONFIG:Release>>:SHELL:${GCC_OPT_RELEASE}>
  $<$<AND:$<CXX_COMPILER_ID:GNU>,$<CONFIG:Debug>>:SHELL:${GCC_OPT_DEBUG}>
  $<$<AND:$<CXX_COMPILER_ID:Clang>,$<CONFIG:Release>>:SHELL:${CLANG_OPT_RELEASE}>
  $<$<AND:$<CXX_COMPILER_ID:Clang>,$<CONFIG:Debug>>:SHELL:${CLANG_OPT_DEBUG}>
  # Debug info
  $<$<AND:$<CXX_COMPILER_ID:MSVC>,$<CONFIG:Debug>>:/Zi>
  $<$<AND:$<NOT:$<CXX_COMPILER_ID:MSVC>>,$<CONFIG:Debug>>:-g>
)

# Debug link options
target_link_options(${PROJECT_NAME}
  PRIVATE
  $<$<AND:$<CXX_COMPILER_ID:MSVC>,$<CONFIG:Debug>>:/DEBUG>
)

# Position Independent Code for shared libraries
set_property(TARGET ${PROJECT_NAME} PROPERTY POSITION_INDEPENDENT_CODE ON)

# Optional: Add compile definitions for debug/release
target_compile_definitions(${PROJECT_NAME}
  PRIVATE
  $<$<CONFIG:Debug>:DEBUG>
  $<$<CONFIG:Release>:NDEBUG>
  # Platform definitions
  $<$<PLATFORM_ID:Windows>:WIN32_LEAN_AND_MEAN>
  $<$<PLATFORM_ID:Linux>:_GNU_SOURCE>
)

# Configure graphics APIs using the already-loaded renderer config
include(cmake/GraphicsAPIHelper.cmake)
configure_all_graphics_apis(${PROJECT_NAME} "${RENDERER_CONFIG_CONTENT}")


# Custom install rules
install(TARGETS ${PROJECT_NAME}
  RUNTIME DESTINATION ${BIN_DIR_EXPANDED}
  LIBRARY DESTINATION ${BIN_DIR_EXPANDED}
  ARCHIVE DESTINATION ${BIN_DIR_EXPANDED}
)

# Print configuration info
message(STATUS "")
message(STATUS "===========================================")
message(STATUS "Build Configuration")
message(STATUS "===========================================")
message(STATUS "Platform: ${PLATFORM}")
message(STATUS "Architecture: ${ARCH}")
message(STATUS "Build Type: ${CONFIG}")
message(STATUS "Output Base: ${OUTPUT_BASE_DIR}")
message(STATUS "")
message(STATUS "Release Configuration:")
message(STATUS "  Optimization: MSVC[${MSVC_OPT_RELEASE}] GCC[${GCC_OPT_RELEASE}] Clang[${CLANG_OPT_RELEASE}]")
message(STATUS "  Debug Info: ${DEBUG_INFO_RELEASE}")
message(STATUS "  Output: ${OUTPUT_BASE_DIR}/bin/Release/")
message(STATUS "")
message(STATUS "Debug Configuration:")
message(STATUS "  Optimization: MSVC[${MSVC_OPT_DEBUG}] GCC[${GCC_OPT_DEBUG}] Clang[${CLANG_OPT_DEBUG}]")
message(STATUS "  Debug Info: ${DEBUG_INFO_DEBUG}")
message(STATUS "  Output: ${OUTPUT_BASE_DIR}/bin/Debug/")
message(STATUS "===========================================")
message(STATUS "") 

# Optional: Package configuration
set(CPACK_PACKAGE_NAME ${PROJECT_NAME})
set(CPACK_PACKAGE_VERSION ${PROJECT_VERSION})
set(CPACK_PACKAGE_DESCRIPTION_SUMMARY ${PROJECT_DESCRIPTION})
include(CPack)