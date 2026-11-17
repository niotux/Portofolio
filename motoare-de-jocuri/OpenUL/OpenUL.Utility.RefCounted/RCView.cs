using System.Collections;

namespace OpenUL.Utility.RefCounted;

public class RCView<T> : RCObject, ICollection<T>, IEnumerable<T>, IList<T>
{
  private Memory<T> Slice;

  public RCView(Memory<T> slice)
  {
    Slice = slice;
  }
  public RCView(T[] array)
  : this(array.AsMemory()) { }

  public RCView(T[] array, int start)
  : this(array.AsMemory(start)) { }

  public RCView(T[] array, int start, int length)
  : this(array.AsMemory(start, length)) { }


  protected override void OnTerminate()
  {
    base.OnTerminate();
    if (typeof(T).IsInstanceOfType(typeof(IDisposable)))
    {
      for (int i = 0; i < Slice.Length; i++)
      {
        ((IDisposable?)Slice.Span[i])?.Dispose();
      }
      return;
    }
    for (int i = 0; i < Slice.Length; i++)
    {
      if (Slice.Span[i] is IDisposable obj)
      {
        obj.Dispose();
      }
    }
  }


  public int Length => Slice.Length;
  public long LongLength => Slice.Length;
  int ICollection<T>.Count => Slice.Length;

  public bool IsReadOnly => false;

  public ref T this[int index] => ref Slice.Span[index];

  T IList<T>.this[int index]
  {
    get => this[index];
    set => this[index] = value;
  }

  public void Add(T item)
    => throw new InvalidOperationException();
  public void Clear()
    => throw new InvalidOperationException();
  public bool Remove(T item)
    => throw new InvalidOperationException();



  public bool Contains(T item)
    => Slice.Span.Contains(item);
  public void CopyTo(T[] array, int arrayIndex)
    => Slice.Span.CopyTo(array.AsSpan(arrayIndex));


  public Enumerator GetEnumerator()
    => new(Slice);
  IEnumerator<T> IEnumerable<T>.GetEnumerator()
    => GetEnumerator();
  IEnumerator IEnumerable.GetEnumerator()
    => GetEnumerator();



  public int IndexOf(T item)
    => Slice.Span.IndexOf(item);
  public void Insert(int index, T item)
    => Slice.Span[index] = item;
  public void RemoveAt(int index)
    => throw new InvalidOperationException();


  public struct Enumerator : IEnumerator<T>
  {
    private Memory<T> Slice;
    private int Index;
    public Enumerator(Memory<T> slice)
    {
      Slice = slice;
    }

    public readonly T Current => Slice.Span[Index];

    readonly object? IEnumerator.Current => Current;

    public void Dispose()
    {
      Reset();
    }

    public bool MoveNext()
    {
      bool result = Index < Slice.Length;
      Index++;
      return result;
    }

    public void Reset()
    {
      Index = 0;
    }
  }
}


