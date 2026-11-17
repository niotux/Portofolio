// CAMERA class //

class Camera
{
public:
	Camera();

	void viewportUpdate(GLFWwindow* window);
	void rotate(float angle, glm::vec3 axis);

	float FOV = 45.0f;	
	float speed = 0.0f;
	float sensitivity = 0.0f;
	float nearRange = 0.1f;
	float farRange = 100.0f;

	glm::vec3 position;
	glm::vec3 front;
	glm::vec3 up;
	glm::vec3 right;

	glm::mat4 view;
	glm::mat4 proj;
private:
	glm::vec3 target;
};