// APLICATION class //
#pragma once

#include "c-mplified/logs.hpp"

#include "Application/Game/Game.hpp"
#include "Engine/window/window.hpp"

// APLICATION
class Application
{
public:
	Application(const char* appName);
	~Application();

	bool shouldStop();
	void init();
	void run();

	const char* name;

private:

	engine::Window* debugWindow;
	engine::Window* mainWindow;

	Game* mainGame;
	Game* debugGame;

	Log p_LOG;	// Log
};