# GraphicsAPIHelper.cmake
# Helper functions for configuring graphics APIs from renderer.config.json

# Function to check if a JSON key exists
function(json_key_exists RESULT JSON_STRING KEYS)
  string(JSON VALUE ERROR_VARIABLE ERROR GET ${JSON_STRING} ${KEYS})
  if(ERROR)
    set(${RESULT} FALSE PARENT_SCOPE)
  else()
    set(${RESULT} TRUE PARENT_SCOPE)
  endif()
endfunction()

# Function to configure a single graphics API
function(configure_graphics_api TARGET_NAME API_NAME CONFIG_JSON)
  message(STATUS "")
  message(STATUS "Checking ${API_NAME} configuration...")

  # Check if API is enabled
  string(JSON ENABLED ERROR_VARIABLE err GET ${CONFIG_JSON} graphics_apis ${API_NAME} enabled)

  if(err OR NOT ENABLED)
    message(STATUS "  ${API_NAME}: Disabled")
    return()
  endif()

  # Check platform restriction
  string(JSON PLATFORM ERROR_VARIABLE platform_err GET ${CONFIG_JSON} graphics_apis ${API_NAME} platform)
  if(NOT platform_err)
    if(NOT CMAKE_SYSTEM_NAME STREQUAL PLATFORM)
      message(STATUS "  ${API_NAME}: Not available on ${CMAKE_SYSTEM_NAME} (requires ${PLATFORM})")
      return()
    endif()
  endif()

  message(STATUS "  ${API_NAME}: Enabled")

  # Get compile definitions
  string(JSON DEFS_LENGTH ERROR_VARIABLE defs_err LENGTH ${CONFIG_JSON} graphics_apis ${API_NAME} compile_definitions)
  if(NOT defs_err AND DEFS_LENGTH GREATER 0)
    math(EXPR DEFS_END "${DEFS_LENGTH} - 1")
    foreach(i RANGE 0 ${DEFS_END})
      string(JSON DEF GET ${CONFIG_JSON} graphics_apis ${API_NAME} compile_definitions ${i})
      target_compile_definitions(${TARGET_NAME} PRIVATE ${DEF})
      message(STATUS "    Added definition: ${DEF}")
    endforeach()
  endif()

  # Include the API-specific CMake module
  string(TOUPPER ${API_NAME} API_NAME_UPPER)
  set(API_MODULE "${CMAKE_CURRENT_SOURCE_DIR}/cmake/${API_NAME_UPPER}.cmake")

  # Try with original case if uppercase doesn't exist
  if(NOT EXISTS ${API_MODULE})
    set(API_MODULE "${CMAKE_CURRENT_SOURCE_DIR}/cmake/${API_NAME}.cmake")
  endif()

  # Try with capitalized first letter
  if(NOT EXISTS ${API_MODULE})
    string(SUBSTRING ${API_NAME} 0 1 FIRST_LETTER)
    string(TOUPPER ${FIRST_LETTER} FIRST_LETTER_UPPER)
    string(SUBSTRING ${API_NAME} 1 -1 REST)
    set(API_NAME_CAPITALIZED "${FIRST_LETTER_UPPER}${REST}")
    set(API_MODULE "${CMAKE_CURRENT_SOURCE_DIR}/cmake/${API_NAME_CAPITALIZED}.cmake")
  endif()

  if(EXISTS ${API_MODULE})
    message(STATUS "    Including module: ${API_MODULE}")
    include(${API_MODULE})

    # Call the API-specific configuration function
    string(TOLOWER ${API_NAME} API_NAME_LOWER)
    cmake_language(CALL configure_${API_NAME_LOWER} ${TARGET_NAME} ${CONFIG_JSON})
  else()
    message(WARNING "  ${API_NAME}: No CMake module found at ${API_MODULE}")
  endif()

endfunction()

# Main function to configure all enabled graphics APIs
function(configure_all_graphics_apis TARGET_NAME CONFIG_JSON)
  message(STATUS "===========================================")
  message(STATUS "Configuring Graphics APIs")
  message(STATUS "===========================================")

  # List of all possible APIs (order matters for priority)
  set(ALL_APIS vulkan opengl directx12 metal)

  set(ENABLED_API_COUNT 0)

  foreach(API ${ALL_APIS})
    configure_graphics_api(${TARGET_NAME} ${API} ${CONFIG_JSON})

    # Check if it was actually enabled and configured
    string(JSON ENABLED ERROR_VARIABLE err GET ${CONFIG_JSON} graphics_apis ${API} enabled)
    if(NOT err AND ENABLED)
      math(EXPR ENABLED_API_COUNT "${ENABLED_API_COUNT} + 1")
    endif()
  endforeach()

  message(STATUS "===========================================")
  message(STATUS "Total enabled APIs: ${ENABLED_API_COUNT}")
  message(STATUS "===========================================")
  message(STATUS "")

  if(ENABLED_API_COUNT EQUAL 0)
    message(WARNING "No graphics APIs are enabled! The renderer may not function properly.")
  endif()
endfunction()

# Function to read and validate the renderer config
function(load_renderer_config CONFIG_FILE)
  if(NOT EXISTS ${CONFIG_FILE})
    message(FATAL_ERROR "Renderer configuration file not found: ${CONFIG_FILE}")
  endif()

  file(READ ${CONFIG_FILE} CONFIG_CONTENT)
  set(RENDERER_CONFIG ${CONFIG_CONTENT} PARENT_SCOPE)

  message(STATUS "Loaded renderer configuration from: ${CONFIG_FILE}")
endfunction()
