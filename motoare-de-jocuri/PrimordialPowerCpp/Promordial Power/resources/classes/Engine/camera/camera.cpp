// CAMERA class //

#include "glm/gtx/rotate_vector.hpp"
#include "glm/gtc/matrix_transform.hpp"

#include "camera.hpp"

// CAMERA
Camera::Camera()
{
	// Standard camera setup
	p_front = glm::vec3(0.0f, 0.0f, 1.0f);
	p_up = glm::vec3(0.0f, 1.0f, 0.0f);

	position = glm::vec3(0.0f, 0.0f, 0.0f);

	FOV = 45.0f;
	near = 0.1f;
	far = 100.0f;
}

//----------------------------------------------
// Viewport update
void Camera::viewportUpdate(engine::Window* window)
{
	// Get window size
	int width;
	int height;
	window->getSize(&width, &height);

	p_view = glm::lookAt(position, position + p_front, p_up);
	p_proj = glm::perspective(glm::radians(FOV), (float)width / height, near, far);
}

void Camera::viewportUpdate(int& width, int& height)
{
	p_view = glm::lookAt(position, position + p_front, p_up);
	p_proj = glm::perspective(glm::radians(FOV), (float)width / height, near, far);
}

//----------------------------------------------
// Rotate camera
void Camera::rotate(float angle, glm::vec3 axis)
{
	p_front = glm::normalize(glm::rotate(p_front, glm::radians(angle), axis));
	p_up = glm::normalize(glm::rotate(p_up, glm::radians(angle), axis));
	glm::vec3 right = glm::cross(p_up, p_front);
	p_up = glm::cross(p_front, right);
}

//----------------------------------------------
// Get local axis
glm::mat3 Camera::getLocalAxis()
{
	return glm::mat3(glm::cross(p_front, p_up), p_up, p_front);
}

//----------------------------------------------
// Get view matrix
glm::mat4 Camera::getView()
{
	return p_view;
}

//----------------------------------------------
// Get projection matrix
glm::mat4 Camera::getProj()
{
	return p_proj;
}

//----------------------------------------------
//----------------------------------------------
// ~CAMERA
Camera::~Camera()
{

}