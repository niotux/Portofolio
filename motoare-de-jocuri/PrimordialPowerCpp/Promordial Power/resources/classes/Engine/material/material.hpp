// MATERIAL class //
#pragma once

#include "Engine/shader/shaderRender.hpp"
#include "Engine/texture/texture.hpp"

// MATERIAL
class Material
{
public:
	Material(Texture& tex, ShaderRender& shad);

	Texture* texture;
	ShaderRender* shader;
};