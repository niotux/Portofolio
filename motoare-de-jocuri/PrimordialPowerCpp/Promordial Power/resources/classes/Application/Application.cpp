// APLICATION class //

#include "c-mplified/logs.hpp"
#include "c-mplified/destroy.hpp"
#include "glad/glad.h"

#include "Application.hpp"

// APLICATION
Application::Application(const char* appName)
{
	name = appName;

    // Logging level
    p_LOG.level = 0;
}

// init()
void Application::init()
{
    p_LOG.progressLog("Initializing aplication: GLFW initializing...", 0 / 5);

    // GLFW Init //
    glfwInit();
    glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 3);
    glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 3);
    glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE);

    p_LOG.progressLog("Initializing aplication: Creating windows setup...", 1 / 5);

    // Create a monitor
    GLFWmonitor* primaryMonitor = glfwGetPrimaryMonitor();

    // Create a window
    mainWindow = new engine::Window(primaryMonitor, 1000, 800, name);
    mainWindow->setIcon("assets/textures/icon.png");
    
    debugWindow = new engine::Window(primaryMonitor, 1000, 800, "debug_01.a");
    debugWindow->setIcon("assets/textures/icon3.png");

    p_LOG.progressLog("Initializing aplication: initializing GAME...", 4 / 5);

    
    //mainGame = new Game();
    //mainGame->init();

    //debugGame = new Game();
    //debugGame->init();

    p_LOG.progressLog("Initializing aplication: Application initialized", 5 / 5);
}

// run()
void Application::run()
{
    // mainWindow
    if (mainWindow)
    {
        mainWindow->pollEvents();

        //mainGame->simulate(1.0f);
        //mainGame->input(mainWindow);
        //mainGame->render(mainWindow);

        // Death check
        if (mainWindow->shouldClose())
        {
            destroyObject(mainWindow);
            //destroyObject(mainGame);

            p_LOG.infoLogSimple("Main Window has been closed");
        }
    }

    // debugWindow
    if (debugWindow)
    {
        debugWindow->pollEvents();

        //debugGame->simulate(1.0f);
        //debugGame->input(debugWindow);
        //debugGame->render(debugWindow);
        
        // Death check
        if (debugWindow->shouldClose())
        {
            destroyObject(debugWindow);
            //destroyObject(debugGame);

            p_LOG.infoLogSimple("Debug Window has been closed");
        }
    }
}

// shouldStop()
bool Application::shouldStop()
{
    return (
        !mainWindow and
        !debugWindow
        );
}

// ~APLICATION
Application::~Application()
{
    //destroyObject(mainGame);

    destroyObject(mainWindow);
    destroyObject(debugWindow);

    destroyPtr(name);

    p_LOG.infoLogSimple("Application has succesfully closed!");
}