using System.Collections;

namespace OpenUL.Utility.Collections;

public struct UnorderedList<T> : IList<T>
{
  private T[]? Values;

  public UnorderedList(long size)
  {
    Values = size > 0 ? new T[size] : null;
    Count = 0;
    ResizingFactor = 2;
  }

  public UnorderedList()
  {
    Values = null;
    Count = 0;
    ResizingFactor = 2;
  }

  public int Count { get; private set; }

  public int Capacity
  {
    get => Values?.Length ?? 0;
    set
    {
      if (value == 0)
      {
        Values = null;
        return;
      }
      if (Values == null)
      {
        Values = new T[value];
        return;
      }
      Array.Resize(ref Values, value);
    }
  }

  public readonly bool IsReadOnly => false;

  public readonly ref T this[int index]
  {
    get
    {
      if ((uint)index >= (uint)Count)
      {
        throw new IndexOutOfRangeException();
      }
      return ref Values![index];
    }
  }

  public readonly ref T this[Index index]
  {
    get
    {
      int i = index.GetOffset(Count);
      if ((uint)i >= (uint)Count)
      {
        throw new IndexOutOfRangeException();
      }
      return ref Values![i];
    }
  }

  T IList<T>.this[int index]
  {
    get => this[index];
    set => this[index] = value;
  }

  public int ResizingFactor { get; set; }

  public void Add(T item)
  {
    EnsureCapacity(Count + 1);
    Values![Count] = item;
    Count++;
  }

  private void EnsureCapacity(int needed)
  {
    if (Values == null)
    {
      Capacity = Math.Max(needed, 8);
      return;
    }

    if (needed > Capacity)
    {
      int factor = ResizingFactor > 1 ? ResizingFactor : 2;
      int newCapacity = Capacity == 0 ? 8 : Capacity;
      while (newCapacity < needed)
      {
        newCapacity *= factor;
      }
      Capacity = newCapacity;
    }
  }

  private void ReduceCapacity()
  {
    int factor = ResizingFactor > 1 ? ResizingFactor : 2;
    if (Capacity == 0)
    {
      return;
    }
    int reduced = Capacity / factor;
    if (reduced >= 8 && Count <= reduced)
    {
      Capacity = reduced;
    }
  }

  public void Clear()
  {
    Count = 0;
    Values = null;
  }

  public readonly bool Contains(T item)
  {
    if (Values == null)
    {
      return false;
    }
    return Array.IndexOf(Values, item, 0, Count) >= 0;
  }

  public readonly void CopyTo(T[] array, int arrayIndex)
  {
    if (Values == null || Count == 0)
    {
      return;
    }
    Values.AsSpan(0, Count).CopyTo(array.AsSpan(arrayIndex));
  }
  public readonly IEnumerator<T> GetEnumerator()
  {
    for (int i = 0; i < Count; i++)
    {
      yield return Values![i];
    }
  }
  readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public int IndexOf(T item)
  {
    if (Values == null)
    {
      return -1;
    }
    return Array.IndexOf(Values, item, 0, Count);
  }

  // "Unordered" list: O(1) removal by swapping the last element into the
  // removed slot instead of shifting everything down.
  public bool Remove(T item)
  {
    int index = IndexOf(item);
    if (index < 0)
    {
      return false;
    }
    RemoveAt(index);
    return true;
  }

  public void RemoveAt(int index)
  {
    if ((uint)index >= (uint)Count)
    {
      throw new IndexOutOfRangeException();
    }

    int lastIndex = Count - 1;
    if (index != lastIndex)
    {
      Values![index] = Values[lastIndex];
    }
    Values![lastIndex] = default!;
    Count--;
    ReduceCapacity();
  }

  // No stable "position" concept in an unordered list, so Insert just
  // appends, ignoring the index. Kept to satisfy IList<T>.
  public void Insert(int index, T item)
  {
    if ((uint)index > (uint)Count)
    {
      throw new IndexOutOfRangeException();
    }
    Values![index] = item;
  }
}