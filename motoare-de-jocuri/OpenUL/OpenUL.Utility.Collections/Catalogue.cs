using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace OpenUL.Utility.Collections;

public class Catalogue<T> : IDictionary<CatalogueKey, T>
{
  public T this[CatalogueKey key]
  {
    get
    {
      if (!TryGetValue(key, out var val))
      {
        throw new NotImplementedException();
      }
      return val;
    }
    set => Entries.Insert(key.Index, new(key, value));
  }

  private UnorderedList<KeyValuePair<CatalogueKey, T>> Entries = new();

  public int Count => Entries.Count;

  public bool IsReadOnly => false;

  public ICollection<CatalogueKey> Keys => throw new NotImplementedException();

  public ICollection<T> Values => throw new NotImplementedException();

  public void Add(CatalogueKey key, T value)
  {
    key.Index = Entries.Count;
    Entries.Add(new(key, value));
  }

  public void Add(KeyValuePair<CatalogueKey, T> item)
  {
    item.Key.Index = Entries.Count;
    Entries.Add(item);
  }

  public CatalogueKey Add(T item)
  {
    CatalogueKey key = new()
    {
      Index = Entries.Count
    };
    Entries.Add(new(key, item));
    return key;
  }

  public void Clear()
  {
    Entries.Clear();
  }

  public bool Contains(KeyValuePair<CatalogueKey, T> item)
  {
    var idx = item.Key.Index;
    if (!ContainsKey(item.Key))
    {
      return false;
    }
    var value = Entries[idx].Value;
    return Equals(value, item.Value);
  }

  private static bool Equals(T a, T b)
  {
    if (a is IEquatable<T> eq)
    {
      return eq.Equals(b);
    }
    return EqualityComparer<T>.Default.Equals(a, b);
  }

  public bool ContainsKey(CatalogueKey key)
  {
    return key.Index < Entries.Count;
  }

  public void CopyTo(KeyValuePair<CatalogueKey, T>[] array, int arrayIndex)
  {
    Entries.CopyTo(array, arrayIndex);
  }

  public void CopyTo(T[] array, int arrayIndex)
  {
    for (int i = arrayIndex; i < array.Length; i++)
    {
      array[i] = Entries[i - arrayIndex].Value;
    }
  }

  public IEnumerator<KeyValuePair<CatalogueKey, T>> GetEnumerator()
  {
    return Entries.GetEnumerator();
  }

  public bool Remove(CatalogueKey key)
  {
    if (!ContainsKey(key))
    {
      return false;
    }

    Entries[^1].Key.Index = key.Index;
    Entries.RemoveAt(key.Index);
    return true;
  }

  public bool Remove(KeyValuePair<CatalogueKey, T> item)
  {
    var key = item.Key;
    if (!ContainsKey(key))
    {
      return false;
    }

    if (!Equals(Entries[key.Index].Value, item.Value))
    {
      return false;
    }

    Entries[^1].Key.Index = key.Index;
    Entries.RemoveAt(key.Index);
    return true;
  }

  public bool TryGetValue(CatalogueKey key, [MaybeNullWhen(false)] out T value)
  {
    if (!ContainsKey(key))
    {
      value = default;
      return false;
    }
    value = Entries[key.Index].Value;
    return true;
  }

  IEnumerator IEnumerable.GetEnumerator()
  {
    return GetEnumerator();
  }
}

public class CatalogueKey
{
  internal int Index;
}
