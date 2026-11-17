namespace QuantiX.Engine.Rendering;

public interface IBuffer : IGPUObject
{
  public void Write<T>(ReadOnlySpan<T> data, uint byteOffset)
    where T : unmanaged;
  public void Write<T>(T data, uint byteOffset)
    where T : unmanaged;
  public void Clear();
  public long Length { get; }
}