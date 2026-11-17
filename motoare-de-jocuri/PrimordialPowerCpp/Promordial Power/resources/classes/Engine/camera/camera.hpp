// CAMERA class //
#pragma once

#include "glm/gtc/type_ptr.hpp"

#include "Engine/window/window.hpp"

// CAMERA
class Camera
{
public:
	Camera();
	~Camera();

	// Updates viewport
	void viewportUpdate(engine::Window* window);
	void viewportUpdate(int& width, int& height);
	
	// Rotate camera around specific axis
	void rotate(float angle, glm::vec3 axis);

	// Get camera local axis
	glm::mat3 getLocalAxis();

	// Get view matrix
	glm::mat4 getView();

	// Get projection matrix
	glm::mat4 getProj();

	// View settings:
	glm::vec3 position;

	// Projection settings:
	float FOV;	
	float near;
	float far;

private:
	// Rotation:
	glm::vec3 p_front;
	glm::vec3 p_up;

	// View & Projection matrices
	glm::mat4 p_view;
	glm::mat4 p_proj;
};