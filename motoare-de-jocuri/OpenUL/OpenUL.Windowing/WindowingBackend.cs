using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;

namespace OpenUL.Windowing;

public static class WindowingBackend
{
  public static IWindowingBackend<TWindowContract, TWindowProps> Select<TWindowContract, TWindowProps>
  (params ReadOnlySpan<IWindowingBackend<TWindowContract, TWindowProps>> selectionList)
    where TWindowContract : IWindow
    where TWindowProps : IWindowProps
  {
    for (int i = 0; i < selectionList.Length; i++)
    {
      if (selectionList[i].IsSupported)
      {
        return selectionList[i];
      }
    }
    throw Helpers.GetNoBackendException();
  }
  public static class Helpers
  {
    public static Exception GetNoBackendException()
      => new NotSupportedException("No backend provided is supported on this platform");
  }
}