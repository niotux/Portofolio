# Metal.cmake
# Metal-specific configuration for the renderer (macOS/iOS only)

function(configure_metal TARGET_NAME CONFIG_JSON)
  message(STATUS "    Configuring Metal...")

  # Metal is Apple-only
  if(NOT APPLE)
    message(WARNING "Metal is only available on Apple platforms. Skipping.")
    return()
  endif()

  # Get frameworks from config
  string(JSON FRAMEWORKS_LENGTH ERROR_VARIABLE fw_err LENGTH ${CONFIG_JSON} graphics_apis metal frameworks)

  if(NOT fw_err AND FRAMEWORKS_LENGTH GREATER 0)
    math(EXPR FRAMEWORKS_END "${FRAMEWORKS_LENGTH} - 1")
    foreach(i RANGE 0 ${FRAMEWORKS_END})
      string(JSON FRAMEWORK GET ${CONFIG_JSON} graphics_apis metal frameworks ${i})
      find_library(${FRAMEWORK}_FRAMEWORK ${FRAMEWORK})

      if(${FRAMEWORK}_FRAMEWORK)
        target_link_libraries(${TARGET_NAME} PRIVATE ${${FRAMEWORK}_FRAMEWORK})
        message(STATUS "      Linked framework: ${FRAMEWORK}")
      else()
        message(WARNING "      Framework not found: ${FRAMEWORK}")
      endif()
    endforeach()
  else()
    # Default Metal frameworks
    find_library(METAL_FRAMEWORK Metal)
    find_library(METALKIT_FRAMEWORK MetalKit)
    find_library(FOUNDATION_FRAMEWORK Foundation)

    if(METAL_FRAMEWORK)
      target_link_libraries(${TARGET_NAME} PRIVATE ${METAL_FRAMEWORK})
      message(STATUS "      Linked framework: Metal")
    endif()

    if(METALKIT_FRAMEWORK)
      target_link_libraries(${TARGET_NAME} PRIVATE ${METALKIT_FRAMEWORK})
      message(STATUS "      Linked framework: MetalKit")
    endif()

    if(FOUNDATION_FRAMEWORK)
      target_link_libraries(${TARGET_NAME} PRIVATE ${FOUNDATION_FRAMEWORK})
      message(STATUS "      Linked framework: Foundation")
    endif()
  endif()

  # Add any custom include directories from config
  string(JSON INCLUDE_DIRS_LENGTH ERROR_VARIABLE inc_err LENGTH ${CONFIG_JSON} graphics_apis metal include_dirs)
  if(NOT inc_err AND INCLUDE_DIRS_LENGTH GREATER 0)
    math(EXPR INCLUDE_DIRS_END "${INCLUDE_DIRS_LENGTH} - 1")
    foreach(i RANGE 0 ${INCLUDE_DIRS_END})
      string(JSON INCLUDE_DIR GET ${CONFIG_JSON} graphics_apis metal include_dirs ${i})
      target_include_directories(${TARGET_NAME} PRIVATE ${INCLUDE_DIR})
      message(STATUS "      Added custom include directory: ${INCLUDE_DIR}")
    endforeach()
  endif()

  # Set minimum macOS version for Metal (10.11+)
  if(NOT CMAKE_OSX_DEPLOYMENT_TARGET)
    set(CMAKE_OSX_DEPLOYMENT_TARGET "10.11" CACHE STRING "Minimum macOS deployment version for Metal")
    message(STATUS "      Set minimum macOS version to 10.11 for Metal support")
  endif()

  # Enable Objective-C++ if needed for Metal
  set_target_properties(${TARGET_NAME} PROPERTIES
    LINKER_LANGUAGE CXX
  )

  message(STATUS "      Metal configuration complete")
endfunction()
