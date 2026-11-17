// TEXTURE class //
#pragma once

#include "c-mplified/logs.hpp"

#include "Engine/image/image.hpp"

#define pp_TEXTURE_COLOR		"color"
#define pp_TEXTURE_ROUGHNESS	"roughness"
#define pp_TEXTURE_METALIC		"metalic"
#define pp_TEXTURE_NORMAL		"normal"

// TEXTURE
class Texture
{
public:
	Texture(const char* textureType, Image& image, unsigned int GLBorderS, unsigned int GLBorderT, unsigned int GLFiltering);

	const char* type;
	unsigned int ID;

private:

	Log p_LOG;	// Log
};
