
using OpenUL.Windowing;
using OpenUL.Windowing.Desktop;
using OpenUL.Windowing.GLFW;
using OpenUL.Windowing.Platform;

var props = new DesktopWindowProps("title", 800, 600)
{
  Resizable = true
};

using var backend = WindowingBackend.Select(GLFWProvider.Backend);
var window1 = Window.OpenDesktop<GLFWProvider>(props);
var window2 = backend.OpenWindow(props);
window1.Show();
window2.Show();

var win32 = window1.Handle.TryGet<Win32WindowHandle>();
var x11 = window1.Handle.TryGet<X11WindowHandle>();
var wayland = window1.Handle.TryGet<WaylandWindowHandle>();
Console.WriteLine(window1.Handle);
Console.WriteLine(x11);
Console.WriteLine(wayland);

WindowHandle handle = Win32WindowHandle.Create(10, 10);
Console.WriteLine(handle.TryGet<Win32WindowHandle>());

while (backend.OpenCount > 0)
{
  Window.PollEvents<GLFWProvider>();
  window1.UpdateClient();
  window2.UpdateClient();
}