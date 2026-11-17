namespace OpenUL.Utility.RefCounted;

public class RCValue<T> : RCObject
  where T : IDisposable
{
  public T Value { get; }
  public RCValue(T value)
  {
    Value = value;
  }
  public static implicit operator RCValue<T>(T value)
    => new(value);

  protected override void OnTerminate()
  {
    Value.Dispose();
  }
}