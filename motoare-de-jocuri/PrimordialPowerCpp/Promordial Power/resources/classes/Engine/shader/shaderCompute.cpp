// SHADER_PROGRAM class //

#include <fstream>
#include <sstream>
#include <unordered_map>

#include "c-mplified/logs.hpp"
#include "c-mplified/destroy.hpp"
#include "glad/glad.h"
#include "glm/gtc/type_ptr.hpp"

#include "shaderCompute.hpp"

// SHADER
ShaderCompute::ShaderCompute(const char* programName)
{
	p_ID = glCreateProgram();
	p_name = programName;
	p_ready = false;

	// Logging level
	p_LOG.level = 0;
}

//----------------------------------------------
// Add shader
void ShaderCompute::addShader(std::string filePath)
{
	// Handle already linked case
	p_ready = false;

	// Load shader source
	std::string shaderSource = p_loadToString(filePath);
	if (shaderSource.empty())	// Handle file reading failure
	{
		return;
	}

	// Determine shader type
	unsigned int type = p_determineShaderType(filePath);

	if (!type)		// Handle type determination failure
	{
		return;
	}

	// Create & compile shader
	unsigned int shader = glCreateShader(type);
	const char* src = shaderSource.c_str();

	glShaderSource(shader, 1, &src, NULL);
	glCompileShader(shader);

	// Check for compilation errors
	int succes;
	char infoLog[512];
	glGetShaderiv(shader, GL_COMPILE_STATUS, &succes);

	if (!succes)	// Handle compilation failure
	{
		glGetShaderInfoLog(shader, 512, NULL, infoLog);
		p_LOG.errorLogFile("Shader <compute program> \"" + p_name + "\" failed to compile\n" + std::string(infoLog) + "\033[A\r", filePath.c_str());

		glDeleteShader(shader);
		return;
	}

	// Attach shader
	glAttachShader(shader, p_ID);
	glDeleteShader(shader);
}

//----------------------------------------------
// Link program
void ShaderCompute::link()
{
	// Link program
	glLinkProgram(p_ID);

	// Check for linking errors
	int succes;
	char infoLog[512];
	glGetProgramiv(p_ID, GL_COMPILE_STATUS, &succes);

	if (!succes)	// Handle linking failure
	{
		glGetProgramInfoLog(p_ID, 512, NULL, infoLog);
		p_LOG.errorLog("Failed to link <compute program>: \"" + p_name + "\"\n" + std::string(infoLog));
		p_ready = false;

		glDeleteProgram(p_ID);
		return;
	}

	// Check that program is ready to use
	p_ready = true;
}

//----------------------------------------------
// Use shader program
void ShaderCompute::use()
{
	if (!p_ready)	// Handle use without being linked
	{
		p_LOG.errorLogSimple("The shader <compute program> \"" + p_name + "\" could not be used (check if linked correctly before use)");
		return;
	}

	// Use program
	glUseProgram(p_ID);
}

//----------------------------------------------
// Set Uniforms:

/* Basic types */
void ShaderCompute::setBool(const char* name, bool value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniform1i(loc, (int)value);
}

void ShaderCompute::setInt(const char* name, int value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniform1i(loc, value);
}

void ShaderCompute::setUint(const char* name, unsigned int value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniform1ui(loc, value);
}

void ShaderCompute::setFloat(const char* name, float value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniform1f(loc, value);
}

/* Vec types */
void ShaderCompute::setVec2(const char* name, glm::vec2 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniform2fv(loc, 1, glm::value_ptr(value));
}

void ShaderCompute::setVec3(const char* name, glm::vec3 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniform3fv(loc, 1, glm::value_ptr(value));
}

void ShaderCompute::setVec4(const char* name, glm::vec4 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniform4fv(loc, 1, glm::value_ptr(value));
}

/* Mat types */
void ShaderCompute::setMat2(const char* name, glm::mat2 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniformMatrix2fv(loc, 1, GL_FALSE, glm::value_ptr(value));
}

void ShaderCompute::setMat2x3(const char* name, glm::mat2x3 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniformMatrix2x3fv(loc, 1, GL_FALSE, glm::value_ptr(value));
}

void ShaderCompute::setMat2x4(const char* name, glm::mat2x4 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniformMatrix2x4fv(loc, 1, GL_FALSE, glm::value_ptr(value));
}

void ShaderCompute::setMat3(const char* name, glm::mat3 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniformMatrix3fv(loc, 1, GL_FALSE, glm::value_ptr(value));
}

void ShaderCompute::setMat3x2(const char* name, glm::mat3x2 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniformMatrix3x2fv(loc, 1, GL_FALSE, glm::value_ptr(value));
}

void ShaderCompute::setMat3x4(const char* name, glm::mat3x4 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniformMatrix3x4fv(loc, 1, GL_FALSE, glm::value_ptr(value));
}

void ShaderCompute::setMat4(const char* name, glm::mat4 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniformMatrix4fv(loc, 1, GL_FALSE, glm::value_ptr(value));
}

void ShaderCompute::setMat4x2(const char* name, glm::mat4x2 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniformMatrix4x2fv(loc, 1, GL_FALSE, glm::value_ptr(value));
}

void ShaderCompute::setMat4x3(const char* name, glm::mat4x3 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniformMatrix4x3fv(loc, 1, GL_FALSE, glm::value_ptr(value));
}

//----------------------------------------------
// Load file to string
std::string ShaderCompute::p_loadToString(std::string& filePath)
{
	// Open file
	std::ifstream file(filePath);

	if (!file.is_open())
	{
		p_LOG.errorLogFile("Could not open shader file from <compute program> \"" + p_name + "\"", filePath.c_str());
		return "";
	}

	// Read file to buffer
	std::stringstream fileBuffer;
	file >> fileBuffer.rdbuf();
	return fileBuffer.str();

	// Close file
	file.close();
}
//----------------------------------------------
// Determine shader type
unsigned int ShaderCompute::p_determineShaderType(std::string& fileName)
{
	// Compute shader
	if (
		fileName.rfind(".cs") != fileName.npos or
		fileName.rfind(".comp") != fileName.npos
		)
	{
		return GL_COMPUTE_SHADER;
	}

	// Tessellation control shader (not allowed)
	if (
		fileName.rfind(".tcs") != fileName.npos or
		fileName.rfind(".tesscontrol") != fileName.npos
		)
	{
		p_LOG.warningLogFile("Cannot have <tesselation ctrl. shader> in <compute program> \"" + p_name + "\"", fileName.c_str());
		return 0;
	}

	// Tessellation evaluation Shader (not allowed)
	if (
		fileName.rfind(".tes") != fileName.npos or
		fileName.rfind(".tesseval") != fileName.npos
		)
	{
		p_LOG.warningLogFile("Cannot have <tesselation eval. shader> in <compute program> \"" + p_name + "\"", fileName.c_str());
		return 0;
	}

	// Geometry shader (not allowed)
	if (
		fileName.rfind(".gs") != fileName.npos or
		fileName.rfind(".geom") != fileName.npos
		)
	{
		p_LOG.warningLogFile("Cannot have <geometry shader> in <compute program> \"" + p_name + "\"", fileName.c_str());
		return 0;
	}

	// Vertex shader (not allowed)
	if (
		fileName.rfind(".vs") != fileName.npos or
		fileName.rfind(".vert") != fileName.npos
		)
	{
		p_LOG.warningLogFile("Cannot have <vertex shader> in <compute program> \"" + p_name + "\"", fileName.c_str());
		return 0;
	}

	// Fragment shader (not allowed)
	if (
		fileName.rfind(".fs") != fileName.npos or
		fileName.rfind(".frag") != fileName.npos
		)
	{
		p_LOG.warningLogFile("Cannot have <fragment shader> in <compute program> \"" + p_name + "\"", fileName.c_str());
		return 0;
	}


	// Shader type not found
	p_LOG.errorLogFile("Failed to determine shader type form <compute program> \"" + p_name + "\" by file extension", fileName.c_str());
	return 0;
}
//----------------------------------------------
//----------------------------------------------
// ~SHADER_PROGRAM
ShaderCompute::~ShaderCompute()
{
	glDeleteProgram(p_ID);
}