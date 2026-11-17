// GAME class //

#include "c-mplified/destroy.hpp"
#include "c-mplified/logs.hpp"
#include "glad/glad.h"

#include "Game.hpp"

// GAME
Game::Game(Window* selectWindow)
{
    mainWindow = selectWindow;
}

// init()
void Game::init()
{
    glfwMakeContextCurrent(mainWindow->ID);

	currentCamera = new Camera();
	currentCamera->FOV = 100.0f;
	currentCamera->position = glm::vec3(4.0f, 0.0f, -100.0f);
    currentCamera->near = 0.1;
    currentCamera->far = 2000;

	Images["logo"] = new Image("assets/textures/icon.png", 1);

	Textures["logo"] = new Texture(pp_TEXTURE_COLOR, *Images.at("logo"), GL_REPEAT, GL_REPEAT, GL_NEAREST);

	Shaders["planet"] = new Shader("assets/shaders/vertexShader.vs.glsl", "assets/shaders/planetShader.fs.glsl");

	Materials["logo"] = new Material(*Textures.at("logo"), *Shaders.at("planet"));

	Meshes["cube"] = new Mesh3D();

	Objects["test"] = new Object3D(*Meshes.at("cube"), *Materials.at("logo"));
	Objects.at("test")->position = glm::vec3(0.0f, 0.0f, 0.0f);
}

// simulate()
void Game::simulate(float dt)
{
	//currentCamera->rotate(1.0f * dt, glm::vec3(0.0f, 1.0f, 0.0f));
}

// input()
void Game::input()
{
    float dt = 0.16f;
    float sensitivity = 20.0f;
    float speed = 0.5f;

    glm::vec3 front;
    glm::vec3 right;
    glm::vec3 up;

    glm::mat3 cameraAxis = currentCamera->getLocalAxis();
    right = glm::vec3(cameraAxis[0]);
    up = glm::vec3(cameraAxis[1]);
    front = glm::vec3(cameraAxis[2]);

    // MOVEMENT
    if (glfwGetKey(mainWindow->ID, GLFW_KEY_W) == GLFW_PRESS)
        currentCamera->position += front * speed * dt;
    if (glfwGetKey(mainWindow->ID, GLFW_KEY_S) == GLFW_PRESS)
        currentCamera->position -= front * speed * dt;
    if (glfwGetKey(mainWindow->ID, GLFW_KEY_A) == GLFW_PRESS)
        currentCamera->position -= right * speed * dt;
    if (glfwGetKey(mainWindow->ID, GLFW_KEY_D) == GLFW_PRESS)
        currentCamera->position += right * speed * dt;

    if (glfwGetKey(mainWindow->ID, GLFW_KEY_LEFT_SHIFT) == GLFW_PRESS)
    {
        speed += 20.0f * dt;
    }
    if (glfwGetKey(mainWindow->ID, GLFW_KEY_LEFT_CONTROL) == GLFW_PRESS)
    {
        speed -= 20.0f * dt;
    }

    // Get cursor position
    double mouseX;
    double mouseY;

    glfwSetInputMode(mainWindow->ID, GLFW_CURSOR, GLFW_CURSOR_NORMAL);
    glfwGetCursorPos(mainWindow->ID, &mouseX, &mouseY);

    // Get window size
    int width;
    int height;
    glfwGetWindowSize(mainWindow->ID, &width, &height);

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
    if (glfwGetKey(mainWindow->ID, GLFW_KEY_Q) == GLFW_PRESS)
        currentCamera->rotate(-sensitivity * dt, front);

    cameraAxis = currentCamera->getLocalAxis();
    right = glm::vec3(cameraAxis[0]);
    up = glm::vec3(cameraAxis[1]);
    front = glm::vec3(cameraAxis[2]);

    if (glfwGetKey(mainWindow->ID, GLFW_KEY_E) == GLFW_PRESS)
        currentCamera->rotate(sensitivity * dt, front);

    cameraAxis = currentCamera->getLocalAxis();
    right = glm::vec3(cameraAxis[0]);
    up = glm::vec3(cameraAxis[1]);
    front = glm::vec3(cameraAxis[2]);

    // YAW  + PITCH //
    if (glm::length(glm::vec2(rotThrustYaw, rotThrustPitch)) != 0)
    {
        glm::vec3 rotationAxis = glm::cross(
            front,
            glm::normalize(up * -rotThrustPitch + right * rotThrustYaw)
        );

        currentCamera->rotate(glm::length(glm::vec2(sensitivity * rotThrustYaw, sensitivity * rotThrustPitch)) * dt, rotationAxis);

        cameraAxis = currentCamera->getLocalAxis();
        right = glm::vec3(cameraAxis[0]);
        up = glm::vec3(cameraAxis[1]);
        front = glm::vec3(cameraAxis[2]);
    }

}

// render()
void Game::render()
{
	// Render settings
	glPolygonMode(GL_FRONT_AND_BACK, GL_FILL);
	glEnable(GL_CULL_FACE);
	glEnable(GL_DEPTH_TEST);
	
	// Camera & Viewport update
    mainWindow->viewportUpdate();
	currentCamera->viewportUpdate(mainWindow);

	// Clear render buffers
	/*glClearColor(0.25f, 0.43f, 0.27f, 1.0f);*/
    glClearColor(0.0f, 0.0f, 0.0f, 1.0f);
	glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

	// Render objects
    Shaders.at("planet")->setVec3("lightDirection", glm::vec3(1.0f, 1.0f, 0.5f));
	Objects.at("test")->render(currentCamera, pp_SMOOTH_SHADING);

	// Swap buffers
    mainWindow->swapBuffers();
}

// ~GAME
Game::~Game()
{
	for (auto& pair : Images)
	{
		destroyObject(pair.second);
	}
	for (auto& pair : Textures)
	{
		destroyObject(pair.second);
	}
	for (auto& pair : Materials)
	{
		destroyObject(pair.second);
	}
	for (auto& pair : Meshes)
	{
		destroyObject(pair.second);
	}
	for (auto& pair : Objects)
	{
		destroyObject(pair.second);
	}

	destroyObject(currentCamera);
}