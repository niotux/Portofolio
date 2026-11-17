// OBJECT class //

#include <iostream>

#include "GLFW/glfw3.h"
#include "glad/glad.h"
#include "glm/gtx/rotate_vector.hpp"
#include "glm/gtc/matrix_transform.hpp"
#include "glm/gtc/type_ptr.hpp"

#include "texture.hpp"
#include "shaderProgram.hpp"
#include "camera.hpp"
#include "material.hpp"
#include "model3D.hpp"
#include "object.hpp"

Object3D::Object3D()
{
}

Object3D::Object3D(Model3D* model3D, Material* material)
{
	usingModel = model3D;
	usingMaterial = material;
}

void Object3D::render(Camera* currentCamera, const char* shading)
{
	// Use Shader
	(*usingMaterial).shader->use();

	// RENDER
	glm::mat4 rotationMatrix_model(
		right.x, right.y, right.z, 0.0f,
		up.x, up.y, up.z, 0.0f,
		-front.x, -front.y, -front.z, 0.0f,
		0.0f, 0.0f, 0.0f, 1.0f
	);

	glm::mat4 model = glm::scale(glm::translate(glm::mat4(1.0f), glm::vec3(position.x, position.y, position.z)), scale) * rotationMatrix_model;
	glm::mat3 normalMatrix = glm::transpose(glm::inverse(glm::mat3(model)));

	(*usingMaterial).shader->setMat4("model", model);
	(*usingMaterial).shader->setMat3("normalMatrix", normalMatrix);

	(*usingMaterial).shader->setMat4("view", (*currentCamera).view);
	(*usingMaterial).shader->setMat4("proj", (*currentCamera).proj);

	(*usingMaterial).shader->setVec3("cameraPos", (*currentCamera).position);

	glBindTexture(GL_TEXTURE_2D, (*usingMaterial).texture->ID);

	if (shading == "smooth")
	{
		glBindVertexArray((*usingModel).smoothVAO);

		glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, (*usingModel).smoothEBO);
		glDrawElements(GL_TRIANGLES, (*usingModel).facNbr * 3, GL_UNSIGNED_INT, 0);
	}
	else if (shading == "flat")
	{
		glBindVertexArray((*usingModel).flatVAO);

		glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, (*usingModel).flatEBO);
		glDrawElements(GL_TRIANGLES, (*usingModel).facNbr * 3, GL_UNSIGNED_INT, 0);
	}
	else
		std::cerr << "class Object.render() -> ERROR::Go fuck yourself, no such shading :p";
}