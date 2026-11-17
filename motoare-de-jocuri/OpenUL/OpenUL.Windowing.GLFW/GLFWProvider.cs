using System.Runtime.CompilerServices;
using OpenUL.Windowing.Desktop;

namespace OpenUL.Windowing.GLFW;

public class GLFWProvider : IWindowingBackendProvider<GLFWWindow, DesktopWindowProps>
{
  public static IWindowingBackend<GLFWWindow, DesktopWindowProps> Backend => GLFWWindowingBackend.Instance;
}