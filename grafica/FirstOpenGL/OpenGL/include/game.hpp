// GAME functions //
class Game
{
public:
	Game(GLFWmonitor* selectMonitor, GLFWwindow* selectWindow);
	
	void init();
	void input();
	void renderer();

	float dt;

	GLFWmonitor* monitor;
	GLFWwindow* window;
	Camera* currentCamera;
};
