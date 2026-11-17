using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Running;
using System.Runtime.CompilerServices;

namespace UnivKit.Unmanaged.RefCounted.Benchmarks;

public static class Program
{
  public static void Main(string[] args)
  {
    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
  }
}


// =============================================================================
// BENCHMARK 1 — RC<T> mechanism: assembly inspection
//
// Goal: see exactly what the JIT emits for create / pass / dispose sequences.
// Run with: --filter *RCMechanism* --runtimes net9.0 and inspect the asm output.
// =============================================================================

[DisassemblyDiagnoser(printSource: true, syntax: DisassemblySyntax.Intel, maxDepth: 3)]
[MemoryDiagnoser]
public class RCMechanismBenchmark
{
  // ----- single object -------------------------------------------------------

  /// Create one RC<int>, then immediately dispose (refcount 1 → 0).
  [Benchmark]
  public void CreateAndDispose()
  {
    using RC<int> rc = new(42);
  }

  /// Create, pass once (refcount 1 → 2), dispose original (2 → 1), dispose copy (1 → 0).
  [Benchmark]
  public void CreatePassDispose()
  {
    using RC<int> rc = new(42);
    RC<int> copy = rc.Pass();   // rc=2
    copy.Dispose();             // rc=1
  }                             // rc=0

  /// Pass the same object through N hand-offs to stress the increment path.
  [Benchmark]
  [Arguments(4)]
  [Arguments(16)]
  public void PassChain(int depth)
  {
    using RC<int> root = new(42);
    RC<int> current = root.Pass(); // rc=2

    for (int i = 1; i < depth; i++)
    {
      RC<int> next = current.Pass(); // rc++
      current.Dispose();             // rc--
      current = next;
    }

    current.Dispose(); // rc back to 1, root using-block disposes to 0
  }

  // ----- multiple independent objects ----------------------------------------

  /// Create five independent RC<int> objects and dispose them all.
  [Benchmark]
  public void CreateFiveAndDispose()
  {
    using RC<int> a = new(1);
    using RC<int> b = new(2);
    using RC<int> c = new(3);
    using RC<int> d = new(4);
    using RC<int> e = new(5);
  }

  /// Pass five objects once each, then dispose both handles.
  [Benchmark]
  public void PassFiveAndDispose()
  {
    using RC<int> a = new(1);
    using RC<int> b = new(2);
    using RC<int> c = new(3);
    using RC<int> d = new(4);
    using RC<int> e = new(5);

    RC<int> pa = a.Pass();
    RC<int> pb = b.Pass();
    RC<int> pc = c.Pass();
    RC<int> pd = d.Pass();
    RC<int> pe = e.Pass();

    pa.Dispose();
    pb.Dispose();
    pc.Dispose();
    pd.Dispose();
    pe.Dispose();
  }
}


// =============================================================================
// BENCHMARK 2 — RC types vs managed equivalents
//
// Each pair of benchmarks is named  Unmanaged_* / Managed_* so they sort
// together and the structural mirror is obvious.
// =============================================================================

[MemoryDiagnoser]
public class RCVsManagedBenchmark
{
  private const int ArrayLength = 1_000;
  private const string SampleText = "The quick brown fox jumps over the lazy dog";

  // ---------------------------------------------------------------------------
  // 2a. Array allocation + fill + dispose
  // ---------------------------------------------------------------------------

  [Benchmark]
  public void Unmanaged_ArrayAllocFill()
  {
    using RCArray<int> arr = new(ArrayLength);
    for (int i = 0; i < arr.Length; i++)
      arr[i] = i;
  }

  [Benchmark]
  public int[] Managed_ArrayAllocFill()
  {
    int[] arr = new int[ArrayLength];
    for (int i = 0; i < arr.Length; i++)
      arr[i] = i;
    return arr; // returned to prevent DCE; GC handles cleanup
  }

  // ---------------------------------------------------------------------------
  // 2b. Array enumeration (pre-allocated, no allocation cost in hot path)
  // ---------------------------------------------------------------------------

  private RCArray<int> _rcArr = default;
  private int[] _arr = [];

  [GlobalSetup]
  public void Setup()
  {
    _rcArr = new RCArray<int>(ArrayLength);
    _arr = new int[ArrayLength];

    for (int i = 0; i < ArrayLength; i++)
    {
      _rcArr[i] = i;
      _arr[i] = i;
    }
  }

  [GlobalCleanup]
  public void Cleanup() => _rcArr.Dispose();

  [Benchmark]
  public long Unmanaged_ArrayEnumerate()
  {
    long sum = 0;
    foreach (int v in _rcArr) sum += v;
    return sum;
  }

  [Benchmark]
  public long Managed_ArrayEnumerate()
  {
    long sum = 0;
    foreach (int v in _arr) sum += v;
    return sum;
  }

  // ---------------------------------------------------------------------------
  // 2c. String creation from literal
  // ---------------------------------------------------------------------------

  [Benchmark]
  public void Unmanaged_StringCreate()
  {
    using RCString s = new(SampleText.AsSpan());
  }

  [Benchmark]
  public string Managed_StringCreate()
  {
    return new string(SampleText); // allocation, GC handles cleanup
  }

  // ---------------------------------------------------------------------------
  // 2d. String concat (two halves)
  // ---------------------------------------------------------------------------

  private static readonly RCString _rcHalfA = new("The quick brown fox ");
  private static readonly RCString _rcHalfB = new("jumps over the lazy dog");
  private const string _halfA = "The quick brown fox ";
  private const string _halfB = "jumps over the lazy dog";

  [Benchmark]
  public void Unmanaged_StringConcat()
  {
    using RCString result = _rcHalfA + _rcHalfB;
  }

  [Benchmark]
  public string Managed_StringConcat()
  {
    return _halfA + _halfB;
  }

  // ---------------------------------------------------------------------------
  // 2e. String split
  // ---------------------------------------------------------------------------

  private static readonly RCString _rcSentence = new(SampleText.AsSpan());
  private const string _sentence = SampleText;

  [Benchmark]
  public void Unmanaged_StringSplit()
  {
    using RCArray<RCString> parts = _rcSentence.Split(' ');
  }

  [Benchmark]
  public string[] Managed_StringSplit()
  {
    return _sentence.Split(' ');
  }

  // ---------------------------------------------------------------------------
  // 2f. Boxed scalar: RC<double> vs class wrapper
  // ---------------------------------------------------------------------------

  [Benchmark]
  public double Unmanaged_BoxedScalar()
  {
    using RC<double> v = new(3.14);
    return v.Value;
  }

  [Benchmark]
  public double Managed_BoxedScalar()
  {
    // Closest managed analogue: heap-allocated wrapper
    var v = new ManagedBox<double>(3.14);
    return v.Value;
  }
}

/// Managed heap-allocated box used as a fair comparison for RC<T>.
public sealed class ManagedBox<T>(T value)
{
  public T Value { get; } = value;
}


// =============================================================================
// BENCHMARK 3 — Real-world scenario: process a family roster
//
// Unmanaged: RC<Family> owns RCArray<RC<PersonRecord>>, each PersonRecord owns
//            an RCString name. Ownership is explicit, no GC.
//
// Managed:   Plain classes, GC-collected. Structurally mirrored so the
//            boilerplate difference is obvious.
//
// The workload: build the roster, pass ownership to a "processor", sum ages,
// concatenate all names into one string.
// =============================================================================

[MemoryDiagnoser]
public class RealWorldBenchmark
{
  private static readonly (int age, string name)[] RosterData =
  [
    (34, "Alex"),
    (21, "George"),
    (54, "Andrew"),
    (11, "John"),
    (25, "Preston"),
    (38, "Diana"),
    (29, "Marcus"),
    (47, "Elena"),
  ];

  // ===========================================================================
  // UNMANAGED SIDE
  // ===========================================================================

  [Benchmark]
  public (long totalAge, string names) Unmanaged_BuildAndProcess()
  {
    // --- build ---
    using RCArray<RC<PersonRecord>> members = new(RosterData.Length);
    for (int i = 0; i < RosterData.Length; i++)
    {
      var (age, name) = RosterData[i];
      members[i] = new RC<PersonRecord>(new PersonRecord(age, new RCString(name.AsSpan())));
    }

    using RC<Family> family = new(new Family(members.Pass()));

    // --- pass ownership to processor (simulates hand-off to another scope) ---
    RC<Family> handle = family.Pass();
    var result = Unmanaged_Processor(handle); // handle disposed inside
    return result;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static (long totalAge, string names) Unmanaged_Processor(RC<Family> _family)
  {
    using var family = _family;

    long totalAge = 0;
    RCString joined = RCString.Empty;

    foreach (RC<PersonRecord> member in family.Value.Members)
    {
      totalAge += member.Value.Age;

      // Keep track of the old string so we can dispose it after reassigning
      RCString previousJoined = joined;

      if (joined.IsEmpty)
      {
        // Use .Pass() to bump the refcount! 
        // Now 'joined' explicitly owns this reference and can be safely disposed later.
        joined = member.Value.Name.Pass();
      }
      else
      {
        // Allocate the new concatenated string
        joined = RCString.Concat(previousJoined.AsSpan(), ", ".AsSpan(), member.Value.Name.AsSpan());
      }

      // Free the string from the previous loop iteration
      if (!previousJoined.IsEmpty)
      {
        previousJoined.Dispose();
      }
    }

    // Extract the managed string
    string finalNames = joined.ToString();

    // Dispose the final unmanaged string before returning
    if (!joined.IsEmpty)
    {
      joined.Dispose();
    }

    return (totalAge, finalNames);
  }

  // ===========================================================================
  // MANAGED SIDE  (structurally mirrored)
  // ===========================================================================

  [Benchmark]
  public (long totalAge, string names) Managed_BuildAndProcess()
  {
    // --- build ---
    var members = new ManagedPersonRecord[RosterData.Length];
    for (int i = 0; i < RosterData.Length; i++)
    {
      var (age, name) = RosterData[i];
      members[i] = new ManagedPersonRecord(age, name);
    }

    var family = new ManagedFamily(members);

    // --- pass to processor (plain reference, GC tracks it) ---
    var result = Managed_Processor(family);
    return result;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static (long totalAge, string names) Managed_Processor(ManagedFamily family)
  {
    long totalAge = 0;
    string joined = string.Empty;

    foreach (ManagedPersonRecord member in family.Members)
    {
      totalAge += member.Age;
      joined = joined.Length == 0
        ? member.Name
        : string.Concat(joined, ", ", member.Name);
    }

    return (totalAge, joined);
  }
}


// =============================================================================
// Domain types — Unmanaged
// =============================================================================

public struct Family : IDisposable
{
  public RCArray<RC<PersonRecord>> Members { get; }

  public Family(RCArray<RC<PersonRecord>> members)
  {
    Members = members;
  }

  public void Dispose() => Members.Dispose();
}

public record struct PersonRecord : IDisposable
{
  public PersonRecord(int age, RCString name)
  {
    Age = age;
    Name = name;
  }

  public int Age { get; }
  public RCString Name { get; }

  public void Greet() =>
    Console.WriteLine($"Hi, I am {Name} and I am {Age} years old");

  public void Dispose() => Name.Dispose();
}


// =============================================================================
// Domain types — Managed  (mirrored)
// =============================================================================

public sealed class ManagedFamily(ManagedPersonRecord[] members)
{
  public ManagedPersonRecord[] Members { get; } = members;
}

public sealed class ManagedPersonRecord(int age, string name)
{
  public int Age { get; } = age;
  public string Name { get; } = name;

  public void Greet() =>
    Console.WriteLine($"Hi, I am {Name} and I am {Age} years old");
}