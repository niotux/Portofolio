# Vulkan.cmake
# Simple Vulkan configuration using ONLY CMake's standard find_package method

function(configure_vulkan TARGET_NAME CONFIG_JSON)
  message(STATUS "    Configuring Vulkan SDK using CMake's standard method...")
  
  # Use CMake's built-in Vulkan find_package
  find_package(Vulkan REQUIRED QUIET)
  
  # Add include directories
  if(DEFINED Vulkan_INCLUDE_DIRS)
    target_include_directories(${TARGET_NAME} PRIVATE ${Vulkan_INCLUDE_DIRS})
    message(STATUS "      Added include directories: ${Vulkan_INCLUDE_DIRS}")
  endif()
  
  # Link Vulkan library
  if(DEFINED Vulkan_LIBRARIES)
    target_link_libraries(${TARGET_NAME} PRIVATE ${Vulkan_LIBRARIES})
    message(STATUS "      Linked libraries: ${Vulkan_LIBRARIES}")
  elseif(TARGET Vulkan::Vulkan)
    target_link_libraries(${TARGET_NAME} PRIVATE Vulkan::Vulkan)
    message(STATUS "      Linked target: Vulkan::Vulkan")
  else()
    # Fallback to manual linking if needed
    target_link_libraries(${TARGET_NAME} PRIVATE vulkan)
    message(STATUS "      Linked Vulkan library manually: vulkan")
  endif()
  
  message(STATUS "      Vulkan configuration complete")
endfunction()
