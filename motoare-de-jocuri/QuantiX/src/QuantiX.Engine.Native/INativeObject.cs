namespace QuantiX.Engine.Native;

public interface IObjectCtor<TParams>
{
  public void New(TParams parameters);
}