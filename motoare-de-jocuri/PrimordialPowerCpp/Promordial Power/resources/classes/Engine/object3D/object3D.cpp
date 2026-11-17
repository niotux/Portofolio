// OBJECT3D class //

#include "c-mplified/logs.hpp"
#include "glad/glad.h"
#include "glm/gtc/matrix_transform.hpp"
#include <glm/gtx/rotate_vector.hpp>

#include "object3D.hpp"

// OBJECT3D
Object3D::Object3D(Mesh3D& mesh3D, Material& material)
{
	usingMesh = &mesh3D;
	usingMaterial = &material;
}

// Rotate object
void Object3D::rotate(const float& angle, const glm::vec3& axis)
{
	front = glm::normalize(glm::rotate(front, glm::radians(angle), axis));
	up = glm::normalize(glm::rotate(up, glm::radians(angle), axis));
	glm::vec3 right = glm::cross(up, front);
	up = glm::cross(front, right);
}

// Render object
void Object3D::render(Camera* currentCamera, const char* shading)
{
	// use shader
	usingMaterial->shader->use();

	// transformations
	glm::vec3 right = glm::cross(up, front);

	glm::mat4 rotationMatrix_model(
		right.x, right.y, right.z, 0.0f,
		up.x, up.y, up.z, 0.0f,
		-front.x, -front.y, -front.z, 0.0f,
		0.0f, 0.0f, 0.0f, 1.0f
	);

	glm::mat4 model = glm::scale(glm::translate(glm::mat4(1.0f), glm::vec3(position.x, position.y, position.z)), scale) * rotationMatrix_model;
	glm::mat3 normalMatrix = glm::transpose(glm::inverse(glm::mat3(model)));

	// shader uniforms
	usingMaterial->shader->setMat4("model", model);
	usingMaterial->shader->setMat3("normalMatrix", normalMatrix);

	usingMaterial->shader->setMat4("view", currentCamera->view);
	usingMaterial->shader->setMat4("proj", currentCamera->proj);

	usingMaterial->shader->setVec3("cameraPos", currentCamera->position);

	// bind texture
	glBindTexture(GL_TEXTURE_2D, usingMaterial->texture->ID);

	// select smooth/flat shading
	if (shading == pp_SMOOTH_SHADING)
	{
		glBindVertexArray(usingMesh->smoothVAO);

		glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, usingMesh->smoothEBO);
		glDrawElements(GL_TRIANGLES, usingMesh->facNbr * 3, GL_UNSIGNED_INT, 0);
	}
	else if (shading == pp_FLAT_SHADING)
	{
		glBindVertexArray(usingMesh->flatVAO);

		glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, usingMesh->flatEBO);
		glDrawElements(GL_TRIANGLES, usingMesh->facNbr * 3, GL_UNSIGNED_INT, 0);
	}
	else
		errorLog("Selected shading type is not valid: " + std::string(shading));
}