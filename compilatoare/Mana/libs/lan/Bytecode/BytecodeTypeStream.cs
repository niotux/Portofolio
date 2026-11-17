using System.Threading.Tasks.Dataflow;

namespace LAN.Bytecode;

public interface IBytecodeReadableType<T>
  where T : IBytecodeReadableType<T>
{
  static abstract T Read(Stream dest);
}


public interface IBytecodeReadableType<T, TOptions>
  where T : IBytecodeReadableType<T, TOptions>
{
  static abstract T Read(Stream dest, TOptions options);
}


public interface IBytecodeWriteableType<T>
where T : IBytecodeWriteableType<T>
{
  static abstract void Write(Stream dest, T value);
}


public class BytecodeTypeStream : IDisposable
{
  private readonly Stream Stream;
  public BytecodeTypeStream(Stream stream)
  {
    Stream = stream;
  }

  public ulong Position
  {
    get => (ulong)Stream.Position;
    set => Stream.Position = (long)value;
  }

  public ulong Length
  {
    get => (ulong)Stream.Length;
    set => Stream.SetLength((long)value);
  }

  public void Dispose()
  {
    Stream.Dispose();
  }

  public void Write<T>(T value)
    where T : IBytecodeWriteableType<T>
  {
    T.Write(Stream, value);
  }

  public T Read<T>()
    where T : IBytecodeReadableType<T>
  {
    return T.Read(Stream);
  }

  public T Read<T, TOptions>(TOptions options)
    where T : IBytecodeReadableType<T, TOptions>
  {
    return T.Read(Stream, options);
  }
}