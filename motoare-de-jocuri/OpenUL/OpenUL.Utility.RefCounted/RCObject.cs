namespace OpenUL.Utility.RefCounted;

public class RCObject : IDisposable
{
  private volatile uint RefCountField = 1;
  protected uint RefCount => RefCountField;
  private readonly bool Threaded;
  private volatile bool Terminated;
  public RCObject(bool initThreading = true)
  {
    Threaded = initThreading;
  }

  public bool IsThreaded() => Threaded;

  internal RCObject Aquire()
  {
    if (Threaded)
    {
      Interlocked.Increment(ref RefCountField);
    }
    else
    {
      RefCountField++;
    }

    OnAquire();
    return this;
  }

  public void Dispose()
  {
    OnDispose();
    if (Threaded)
    {
      var c = Interlocked.Decrement(ref RefCountField);
      if (c == 0)
      {
        Terminate();
      }
    }
    else
    {
      RefCountField--;
      if (RefCountField == 0)
      {
        Terminate();
      }
    }
  }

  protected virtual void OnTerminate()
  { }

  protected virtual void OnAquire()
  { }

  protected virtual void OnDispose()
  { }

  private void Terminate()
  {
    GC.SuppressFinalize(this);
    OnTerminate();

    if (Threaded)
    {
      var t = Interlocked.Exchange(ref Terminated, true);
    }
    else
    {

    }
  }
}