// GAME class //
#pragma once

#include <unordered_map>
#include <string>

#include "Engine/object3D/object3D.hpp"

// GAME
class Game
{
public:
	Game(engine::Window* selectWindow);
	~Game();

	void init();
	void simulate(float dt);
	void input();
	void render();

private:
	engine::Window* mainWindow;
	Camera* currentCamera;
	std::unordered_map<std::string, Image*>Images;
	std::unordered_map<std::string, Texture*>Textures;
	std::unordered_map<std::string, ShaderRender*>Shaders;
	std::unordered_map<std::string, Material*>Materials;
	std::unordered_map<std::string, Camera*>Cameras;
	std::unordered_map<std::string, Mesh3D*>Meshes;
	std::unordered_map<std::string, Object3D*>Objects;
};