// MODEL3D class //

#include <iostream>
#include <fstream>
#include <string>

#include "glad/glad.h"
#include "glm/gtx/rotate_vector.hpp"
#include "glm/gtc/matrix_transform.hpp"
#include "glm/gtc/type_ptr.hpp"

#include "model3D.hpp"

Model3D::Model3D(const char* modelPath, bool invertNormals)
{
	std::ifstream modelPLY(modelPath);

	if (!modelPLY.is_open())
	{
		std::cout << "class Object3D -> ERROR::File not found: " << std::endl << modelPath << std::endl;
		return;
	}

	// Reading buffers
	float fNbr;				//float AS input buffer
	unsigned int uiNbr;		//unsigned int AS input buffer
	std::string text;		//string AS input buffer

	unsigned int attributeIndex = 0;
	unsigned int attributeKey[8];
	for (int i = 0; i < 8; i++)
	{
		attributeKey[i] = 0;
	}

	// Safety
	bool header_succes = 0;
	bool fileLoad_succes = 0;

	// Checking if PLY header
	modelPLY >> text;
	if (text != "ply")
	{
		std::cout << "class Object3D -> ERROR::File header not PLY:" << std::endl << modelPath << std::endl;
		return;
	}
	// Read PLY file
	while (!fileLoad_succes)
	{
		modelPLY >> text;
		// PLY Header

			// Counting elements
		if (text == "element")
		{
			modelPLY >> text;

			// Counting vertices
			if (text == "vertex")
			{
				if (vtxNbr == 0)	//reading vtxNbr only once
				{
					modelPLY >> vtxNbr;
					header_succes++;
				}
				else				//if vtxNbr is found more than once then refuse to load
				{
					std::cout << "class Object3D -> ERROR::<element vertex> found more than once in:" << std::endl << modelPath << std::endl;
					return;
				}
			}

			// Counting faces
			if (text == "face")
			{
				if (facNbr == 0)	//reading facNbr only once
				{
					modelPLY >> facNbr;
					header_succes++;
				}
				else				//if vtxNbr is found more than once then refuse to load
				{
					std::cout << "class Object3D -> ERROR::<element face> found more than once in:" << std::endl << modelPath << std::endl;
					return;
				}
			}
		}

		// Reading attributes
		if (text == "property")
		{
			modelPLY >> text;
			if (text == "float" or text == "float32")
			{
				modelPLY >> text;
				if (text == "x")
					attributeKey[attributeIndex] = 0;
				if (text == "y")
					attributeKey[attributeIndex] = 2;
				if (text == "z")
					attributeKey[attributeIndex] = 1;
				if (text == "nx")
					attributeKey[attributeIndex] = 3;
				if (text == "ny")
					attributeKey[attributeIndex] = 5;
				if (text == "nz")
					attributeKey[attributeIndex] = 4;
				if (text == "s")
					attributeKey[attributeIndex] = 6;
				if (text == "t")
					attributeKey[attributeIndex] = 7;
			}
			attributeIndex++;
		}
		// PLY End of header
		if (text == "end_header")
		{
			attributeIndex--;
			// VERTICES
			verticesModel = new float[vtxNbr * 8];

			for (unsigned int i = 0; i < vtxNbr; i++)
			{
				for (unsigned int a = 0; a < attributeIndex; a++)
				{
					modelPLY >> fNbr;
					verticesModel[i * 8 + attributeKey[a]] = fNbr;
				}
				verticesModel[i * 8] = -verticesModel[i * 8];
				verticesModel[i * 8 + 3] = -verticesModel[i * 8 + 3];
			}
			// FACES
			faces = new unsigned int[facNbr * 3];
			facesModel = new unsigned int[facNbr * 3];
			vertices = new float[facNbr * 3 * 8];
			// This part of the program copies each vertex called in the faces indices.
			// In order to achieve flat shading each vertex neads normals that correspund with the face.
			// Enjoy : )
			for (unsigned int i = 0; i < facNbr; i++)
			{
				modelPLY >> uiNbr;

#define currentFace i * 3
				glm::vec3 facNormal;
				for (unsigned int v = 0; v < 3; v++)
				{

					modelPLY >> uiNbr;
					
					if (!invertNormals)
					{
						vertices[(currentFace + v) * 8 + 0] = verticesModel[uiNbr * 8 + 0];	// x
						vertices[(currentFace + v) * 8 + 1] = verticesModel[uiNbr * 8 + 1];	// y
						vertices[(currentFace + v) * 8 + 2] = verticesModel[uiNbr * 8 + 2];	// z
						vertices[(currentFace + v) * 8 + 6] = verticesModel[uiNbr * 8 + 6];	// s
						vertices[(currentFace + v) * 8 + 7] = verticesModel[uiNbr * 8 + 7];	// t
					}
					else
					{
						vertices[(currentFace + 2 - v) * 8 + 0] = verticesModel[uiNbr * 8 + 0];	// x
						vertices[(currentFace + 2 - v) * 8 + 1] = verticesModel[uiNbr * 8 + 1];	// y
						vertices[(currentFace + 2 - v) * 8 + 2] = verticesModel[uiNbr * 8 + 2];	// z
						vertices[(currentFace + 2 - v) * 8 + 6] = verticesModel[uiNbr * 8 + 6];	// s
						vertices[(currentFace + 2 - v) * 8 + 7] = verticesModel[uiNbr * 8 + 7];	// t
					}
					glm::vec3 vtx0(vertices[currentFace * 8], vertices[currentFace * 8 + 1], vertices[currentFace * 8 + 2]);
					glm::vec3 vtx1(vertices[(currentFace + 1) * 8], vertices[(currentFace + 1) * 8 + 1], vertices[(currentFace + 1) * 8 + 2]);
					glm::vec3 vtx2(vertices[(currentFace + 2) * 8], vertices[(currentFace + 2) * 8 + 1], vertices[(currentFace + 2) * 8 + 2]);
					facNormal = glm::normalize(glm::cross(vtx1 - vtx0, vtx2 - vtx0));
					faces[currentFace + v] = currentFace + v;
					facesModel[currentFace + v] = uiNbr;
				}

				for (unsigned int v = 0; v < 3; v++)
				{
					vertices[(currentFace + v) * 8 + 3] = facNormal.x;	// nx
					vertices[(currentFace + v) * 8 + 4] = facNormal.y;	// ny
					vertices[(currentFace + v) * 8 + 5] = facNormal.z;	// nz

					//for (int att = 0; att < 8; att++)
					//	std::cout << vertices[(currentFace + v) * 8 + att] << " ";
					//std::cout << std::endl;
				}
			}
			fileLoad_succes = 1;
		}
	}
	modelPLY.close();

// Flat

	// VAO
	glGenVertexArrays(1, &flatVAO);
	glBindVertexArray(flatVAO);
	// VBO
	glGenBuffers(1, &flatVBO);
	glBindBuffer(GL_ARRAY_BUFFER, flatVBO);
	glBufferData(GL_ARRAY_BUFFER, sizeof(float)* facNbr * 3 * 8, vertices, GL_DYNAMIC_DRAW);

	// EBO
	glGenBuffers(1, &flatEBO);
	glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, flatEBO);
	glBufferData(GL_ELEMENT_ARRAY_BUFFER, sizeof(unsigned int)* facNbr * 3, faces, GL_DYNAMIC_DRAW);

	// position attribute
	glVertexAttribPointer(0, 3, GL_FLOAT, GL_FALSE, 8 * sizeof(float), (void*)0);
	glEnableVertexAttribArray(0);
	// normal attribute
	glVertexAttribPointer(1, 3, GL_FLOAT, GL_FALSE, 8 * sizeof(float), (void*)(3 * sizeof(float)));
	glEnableVertexAttribArray(1);
	// texture coord attribute
	glVertexAttribPointer(2, 2, GL_FLOAT, GL_FALSE, 8 * sizeof(float), (void*)(6 * sizeof(float)));
	glEnableVertexAttribArray(2);

// Smooth

	// VAO
	glGenVertexArrays(1, &smoothVAO);
	glBindVertexArray(smoothVAO);
	// VBO
	glGenBuffers(1, &smoothVBO);
	glBindBuffer(GL_ARRAY_BUFFER, smoothVBO);
	glBufferData(GL_ARRAY_BUFFER, sizeof(float) * vtxNbr * 8, verticesModel, GL_DYNAMIC_DRAW);

	// EBO
	glGenBuffers(1, &smoothEBO);
	glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, smoothEBO);
	glBufferData(GL_ELEMENT_ARRAY_BUFFER, sizeof(unsigned int) * facNbr * 3, facesModel, GL_DYNAMIC_DRAW);

	// position attribute
	glVertexAttribPointer(0, 3, GL_FLOAT, GL_FALSE, 8 * sizeof(float), (void*)0);
	glEnableVertexAttribArray(0);
	// normal attribute
	glVertexAttribPointer(1, 3, GL_FLOAT, GL_FALSE, 8 * sizeof(float), (void*)(3 * sizeof(float)));
	glEnableVertexAttribArray(1);
	// texture coord attribute
	glVertexAttribPointer(2, 2, GL_FLOAT, GL_FALSE, 8 * sizeof(float), (void*)(6 * sizeof(float)));
	glEnableVertexAttribArray(2);


}