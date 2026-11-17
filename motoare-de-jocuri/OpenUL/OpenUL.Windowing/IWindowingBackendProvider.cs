namespace OpenUL.Windowing;

public interface IWindowingBackendProvider<out TWindowContract, in TWindowProps>
where TWindowContract : IWindow
where TWindowProps : IWindowProps
{
  static abstract IWindowingBackend<TWindowContract, TWindowProps> Backend { get; }
}