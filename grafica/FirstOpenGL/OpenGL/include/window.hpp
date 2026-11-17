// WINDOW functions //

// Create window
GLFWwindow* Window(int width, int height, const char* name);

// Framebuffer size callback
void framebuffer_size_callback(GLFWwindow* window, int width, int height);
// Process input
void window_input(GLFWwindow* window, GLFWmonitor* monitor);
// Mouse callback
void mouse_callback(GLFWwindow* window, double xpos, double ypos);