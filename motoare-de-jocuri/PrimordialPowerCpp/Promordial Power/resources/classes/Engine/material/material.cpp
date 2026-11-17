// MATERIAL class //

#include "material.hpp"

// MATERIAL
Material::Material(Texture& tex, ShaderRender& shad)
{
	texture = &tex;
	shader = &shad;
}