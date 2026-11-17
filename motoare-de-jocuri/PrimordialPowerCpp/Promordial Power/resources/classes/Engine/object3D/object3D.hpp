// OBJECT3D class //
#pragma once

#define pp_SMOOTH_SHADING "smooth"
#define pp_FLAT_SHADING "flat"

#include "glm/gtc/type_ptr.hpp"

#include "Engine/material/material.hpp"
#include "Engine/camera/camera.hpp"
#include "Engine/mesh3D/mesh3D.hpp"

// OBJECT3D
class Object3D
{
public:
	Object3D(Mesh3D& mesh, Material& material);

	void render(Camera* currentCamera, const char* shading);
	void rotate(const float& angle, const glm::vec3& axis);

	Mesh3D* usingMesh;
	Material* usingMaterial;

	glm::vec3 position = glm::vec3(0.0f, 0.0f, 0.0f);
	glm::vec3 scale = glm::vec3(1.0f, 1.0f, 1.0f);

private:
	glm::vec3 front = glm::vec3(0.0f, 0.0f, 1.0f);
	glm::vec3 up = glm::vec3(0.0f, 1.0f, 0.0f);
};