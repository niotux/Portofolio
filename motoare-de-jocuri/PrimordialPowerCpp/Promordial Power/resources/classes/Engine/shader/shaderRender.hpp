// SHADER_PROGRAM class //
#pragma once

#include <string>

#include "c-mplified/logs.hpp"
#include "glm/gtc/type_ptr.hpp"

// SHADER
class ShaderRender
{
public:
	ShaderRender(const char* programName);
	~ShaderRender();

	// Add shader
	void addShader(std::string filePath);

	// Link shaders
	void link();

	// Use shader
	void use();

	// Set uniforms:

	/* Basic types */
	void setBool(const char* name, bool value);
	void setInt(const char* name, int value);
	void setUint(const char* name, unsigned int value);
	void setFloat(const char* name, float value);

	/* Vec types */
	void setVec2(const char* name, glm::vec2 value);
	void setVec3(const char* name, glm::vec3 value);
	void setVec4(const char* name, glm::vec4 value);

	/* Mat types */
	void setMat2(const char* name, glm::mat2 value);
	void setMat2x3(const char* name, glm::mat2x3 value);
	void setMat2x4(const char* name, glm::mat2x4 value);
	void setMat3(const char* name, glm::mat3 value);
	void setMat3x2(const char* name, glm::mat3x2 value);
	void setMat3x4(const char* name, glm::mat3x4 value);
	void setMat4(const char* name, glm::mat4 value);
	void setMat4x2(const char* name, glm::mat4x2 value);
	void setMat4x3(const char* name, glm::mat4x3 value);

private:
	// Load file to string
	std::string p_loadToString(std::string& filePath);

	// Detect shader type by filename
	unsigned int p_determineShaderType(std::string& fileName);

	std::string p_name;		// Shader program name
	bool p_ready;			// States if shader program is ready for use
	unsigned int p_ID;		// Shader program ID

	Log p_LOG;	// Log
};

