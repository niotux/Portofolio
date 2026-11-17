# Directx12.cmake
# DirectX 12-specific configuration for the renderer (Windows only)

function(configure_directx12 TARGET_NAME CONFIG_JSON)
  message(STATUS "    Configuring DirectX 12...")

  # DirectX 12 is Windows-only
  if(NOT WIN32)
    message(WARNING "DirectX 12 is only available on Windows. Skipping.")
    return()
  endif()

  # DirectX 12 is built into Windows 10+ SDK, no need to find_package

  # Get system libraries from config
  string(JSON SYS_LIBS_LENGTH ERROR_VARIABLE sys_libs_err LENGTH ${CONFIG_JSON} graphics_apis directx12 system_libraries)

  if(NOT sys_libs_err AND SYS_LIBS_LENGTH GREATER 0)
    math(EXPR SYS_LIBS_END "${SYS_LIBS_LENGTH} - 1")
    foreach(i RANGE 0 ${SYS_LIBS_END})
      string(JSON SYS_LIB GET ${CONFIG_JSON} graphics_apis directx12 system_libraries ${i})
      target_link_libraries(${TARGET_NAME} PRIVATE ${SYS_LIB})
      message(STATUS "      Linked system library: ${SYS_LIB}")
    endforeach()
  else()
    # Default DirectX 12 libraries
    target_link_libraries(${TARGET_NAME} PRIVATE
      d3d12
      dxgi
      d3dcompiler
    )
    message(STATUS "      Linked default DX12 libraries: d3d12, dxgi, d3dcompiler")
  endif()

  # Add any custom include directories from config
  string(JSON INCLUDE_DIRS_LENGTH ERROR_VARIABLE inc_err LENGTH ${CONFIG_JSON} graphics_apis directx12 include_dirs)
  if(NOT inc_err AND INCLUDE_DIRS_LENGTH GREATER 0)
    math(EXPR INCLUDE_DIRS_END "${INCLUDE_DIRS_LENGTH} - 1")
    foreach(i RANGE 0 ${INCLUDE_DIRS_END})
      string(JSON INCLUDE_DIR GET ${CONFIG_JSON} graphics_apis directx12 include_dirs ${i})
      target_include_directories(${TARGET_NAME} PRIVATE ${INCLUDE_DIR})
      message(STATUS "      Added custom include directory: ${INCLUDE_DIR}")
    endforeach()
  endif()

  # Check Windows SDK version
  if(DEFINED CMAKE_VS_WINDOWS_TARGET_PLATFORM_VERSION)
    message(STATUS "      Windows SDK version: ${CMAKE_VS_WINDOWS_TARGET_PLATFORM_VERSION}")

    # DirectX 12 requires Windows 10 SDK (10.0.10586.0 or later)
    if(CMAKE_VS_WINDOWS_TARGET_PLATFORM_VERSION VERSION_LESS "10.0.10586.0")
      message(WARNING "DirectX 12 requires Windows 10 SDK 10.0.10586.0 or later. Current version: ${CMAKE_VS_WINDOWS_TARGET_PLATFORM_VERSION}")
    endif()
  endif()

  message(STATUS "      DirectX 12 configuration complete")
endfunction()
