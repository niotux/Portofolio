namespace OpenUL.Utility.RefCounted;

public struct RC<T>
  where T : RCObject
{
  public readonly T Object;
  private RC(T obj)
  {
    Object = obj;
  }

  public static implicit operator RC<T>(T obj)
  {
    obj.Aquire();
    return new(obj);
  }

  public static explicit operator T(RC<T> rcObj)
    => rcObj.Object;

  public static RC<T> Weak(T obj)
  {
    return new(obj);
  }
}