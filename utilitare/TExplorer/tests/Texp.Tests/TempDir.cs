using System;
using System.IO;

namespace Texp.Tests;

/// <summary>A scratch folder removed when the test ends.</summary>
public sealed class TempDir : IDisposable
{
  public readonly string Path;

  public TempDir()
  {
    Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "texp-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path);
  }

  public string File(string name, string content)
  {
    string p = System.IO.Path.Combine(Path, name);
    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
    System.IO.File.WriteAllText(p, content);
    return p;
  }

  public string Dir(string name)
  {
    string p = System.IO.Path.Combine(Path, name);
    Directory.CreateDirectory(p);
    return p;
  }

  public void Dispose()
  {
    try
    {
      Directory.Delete(Path, true);
    }
    catch (IOException)
    {
    }
  }
}
