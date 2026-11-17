// OBJECT class //

class Object3D
{
public:
	Object3D();
	Object3D(Model3D* model3D, Material* material);

	void render(Camera* currentCamera, const char* shading);	// Renders the object on the viewport

	glm::vec3 position = glm::vec3(0.0f, 0.0f, 0.0f);
	glm::vec3 scale = glm::vec3(1.0f, 1.0f, 1.0f);

	glm::vec3 front = glm::vec3(0.0f, 0.0f, 1.0f);
	glm::vec3 right = glm::vec3(-1.0f, 0.0f, 0.0f);
	glm::vec3 up = glm::vec3(0.0f, 1.0f, 0.0f);

	Model3D* usingModel;
	Material* usingMaterial;
};