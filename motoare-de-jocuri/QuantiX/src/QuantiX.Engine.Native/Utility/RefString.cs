namespace QuantiX.Engine.Native.Utility;

public readonly struct RefString
{
  public static implicit operator RefString(string obj)
  {
    return "";
  }
}