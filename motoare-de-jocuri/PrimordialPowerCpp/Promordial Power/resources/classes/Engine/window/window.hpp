// WINDOW class //
#pragma once

#include "c-mplified/logs.hpp"
#include "GLFW/glfw3.h"

#include "Engine/image/image.hpp"

// WINDOW
namespace engine
{
	class Window
	{
	public:
		Window(GLFWmonitor* selectMonitor, int startWidth, int startHeight, const char* name);
		~Window();

		// Updates:
		void pollEvents();		// Updates window
		void viewportUpdate();	// Updates viewport
		void swapBuffers();		// Swaps the buffers

		// Set icon
		void setIcon(const char* iconPath);

		// Get size
		void getSize(int* width, int* height);

		// Should close
		bool shouldClose();		// Returns if window should close

	private:
		// framebuffer size callback function
		static void p_framebuffer_size_callback(GLFWwindow* window, int width, int height);

		// glfw:
		GLFWwindow* p_ID;
		GLFWmonitor* p_monitor;

		// Icon
		Image* p_icon;

		// Current size:
		int p_width;
		int p_height;

		// Full screen:
		bool p_fullscreen;
		bool p_wasfullscreen;

		// Previous size:
		int p_lastWidth;
		int p_lastHeight;

		Log p_LOG;	// Log
	};
}