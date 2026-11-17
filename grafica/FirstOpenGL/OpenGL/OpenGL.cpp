//
// "My first OpenGL program"
//
//------------------------------------------------------------------------
//std//
#include <iostream>
#include <vector>
#include <cstdlib>

//dependencies//
#include "glad/glad.h"
#include "KHR/khrplatform.h"
#include "GLFW/glfw3.h"
#include "glm/gtc/matrix_transform.hpp"
#include "glm/gtc/type_ptr.hpp"

//include//
//#include "randomGenerator.hpp"
#include "window.hpp"
#include "shaderProgram.hpp"
#include "texture.hpp"
#include "material.hpp"
#include "model3D.hpp"
#include "camera.hpp"
#include "object.hpp"
#include "game.hpp"
#include "progressBar.hpp"


// -----------------------------------------------------------------------
// MAIN //
int main(void)
{


    // -----------------------------------------------------------------------
    //GLFW//

    /* Initialize the GLFW library */
    updateProgressBar("Initializing GLFW: Loading library", 0);

    glfwInit();
    glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 3);
    glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 3);
    glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE);

    updateProgressBar("Initializing GLFW: Creating setup", 50);

    /* Create a monitor */
    GLFWmonitor* monitor = glfwGetPrimaryMonitor();
    const GLFWvidmode* mode = glfwGetVideoMode(monitor);

    /* Create a window */
    GLFWwindow* window1 = Window(2000, 1000, "Window1");
    glfwSetFramebufferSizeCallback(window1, framebuffer_size_callback);


    // -----------------------------------------------------------------------
    //GLAD//

    if (!gladLoadGLLoader((GLADloadproc)glfwGetProcAddress))
    {
        std::cout << "ERROR::Failed to initialize GLAD" << std::endl;
        return -1;
    }

    updateProgressBar("Initializing GLFW: Done", 100);



    // -----------------------------------------------------------------------
    //THE GAME//

    Game theGame(monitor, window1);
    theGame.init();

    // The Loop
    while (!glfwWindowShouldClose(window1))
    {

        theGame.input();
        theGame.renderer();
    }

    // Terminate program
    glfwTerminate();
    return 0;
}

