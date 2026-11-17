// TEXTURE class //

class Texture
{
public:
	Texture();
	Texture(const std::string& texturePath, unsigned int GLColorChannels,unsigned int GLBorderS, unsigned int GLBorderT, unsigned int GLFiltering);

	unsigned int ID;
};
