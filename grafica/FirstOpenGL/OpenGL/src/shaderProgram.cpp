// SHADER class //

#include <iostream>
#include <fstream>
#include <string>
#include <sstream>

#include "glad/glad.h"
#include "glm/glm.hpp"
#include "glm/gtc/matrix_transform.hpp"
#include "glm/gtc/type_ptr.hpp"

#include "shaderProgram.hpp"

//CONSTRUCT SHADER PROGRAM
Shader::Shader(const std::string& vertexShaderPath, const std::string& fragmentShaderPath)
{
	int success;
	char infoLog[512];

	std::ifstream vtxShader(vertexShaderPath);
	std::ifstream frgShader(fragmentShaderPath);

	if (!vtxShader.is_open())
	{
		std::cout << "class Shader -> ERROR::Vertex shader path not found: " << std::endl << vertexShaderPath << std::endl;
		return;
	}
	if (!frgShader.is_open())
	{
		std::cout << "class Shader -> ERROR::Fragment shader path not found: " << std::endl << vertexShaderPath << std::endl;
		return;
	}
	std::stringstream vtxBuffer;
	vtxBuffer << vtxShader.rdbuf();
	std::stringstream frgBuffer;
	frgBuffer << frgShader.rdbuf();

	std::string vtxString = vtxBuffer.str();
	std::string frgString = frgBuffer.str();

	const char* vertexShaderSource = vtxString.c_str();
	const char* fragmentShaderSource = frgString.c_str();

	vtxShader.close();
	frgShader.close();

	/* Vertex Shader */
	unsigned int vertexShader;
	vertexShader = glCreateShader(GL_VERTEX_SHADER);
	glShaderSource(vertexShader, 1, &vertexShaderSource, NULL);
	glCompileShader(vertexShader);

	glGetShaderiv(vertexShader, GL_COMPILE_STATUS, &success);
	if (!success)
	{
		glGetShaderInfoLog(vertexShader, 512, NULL, infoLog);
		std::cout << "class Shader -> ERROR::Failed to compile <vertexShader>\n" << infoLog << std::endl;
	}

	/* Fragment Shader */
	unsigned int fragmentShader;
	fragmentShader = glCreateShader(GL_FRAGMENT_SHADER);
	glShaderSource(fragmentShader, 1, &fragmentShaderSource, NULL);
	glCompileShader(fragmentShader);

	glGetShaderiv(fragmentShader, GL_COMPILE_STATUS, &success);
	if (!success)
	{
		glGetShaderInfoLog(fragmentShader, 512, NULL, infoLog);
		std::cout << "class Shader -> ERROR::Failed to compile <fragmentShader>\n" << infoLog << std::endl;
	}

	/* Link Shaders */
	ID = glCreateProgram();
	glAttachShader(ID, vertexShader);
	glAttachShader(ID, fragmentShader);
	glLinkProgram(ID);
	glDeleteShader(vertexShader);
	glDeleteShader(fragmentShader);
}

//USE SHADER PROGRAM
void Shader::use()
{
	glUseProgram(ID);
}

//SET UNIFORMS
void Shader::setInt(const char* name, int value)
{
	unsigned int loc = glGetUniformLocation(ID, name);
	glUniform1f(loc, value);
}

void Shader::setFloat(const char* name, float value)
{
	unsigned int loc = glGetUniformLocation(ID, name);
	glUniform1f(loc, value);
}

void Shader::setVec3(const char* name, glm::vec3 value)
{
	unsigned int loc = glGetUniformLocation(ID, name);
	glUniform3fv(loc, 1, glm::value_ptr(value));
}

void Shader::setMat4(const char* name, glm::mat4 value)
{
	unsigned int loc = glGetUniformLocation(ID, name);
	glUniformMatrix4fv(loc, 1, GL_FALSE, glm::value_ptr(value));
}

void Shader::setMat3(const char* name, glm::mat3 value)
{
	unsigned int loc = glGetUniformLocation(ID, name);
	glUniformMatrix3fv(loc, 1, GL_FALSE, glm::value_ptr(value));
}
