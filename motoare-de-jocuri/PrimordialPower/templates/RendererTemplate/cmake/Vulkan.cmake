# Vulkan.cmake
# Vulkan-specific configuration for the renderer

function(configure_vulkan TARGET_NAME CONFIG_JSON)
  message(STATUS "    Configuring Vulkan SDK...")

  # Prefer Vulkan SDK from environment
  if(DEFINED ENV{VULKAN_SDK})
    set(VULKAN_SDK_PATH "$ENV{VULKAN_SDK}")
    message(STATUS "      Found VULKAN_SDK environment variable: ${VULKAN_SDK_PATH}")

    # Set platform-specific paths
    if(WIN32)
      set(Vulkan_INCLUDE_DIR "${VULKAN_SDK_PATH}/Include" CACHE PATH "Vulkan headers path")
      if(CMAKE_SIZEOF_VOID_P EQUAL 8)
        set(Vulkan_LIBRARY "${VULKAN_SDK_PATH}/Lib/vulkan-1.lib" CACHE FILEPATH "Vulkan library path")
      else()
        set(Vulkan_LIBRARY "${VULKAN_SDK_PATH}/Lib32/vulkan-1.lib" CACHE FILEPATH "Vulkan library path")
      endif()
    elseif(APPLE)
      set(Vulkan_INCLUDE_DIR "${VULKAN_SDK_PATH}/include" CACHE PATH "Vulkan headers path")
      set(Vulkan_LIBRARY "${VULKAN_SDK_PATH}/lib/libvulkan.dylib" CACHE FILEPATH "Vulkan library path")
    else() # Linux
      set(Vulkan_INCLUDE_DIR "${VULKAN_SDK_PATH}/include" CACHE PATH "Vulkan headers path")
      set(Vulkan_LIBRARY "${VULKAN_SDK_PATH}/lib/libvulkan.so" CACHE FILEPATH "Vulkan library path")
    endif()

    # Check if the library actually exists
    if(EXISTS "${Vulkan_LIBRARY}")
      set(Vulkan_FOUND TRUE)
      message(STATUS "      Vulkan library found: ${Vulkan_LIBRARY}")
    else()
      message(WARNING "      Vulkan library not found at expected path: ${Vulkan_LIBRARY}")
      message(STATUS "      Falling back to find_package...")
      set(Vulkan_FOUND FALSE)
    endif()
  else()
    message(STATUS "      VULKAN_SDK environment variable not set")
    set(Vulkan_FOUND FALSE)
  endif()

  # Fall back to find_package if environment variable not found or invalid
  if(NOT Vulkan_FOUND)
    find_package(Vulkan QUIET)

    if(Vulkan_FOUND)
      message(STATUS "      Found Vulkan via find_package")
    endif()
  endif()

  # Check if we found Vulkan
  if(NOT Vulkan_FOUND)
    string(JSON REQUIRED ERROR_VARIABLE err GET ${CONFIG_JSON} graphics_apis vulkan required)

    if(NOT err AND REQUIRED)
      message(FATAL_ERROR "Vulkan SDK is required but not found. Please install Vulkan SDK and set VULKAN_SDK environment variable.")
    else()
      message(WARNING "Vulkan SDK not found. Vulkan renderer will not be available.")
      return()
    endif()
  endif()

  # Add include directories
  if(DEFINED Vulkan_INCLUDE_DIR)
    target_include_directories(${TARGET_NAME} PRIVATE ${Vulkan_INCLUDE_DIR})
    message(STATUS "      Added include directory: ${Vulkan_INCLUDE_DIR}")
  elseif(DEFINED Vulkan_INCLUDE_DIRS)
    target_include_directories(${TARGET_NAME} PRIVATE ${Vulkan_INCLUDE_DIRS})
    message(STATUS "      Added include directories: ${Vulkan_INCLUDE_DIRS}")
  endif()

  # Link Vulkan library
  if(DEFINED Vulkan_LIBRARY)
    target_link_libraries(${TARGET_NAME} PRIVATE ${Vulkan_LIBRARY})
    message(STATUS "      Linked library: ${Vulkan_LIBRARY}")
  elseif(DEFINED Vulkan_LIBRARIES)
    target_link_libraries(${TARGET_NAME} PRIVATE ${Vulkan_LIBRARIES})
    message(STATUS "      Linked libraries: ${Vulkan_LIBRARIES}")
  elseif(TARGET Vulkan::Vulkan)
    target_link_libraries(${TARGET_NAME} PRIVATE Vulkan::Vulkan)
    message(STATUS "      Linked target: Vulkan::Vulkan")
  endif()

  # Add any custom include directories from config
  string(JSON INCLUDE_DIRS_LENGTH ERROR_VARIABLE inc_err LENGTH ${CONFIG_JSON} graphics_apis vulkan include_dirs)
  if(NOT inc_err AND INCLUDE_DIRS_LENGTH GREATER 0)
    math(EXPR INCLUDE_DIRS_END "${INCLUDE_DIRS_LENGTH} - 1")
    foreach(i RANGE 0 ${INCLUDE_DIRS_END})
      string(JSON INCLUDE_DIR GET ${CONFIG_JSON} graphics_apis vulkan include_dirs ${i})
      # Expand environment variables
      string(REPLACE "$ENV{VULKAN_SDK}" "$ENV{VULKAN_SDK}" INCLUDE_DIR "${INCLUDE_DIR}")
      target_include_directories(${TARGET_NAME} PRIVATE ${INCLUDE_DIR})
      message(STATUS "      Added custom include directory: ${INCLUDE_DIR}")
    endforeach()
  endif()

  # Add any custom libraries from config
  string(JSON LIBRARIES_LENGTH ERROR_VARIABLE lib_err LENGTH ${CONFIG_JSON} graphics_apis vulkan libraries)
  if(NOT lib_err AND LIBRARIES_LENGTH GREATER 0)
    math(EXPR LIBRARIES_END "${LIBRARIES_LENGTH} - 1")
    foreach(i RANGE 0 ${LIBRARIES_END})
      string(JSON LIBRARY GET ${CONFIG_JSON} graphics_apis vulkan libraries ${i})
      target_link_libraries(${TARGET_NAME} PRIVATE ${LIBRARY})
      message(STATUS "      Linked custom library: ${LIBRARY}")
    endforeach()
  endif()

  message(STATUS "      Vulkan configuration complete")
endfunction()
