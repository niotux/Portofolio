using System.Threading;

namespace Texp.Core;

/// <summary>A long file operation running in the background; the status line shows its progress.</summary>
public sealed class Job
{
  public readonly string Label;
  public readonly CancellationTokenSource Cancel = new();
  long _total;
  long _done;

  public Job(string label)
  {
    Label = label;
  }

  public long Total
  {
    get { return Interlocked.Read(ref _total); }
    set { Interlocked.Exchange(ref _total, value); }
  }

  public long Done
  {
    get { return Interlocked.Read(ref _done); }
  }

  public void Advance(long bytes)
  {
    Interlocked.Add(ref _done, bytes);
  }

  public CancellationToken Token
  {
    get { return Cancel.Token; }
  }
}
