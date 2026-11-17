namespace QuantiX.Engine.RenderingTMP;

/// <summary>
/// Opaque reference to a static geometry allocation inside a RenderableModule.
/// Dispose to free the allocated region back to the static pool.
/// </summary>
public interface IDrawHandle : IDisposable { }
