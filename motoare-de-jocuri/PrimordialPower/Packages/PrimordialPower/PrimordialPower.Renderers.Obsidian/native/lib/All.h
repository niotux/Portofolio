#pragma once

#include <stdint.h>
#include <iostream>
#include <vector>
#include <array>
#include <string>
#include <stdexcept>
#include <cstring>
#include <algorithm>
#include <limits>
#include <exception>

#include "Export.h"
#include "MemHelper.h"

#ifdef OS_LINUX
#define VK_USE_PLATFORM_XLIB_KHR
#endif

#ifdef OS_WINDOWS
#define VK_USE_PLATFORM_WIN32_KHR
#endif

#ifdef OS_MACOS
#define VK_USE_PLATFORM_METAL_EXT
#endif

#include "vulkan/vulkan.h"