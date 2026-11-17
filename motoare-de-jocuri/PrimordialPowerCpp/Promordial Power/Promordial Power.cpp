// MAIN //

#include "c-mplified/logs.hpp"
#include "c-mplified/destroy.hpp"
#include "glad/glad.h"

#include "Application/Application.hpp"


int main()
{
    Application App("Primordial Game");

    App.init();
    //<debug>
    //GLFWmonitor* monitor = glfwGetPrimaryMonitor();
    //Window* debugWin = new Window(monitor, 800, 600, "debug_01", "assets/textures/AG.png");

    //Camera* currentCamera = new Camera();
    //currentCamera->FOV = 45;
    //currentCamera->position = glm::vec3(0, 0, 0);

    //Image testimg("assets/textures/AG.png", 1);
    //Texture colortex(pp_TEXTURE_COLOR, testimg, GL_REPEAT, GL_REPEAT, GL_NEAREST);

    //ShaderRender shader("testShader");
    //shader.addShader("assets/shaders/vertexShader.vs.glsl");
    //shader.addShader("assets/shaders/dumbShader.cs.glsl");
    //shader.addShader("assets/shaders/planetShader.ss.glsl");
    //shader.link();
    //shader.use();

    //Material testmat(colortex, shader);

    //Mesh3D mesh("assets/mesh/cube.ply", 0);

    //Object3D object(mesh, testmat);
    //</debug>

// RUNTIME loop //
    while (!App.shouldStop())
    {
        App.run();
    }

    glfwTerminate();
    return 0;
}