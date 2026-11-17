namespace OpenUL.Utility.RefCounted;

public static class RCExtensions
{
  public static T Aquire<T>(this T rcObj)
    where T : RCObject
    => ((RC<T>)rcObj).Object;

  public static RC<T> Weak<T>(this T rcObj)
    where T : RCObject
    => RC<T>.Weak(rcObj);
}