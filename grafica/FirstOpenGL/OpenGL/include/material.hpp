// MATERIAL class //

class Material
{
public:
	Material();
	Material(Texture* tex, Shader* shad);

	Texture* texture;
	Shader* shader;
};