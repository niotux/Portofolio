#include <iostream>

#include "glm/gtx/rotate_vector.hpp"
#include "glm/gtc/matrix_transform.hpp"
#include "glm/gtc/type_ptr.hpp"

#include "texture.hpp"
#include "shaderProgram.hpp"
#include "material.hpp"

Material::Material(Texture* tex, Shader* shad)
{
	texture = tex;
	shader = shad;
}