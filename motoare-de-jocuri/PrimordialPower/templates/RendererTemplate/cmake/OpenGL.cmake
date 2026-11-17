# OpenGL.cmake
# OpenGL-specific configuration for the renderer

function(configure_opengl TARGET_NAME CONFIG_JSON)
  message(STATUS "    Configuring OpenGL...")

  # Find OpenGL package
  find_package(OpenGL QUIET)

  if(NOT OpenGL_FOUND)
    string(JSON REQUIRED ERROR_VARIABLE err GET ${CONFIG_JSON} graphics_apis opengl required)

    if(NOT err AND REQUIRED)
      message(FATAL_ERROR "OpenGL is required but not found.")
    else()
      message(WARNING "OpenGL not found. OpenGL renderer will not be available.")
      return()
    endif()
  endif()

  message(STATUS "      Found OpenGL")

  # Link OpenGL
  if(TARGET OpenGL::GL)
    target_link_libraries(${TARGET_NAME} PRIVATE OpenGL::GL)
    message(STATUS "      Linked target: OpenGL::GL")
  elseif(DEFINED OPENGL_LIBRARIES)
    target_link_libraries(${TARGET_NAME} PRIVATE ${OPENGL_LIBRARIES})
    message(STATUS "      Linked libraries: ${OPENGL_LIBRARIES}")
  endif()

  # Add OpenGL include directories
  if(DEFINED OPENGL_INCLUDE_DIR)
    target_include_directories(${TARGET_NAME} PRIVATE ${OPENGL_INCLUDE_DIR})
    message(STATUS "      Added include directory: ${OPENGL_INCLUDE_DIR}")
  endif()

  # Check for dependencies (like GLEW)
  string(JSON DEPS_LENGTH ERROR_VARIABLE deps_err LENGTH ${CONFIG_JSON} graphics_apis opengl dependencies)
  if(NOT deps_err AND DEPS_LENGTH GREATER 0)
    math(EXPR DEPS_END "${DEPS_LENGTH} - 1")
    foreach(i RANGE 0 ${DEPS_END})
      string(JSON DEPENDENCY GET ${CONFIG_JSON} graphics_apis opengl dependencies ${i})

      # Handle GLEW
      if(DEPENDENCY STREQUAL "GLEW")
        find_package(GLEW QUIET)
        if(GLEW_FOUND)
          if(TARGET GLEW::GLEW)
            target_link_libraries(${TARGET_NAME} PRIVATE GLEW::GLEW)
            message(STATUS "      Found and linked GLEW")
          else()
            target_link_libraries(${TARGET_NAME} PRIVATE ${GLEW_LIBRARIES})
            target_include_directories(${TARGET_NAME} PRIVATE ${GLEW_INCLUDE_DIRS})
            message(STATUS "      Found and linked GLEW (legacy)")
          endif()
        else()
          message(WARNING "      GLEW not found. Modern OpenGL features may not be available.")
        endif()
      else()
        # Generic dependency handling
        find_package(${DEPENDENCY} QUIET)
        if(${DEPENDENCY}_FOUND)
          message(STATUS "      Found dependency: ${DEPENDENCY}")
        else()
          message(WARNING "      Dependency not found: ${DEPENDENCY}")
        endif()
      endif()
    endforeach()
  endif()

  # Add any custom include directories from config
  string(JSON INCLUDE_DIRS_LENGTH ERROR_VARIABLE inc_err LENGTH ${CONFIG_JSON} graphics_apis opengl include_dirs)
  if(NOT inc_err AND INCLUDE_DIRS_LENGTH GREATER 0)
    math(EXPR INCLUDE_DIRS_END "${INCLUDE_DIRS_LENGTH} - 1")
    foreach(i RANGE 0 ${INCLUDE_DIRS_END})
      string(JSON INCLUDE_DIR GET ${CONFIG_JSON} graphics_apis opengl include_dirs ${i})
      target_include_directories(${TARGET_NAME} PRIVATE ${INCLUDE_DIR})
      message(STATUS "      Added custom include directory: ${INCLUDE_DIR}")
    endforeach()
  endif()

  # Add any custom libraries from config
  string(JSON LIBRARIES_LENGTH ERROR_VARIABLE lib_err LENGTH ${CONFIG_JSON} graphics_apis opengl libraries)
  if(NOT lib_err AND LIBRARIES_LENGTH GREATER 0)
    math(EXPR LIBRARIES_END "${LIBRARIES_LENGTH} - 1")
    foreach(i RANGE 0 ${LIBRARIES_END})
      string(JSON LIBRARY GET ${CONFIG_JSON} graphics_apis opengl libraries ${i})
      target_link_libraries(${TARGET_NAME} PRIVATE ${LIBRARY})
      message(STATUS "      Linked custom library: ${LIBRARY}")
    endforeach()
  endif()

  message(STATUS "      OpenGL configuration complete")
endfunction()
