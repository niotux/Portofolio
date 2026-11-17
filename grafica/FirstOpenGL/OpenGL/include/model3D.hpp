// MODEL3D class //

class Model3D
{
public:
	Model3D();
	Model3D(const char* modelPath, bool invertNormals);

	float* verticesModel;				// The vertices read from the file
	unsigned int* facesModel;			// The faces read from the file
	float* vertices;					// The actual vertices used by the renderer
	unsigned int* faces;				// The actual faces used by the renderer

	unsigned int smoothVBO, smoothVAO, smoothEBO;
	unsigned int flatVBO, flatVAO, flatEBO;

	unsigned int vtxNbr = 0;	// The number of vertices read from the file
	unsigned int facNbr = 0;	// The number of faces
};