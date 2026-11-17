// MESH3D class //
#pragma once

#include <vector>

#include "glm/gtc/type_ptr.hpp"

#include "Engine/shader/shaderCompute.hpp"

// MESH3D
class Mesh3D
{
public:
	Mesh3D();

	// Load mesh
	void loadPLY(const char* filePath);		// Load PLY file
	void loadS3DM(const char* filePath);	// Load Scripted 3D Mesh file
	
	// Get mesh buffers
	unsigned int getVertexBuffer();		// Get vertex buffer
	unsigned int getVertexAtributes();		// Get vertex atributes
	unsigned int getFaceBuffer();		// Get face buffer

	// Get number of elements
	unsigned int getVtxNbr();
	unsigned int getFacNbr();

private:
	unsigned int p_vtxNbr = 0;			// The number of vertices
	unsigned int p_facNbr = 0;			// The number of faces

	std::vector<float> p_vertices;		// Vertices
	std::vector<unsigned int> p_faces;	// Faces

	unsigned int p_VBO, p_VAO, p_EBO;			// OpenGL buffers
};