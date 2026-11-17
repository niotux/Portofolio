// WINDOW functions //

#include <iostream>

#include "GLFW/glfw3.h"

#include "window.hpp"

// -----------------------------------------------------------------------
//

GLFWwindow* Window(int width, int height, const char* name)
{
    GLFWwindow* window = glfwCreateWindow(width, height, name, NULL, NULL);
    if (!window)
    {
        std::cout << "ERROR::Failed to create GLFW window <" << name << ">" << std::endl;
        glfwTerminate();
    }
    glfwMakeContextCurrent(window);

    return window;
}

void framebuffer_size_callback(GLFWwindow* window, int width, int height)
{
    glViewport(0, 0, width, height);
}

void window_input(GLFWwindow* window, GLFWmonitor* monitor)
{
    // Close [Ctrl + C]
    if (glfwGetKey(window, GLFW_KEY_LEFT_CONTROL) == GLFW_PRESS and glfwGetKey(window, GLFW_KEY_C) == GLFW_PRESS)
    {
        glfwSetWindowShouldClose(window, true);
    }

    // Full screen [F11]
    if (glfwGetKey(window, GLFW_KEY_F11) == GLFW_PRESS)
    {
        // get monitor mode
        const GLFWvidmode* mode = glfwGetVideoMode(monitor);

        // get cursor minimized pozition
        double cursorX;
        double cursorY;
        glfwGetCursorPos(window, &cursorX, &cursorY);

        // get previous resolution
        int lastWidth;
        int lastHeight;
        glfwGetWindowSize(window, &lastWidth, &lastHeight);

        // toggle full screen
        if (!glfwGetWindowMonitor(window))
        {
            glfwSetWindowMonitor(window, monitor, 0, 0, (*mode).width, (*mode).height, (*mode).refreshRate);
        }
        else
        {
            glfwSetWindowMonitor(window, NULL, 100, 100, lastWidth, lastHeight, (*mode).refreshRate);
        }

        // set new cursor position
        glfwSetCursorPos(
            window,
            (cursorX - lastWidth / 2) * (*mode).height / lastHeight + (*mode).width / 2,
            (cursorY - lastHeight / 2) * (*mode).height / lastHeight + (*mode).height / 2
        );
    }
}