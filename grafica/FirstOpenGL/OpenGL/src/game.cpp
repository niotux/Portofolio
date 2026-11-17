// GAME functions //

#include <iostream>
#include <vector>

#include "glad/glad.h"
#include "KHR/khrplatform.h"
#include "GLFW/glfw3.h"
#include "glm/gtx/rotate_vector.hpp"
#include "glm/gtc/matrix_transform.hpp"
#include "glm/gtc/type_ptr.hpp"

#include "randomGenerator.hpp"
#include "window.hpp"
#include "shaderProgram.hpp"
#include "texture.hpp"
#include "material.hpp"
#include "model3D.hpp"
#include "camera.hpp"
#include "object.hpp"
#include "game.hpp"
//#include "progressBar.hpp"

Game::Game(GLFWmonitor* selectMonitor, GLFWwindow* selectWindow)
{
    selectMonitor = monitor;
    selectWindow = window;
}

// Game init
void Game::init()
{
    // Render settings
    glPolygonMode(GL_FRONT_AND_BACK, GL_FILL);
    glEnable(GL_CULL_FACE);
    glEnable(GL_DEPTH_TEST);

    // Initiate objects


}
// Game input
void Game::input()
{

    // MOVEMENT
    if (glfwGetKey(window, GLFW_KEY_W) == GLFW_PRESS)
        currentCamera->position += (*currentCamera).front * (*currentCamera).speed * dt;
    if (glfwGetKey(window, GLFW_KEY_S) == GLFW_PRESS)
        currentCamera->position -= (*currentCamera).front * (*currentCamera).speed * dt;
    if (glfwGetKey(window, GLFW_KEY_A) == GLFW_PRESS)
        currentCamera->position -= (*currentCamera).right * (*currentCamera).speed * dt;
    if (glfwGetKey(window, GLFW_KEY_D) == GLFW_PRESS)
        currentCamera->position += (*currentCamera).right * (*currentCamera).speed * dt;

    if (glfwGetKey(window, GLFW_KEY_LEFT_SHIFT) == GLFW_PRESS)
    {
        currentCamera->speed += 20.0f * dt;
    }
    if (glfwGetKey(window, GLFW_KEY_LEFT_CONTROL) == GLFW_PRESS)
    {
        currentCamera->speed -= 20.0f * dt;
    }

    // Get cursor position
    double mouseX;
    double mouseY;

    glfwSetInputMode(window, GLFW_CURSOR, GLFW_CURSOR_NORMAL);
    glfwGetCursorPos(window, &mouseX, &mouseY);

    // Get window size
    int width;
    int height;
    glfwGetWindowSize(window, &width, &height);

    // Get window center
    float centerX = width / 2.0f;
    float centerY = height / 2.0f;

    // Get mouse pos from center
    float mouseDeltaX = mouseX - centerX;
    float mouseDeltaY = mouseY - centerY;

    // Rotation YAW thrusters
    float rotThrustYaw = 1 - exp((-2) * (2 * mouseDeltaX / height) * abs(2 * mouseDeltaX / height));
    // Rotation YAW thrusters limiter
    if (abs(rotThrustYaw) >= 1)
    {
        if (rotThrustYaw < 0)
            rotThrustYaw = -1;
        else
            rotThrustYaw = 1;
    }

    // Rotation PITCH thrusters
    float rotThrustPitch = 1 - exp((-2) * (2 * mouseDeltaY / height) * abs(2 * mouseDeltaY / height));
    // Rotation PITCH thrusters limiter
    if (abs(rotThrustPitch) >= 1)
    {
        if (rotThrustPitch < 0)
            rotThrustPitch = -1;
        else
            rotThrustPitch = 1;
    }

    // ROLL //
    if (glfwGetKey(window, GLFW_KEY_Q) == GLFW_PRESS)
        currentCamera->rotate(-(*currentCamera).sensitivity * dt, (*currentCamera).front);
    if (glfwGetKey(window, GLFW_KEY_E) == GLFW_PRESS)
        currentCamera->rotate((*currentCamera).sensitivity * dt, (*currentCamera).front);

    // YAW  + PITCH //
    if (glm::length(glm::vec2(rotThrustYaw, rotThrustPitch)) != 0)
    {
        glm::vec3 rotationAxis = glm::cross(
            currentCamera->front, 
            glm::normalize(currentCamera->up * -rotThrustPitch + currentCamera->right * rotThrustYaw)
        );

        currentCamera->rotate(glm::length(glm::vec2(currentCamera->sensitivity * rotThrustYaw, currentCamera->sensitivity * rotThrustPitch)) * dt, rotationAxis);
    }

    // Update viewport to camera
    currentCamera->viewportUpdate(window);

    
    //std::cout << glm::length((*currentCamera).right) << std::endl;
}

void Game::renderer()
{
    std::cout << "\033[K\r" << "FPS: " << 1 / dt << std::endl;

    // Window update
    int width;
    int height;
    window_input(window, monitor);
    glfwGetWindowSize(window, &width, &height);

    // Clear Buffer
    glClearColor(0.0f, 0.0f, 0.0f, 0.0f);
    glClear(GL_COLOR_BUFFER_BIT);
    glClear(GL_DEPTH_BUFFER_BIT);

    // Swap buffers
    glfwSwapBuffers(window);
    glfwPollEvents();
}