// CAMERA class //

#include <iostream>

#include "GLFW/glfw3.h"
#include "glm/gtx/rotate_vector.hpp"
#include "glm/gtc/matrix_transform.hpp"
#include "glm/gtc/type_ptr.hpp"

#include "camera.hpp"

Camera::Camera()
{
	front = glm::vec3(0.0f, 0.0f, 1.0f);
	up = glm::vec3(0.0f, 1.0f, 0.0f);
	right = glm::vec3(-1.0f, 0.0f, 0.0f);
	position = glm::vec3(0.0f, 0.0f, 0.0f);
}
void Camera::viewportUpdate(GLFWwindow* window)
{
	// Get window size
	int width;
	int height;
	glfwGetWindowSize(window, &width, &height);

	target = position + front;
	view = glm::lookAt(position, target, up);
	proj = glm::perspective(glm::radians(FOV), (float)(width) / (height), nearRange, farRange);
}
void Camera::rotate(float angle, glm::vec3 axis)
{
	if (axis == up)
	{
		front = glm::normalize(glm::rotate(front, glm::radians(angle), axis));
		right = glm::normalize(glm::cross(front, up));
		front = glm::normalize(glm::cross(up, right));
	}
	else if (axis == front)
	{
		up = glm::normalize(glm::rotate(up, glm::radians(angle), axis));
		right = glm::normalize(glm::cross(front, up));
		up = glm::normalize(glm::cross(right, front));
	}
	else if (axis == right)
	{
		front = glm::normalize(glm::rotate(front, glm::radians(angle), axis));
		up = glm::normalize(glm::cross(right, front));
		front = glm::normalize(glm::cross(up, right));
	}
	else
	{
		front = glm::normalize(glm::rotate(front, glm::radians(angle), axis));
		up = glm::normalize(glm::rotate(up, glm::radians(angle), axis));
		right = glm::normalize(glm::cross(front, up));
		up = glm::normalize(glm::cross(right, front));
		front = glm::normalize(glm::cross(up, right));
	}

}