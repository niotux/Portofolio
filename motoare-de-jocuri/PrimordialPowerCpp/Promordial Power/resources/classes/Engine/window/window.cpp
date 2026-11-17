// WINDOW class //

#include "c-mplified/destroy.hpp"
#include "c-mplified/logs.hpp"
#include "glad/glad.h"
#include "KHR/khrplatform.h"

#include "Engine/window/window.hpp"

// WINDOW
namespace engine
{
    Window::Window(GLFWmonitor* selectMonitor, int startWidth, int startHeight, const char* name)
    {
        // Logging level
        p_LOG.level = 0;

        // Setup
        p_width = startWidth;
        p_height = startHeight;

        p_fullscreen = 0;
        p_wasfullscreen = 0;

        p_ID = glfwCreateWindow(p_width, p_height, name, NULL, NULL);
        p_monitor = selectMonitor;

        // Handle window failing to create
        if (!p_ID)
        {
            p_LOG.errorLog("Failed to create GLFW window \"" + std::string(name) + "\"");
            glfwTerminate();
        }

        // Load GLAD
        glfwMakeContextCurrent(p_ID);
        if (!gladLoadGLLoader((GLADloadproc)glfwGetProcAddress))
        {
            p_LOG.errorLog("GLAD failed to load for window \" " + std::string(name) + " \"");
        }

        // Attach size callback function
        glfwSetFramebufferSizeCallback(p_ID, p_framebuffer_size_callback);
    }

    //----------------------------------------------
    // Window framebuffer size callback
    void Window::p_framebuffer_size_callback(GLFWwindow* window, int currentWidth, int currentHeight)
    {
        // Update viewport
        glViewport(0, 0, currentWidth, currentHeight);
    }

    //----------------------------------------------
    // Window poll events
    void Window::pollEvents()
    {
        glfwMakeContextCurrent(p_ID);

        // Close [Ctrl + C] or [Esc]
        if (
            glfwGetKey(p_ID, GLFW_KEY_LEFT_CONTROL) == GLFW_PRESS and
            glfwGetKey(p_ID, GLFW_KEY_C) == GLFW_PRESS or
            glfwGetKey(p_ID, GLFW_KEY_ESCAPE) == GLFW_PRESS
            )
        {
            glfwSetWindowShouldClose(p_ID, true);
        }

        // Full screen [F11]
        glfwGetWindowSize(p_ID, &p_width, &p_height);

        if (glfwGetKey(p_ID, GLFW_KEY_F11) == GLFW_PRESS)
        {
            p_wasfullscreen = p_fullscreen;
            p_fullscreen = 1;
        }
        else
        {
            p_wasfullscreen = p_fullscreen;
            p_fullscreen = 0;
        }

        if (p_fullscreen == 1 and p_wasfullscreen == 0)
        {
            // Get monitor mode
            const GLFWvidmode* mode = glfwGetVideoMode(p_monitor);

            // Get cursor minimized pozition
            double cursorX;
            double cursorY;
            glfwGetCursorPos(p_ID, &cursorX, &cursorY);

            // Toggle full screen
            p_width = p_lastWidth;
            p_height = p_lastHeight;
            glfwGetWindowSize(p_ID, &p_lastWidth, &p_lastHeight);

            if (!glfwGetWindowMonitor(p_ID))
            {
                glfwSetWindowMonitor(p_ID, p_monitor, 0, 0, mode->width, mode->height, mode->refreshRate);
            }
            else
            {
                glfwSetWindowMonitor(p_ID, NULL, 100, 100, p_width, p_height, mode->refreshRate);
            }

            glfwGetWindowSize(p_ID, &p_width, &p_height);

            // Position cursor
            glfwSetCursorPos(
                p_ID,
                (cursorX - p_lastWidth / 2) * p_height / p_lastHeight + p_width / 2,
                (cursorY - p_lastHeight / 2) * p_height / p_lastHeight + p_height / 2
            );
        }
        glfwPollEvents();
    }

    //----------------------------------------------
    // Update glViewport
    void Window::viewportUpdate()
    {
        glViewport(0, 0, p_width, p_height);
    }

    //----------------------------------------------
    // Swap buffers
    void Window::swapBuffers()
    {
        glfwSwapBuffers(p_ID);
    }

    //----------------------------------------------
    // Set icon
    void Window::setIcon(const char* iconPath)
    {
        // Make icon
        p_icon = new Image(iconPath, 0);
        GLFWimage glfwIcon[1];

        glfwIcon[0].width = p_icon->getWidth();
        glfwIcon[0].height = p_icon->getHeight();
        glfwIcon[0].pixels = p_icon->imageData;

        glfwSetWindowIcon(p_ID, 1, glfwIcon);
    }

    //----------------------------------------------
    // Get size
    void Window::getSize(int* width, int* height)
    {
        glfwGetWindowSize(p_ID, width, height);
    }

    //----------------------------------------------
    // Window should close
    bool Window::shouldClose()
    {
        return glfwWindowShouldClose(p_ID);
    }

    //----------------------------------------------
    //----------------------------------------------
    // ~WINDOW
    Window::~Window()
    {
        glfwDestroyWindow(p_ID);
        destroyPtr(p_ID);

        destroyPtr(p_monitor);

        destroyObject(p_icon);
    }
}