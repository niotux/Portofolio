// SHADER class //

class Shader
{
public:
	Shader();
	Shader(const std::string& vertexShaderPath, const std::string& fragmentShaderPath);

	unsigned int ID;

	void use();
	void setInt(const char* name, int value);
	void setFloat(const char* name, float value);
	void setVec3(const char* name, glm::vec3 value);
	void setMat4(const char* name, glm::mat4 value);
	void setMat3(const char* name, glm::mat3 value);
};

