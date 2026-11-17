// SHADER_PROGRAM class //

#include <fstream>
#include <sstream>
#include <unordered_map>

#include "c-mplified/logs.hpp"
#include "c-mplified/destroy.hpp"
#include "glad/glad.h"
#include "glm/gtc/type_ptr.hpp"

#include "shaderRender.hpp"

// SHADER
ShaderRender::ShaderRender(const char* programName)
{
	p_ID = glCreateProgram();
	p_name = programName;
	p_ready = false;

	// Logging level
	p_LOG.level = 0;
}

//----------------------------------------------
// Add shader
void ShaderRender::addShader(std::string filePath)
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
		p_LOG.errorLogFile("Shader <render program> \"" + p_name + "\" failed to compile\n" + std::string(infoLog) + "\033[A\r", filePath.c_str());
		
		glDeleteShader(shader);
		return;
	}

	// Attach shader
	glAttachShader(shader, p_ID);
	glDeleteShader(shader);
}

//----------------------------------------------
// Link program
void ShaderRender::link()
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
		p_LOG.errorLog("Failed to link <render program>: \"" + p_name + "\"\n" + std::string(infoLog));
		p_ready = false;

		glDeleteProgram(p_ID);
		return;
	}
	
	// Check that program is ready to use
	p_ready = true;
}

//----------------------------------------------
// Use shader program
void ShaderRender::use()
{
	if (!p_ready)	// Handle use without being linked
	{
		p_LOG.errorLogSimple("The shader <render program> \"" + p_name + "\" could not be used (check if linked correctly before use)");
		return;
	}

	// Use program
	glUseProgram(p_ID);
}

//----------------------------------------------
// Set Uniforms:

/* Basic types */
void ShaderRender::setBool(const char* name, bool value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniform1i(loc, (int)value);
}

void ShaderRender::setInt(const char* name, int value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniform1i(loc, value);
}

void ShaderRender::setUint(const char* name, unsigned int value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniform1ui(loc, value);
}

void ShaderRender::setFloat(const char* name, float value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniform1f(loc, value);
}

/* Vec types */
void ShaderRender::setVec2(const char* name, glm::vec2 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniform2fv(loc, 1, glm::value_ptr(value));
}

void ShaderRender::setVec3(const char* name, glm::vec3 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniform3fv(loc, 1, glm::value_ptr(value));
}

void ShaderRender::setVec4(const char* name, glm::vec4 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniform4fv(loc, 1, glm::value_ptr(value));
}

/* Mat types */
void ShaderRender::setMat2(const char* name, glm::mat2 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniformMatrix2fv(loc, 1, GL_FALSE, glm::value_ptr(value));
}

void ShaderRender::setMat2x3(const char* name, glm::mat2x3 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniformMatrix2x3fv(loc, 1, GL_FALSE, glm::value_ptr(value));
}

void ShaderRender::setMat2x4(const char* name, glm::mat2x4 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniformMatrix2x4fv(loc, 1, GL_FALSE, glm::value_ptr(value));
}

void ShaderRender::setMat3(const char* name, glm::mat3 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniformMatrix3fv(loc, 1, GL_FALSE, glm::value_ptr(value));
}

void ShaderRender::setMat3x2(const char* name, glm::mat3x2 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniformMatrix3x2fv(loc, 1, GL_FALSE, glm::value_ptr(value));
}

void ShaderRender::setMat3x4(const char* name, glm::mat3x4 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniformMatrix3x4fv(loc, 1, GL_FALSE, glm::value_ptr(value));
}

void ShaderRender::setMat4(const char* name, glm::mat4 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniformMatrix4fv(loc, 1, GL_FALSE, glm::value_ptr(value));
}

void ShaderRender::setMat4x2(const char* name, glm::mat4x2 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniformMatrix4x2fv(loc, 1, GL_FALSE, glm::value_ptr(value));
}

void ShaderRender::setMat4x3(const char* name, glm::mat4x3 value)
{
	unsigned int loc = glGetUniformLocation(p_ID, name);
	glUniformMatrix4x3fv(loc, 1, GL_FALSE, glm::value_ptr(value));
}

//----------------------------------------------
// Load file to string
std::string ShaderRender::p_loadToString(std::string& filePath)
{
	// Open file
	std::ifstream file(filePath);
	
	if (!file.is_open())
	{
		p_LOG.errorLogFile("Could not open shader file from <render program> \"" + p_name + "\"", filePath.c_str());
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
unsigned int ShaderRender::p_determineShaderType(std::string& fileName)
{
	// Tessellation control shader
	if (
		fileName.rfind(".tcs") != fileName.npos or
		fileName.rfind(".tesscontrol") != fileName.npos
		)
	{
		return GL_TESS_CONTROL_SHADER;
	}

	// Tessellation evaluation Shader
	if (
		fileName.rfind(".tes") != fileName.npos or
		fileName.rfind(".tesseval") != fileName.npos
		)
	{
		return GL_TESS_EVALUATION_SHADER;
	}

	// Geometry shader
	if (
		fileName.rfind(".gs") != fileName.npos or
		fileName.rfind(".geom") != fileName.npos
		)
	{
		return GL_GEOMETRY_SHADER;
	}

	// Vertex shader
	if (
		fileName.rfind(".vs") != fileName.npos or
		fileName.rfind(".vert") != fileName.npos
		)
	{
		return GL_VERTEX_SHADER;
	}

	// Fragment shader
	if (
		fileName.rfind(".fs") != fileName.npos or
		fileName.rfind(".frag") != fileName.npos
		)
	{
		return GL_FRAGMENT_SHADER;
	}

	// Compute shader (not allowed)
	if (
		fileName.rfind(".cs") != fileName.npos or
		fileName.rfind(".comp") != fileName.npos
		)
	{
		p_LOG.warningLogFile("Cannot have <compute shader> in <render program> \"" + p_name + "\"", fileName.c_str());
		return 0;
	}


	// Shader type not found
	p_LOG.errorLogFile("Failed to determine shader type form <render program> \"" + p_name + "\" by file extension", fileName.c_str());
	return 0;
}
//----------------------------------------------
//----------------------------------------------
// ~SHADER_PROGRAM
ShaderRender::~ShaderRender()
{
	glDeleteProgram(p_ID);
}