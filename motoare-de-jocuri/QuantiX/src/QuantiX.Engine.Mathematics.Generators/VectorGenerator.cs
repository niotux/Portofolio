using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace QuantiX.Engine.Mathematics.Generators;

[Generator]
public class VectorGenerator : IIncrementalGenerator
{
  // ── Attribute injected into the consuming compilation ────────────────

  private const string AttributeSource = @"
namespace QuantiX.Engine.Mathematics.Vectors
{
    [System.AttributeUsage(System.AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    internal sealed class GenerateVectorAttribute : System.Attribute
    {
        public int Dimensions { get; }
        public int SimdSlots { get; set; }
        public bool HasCross { get; set; }
        public string CrossResult { get; set; }

        public GenerateVectorAttribute(int dimensions)
        {
            Dimensions = dimensions;
            SimdSlots = dimensions;
            CrossResult = """";
        }
    }
}
";

  // ── Pipeline ────────────────────────────────────────────────────────

  public void Initialize(IncrementalGeneratorInitializationContext context)
  {
    context.RegisterPostInitializationOutput(static ctx =>
      ctx.AddSource("GenerateVectorAttribute.g.cs", AttributeSource));

    var specs = context.SyntaxProvider.ForAttributeWithMetadataName(
      "QuantiX.Engine.Mathematics.Vectors.GenerateVectorAttribute",
      predicate: static (_, _) => true,
      transform: static (ctx, _) => ExtractSpec(ctx));

    context.RegisterSourceOutput(specs, static (ctx, spec) =>
      ctx.AddSource($"{spec.Name}.g.cs", GenerateSource(spec)));
  }

  // ── Spec extraction from attribute ──────────────────────────────────

  readonly struct VectorSpec : IEquatable<VectorSpec>
  {
    public readonly string Name;
    public readonly string Namespace;
    public readonly int Dimensions;
    public readonly int SimdSlots;
    public readonly bool HasCross;
    public readonly string CrossResult; // empty = self, otherwise the result type name (e.g. "Vec3")

    public VectorSpec(string name, string ns, int dimensions, int simdSlots, bool hasCross, string crossResult)
    {
      Name = name;
      Namespace = ns;
      Dimensions = dimensions;
      SimdSlots = simdSlots;
      HasCross = hasCross;
      CrossResult = crossResult;
    }

    public bool Equals(VectorSpec other) =>
      Name == other.Name && Namespace == other.Namespace &&
      Dimensions == other.Dimensions && SimdSlots == other.SimdSlots &&
      HasCross == other.HasCross && CrossResult == other.CrossResult;

    public override bool Equals(object? obj) => obj is VectorSpec o && Equals(o);

    public override int GetHashCode()
    {
      unchecked
      {
        int h = 17;
        h = h * 31 + (Name?.GetHashCode() ?? 0);
        h = h * 31 + (Namespace?.GetHashCode() ?? 0);
        h = h * 31 + Dimensions;
        h = h * 31 + SimdSlots;
        h = h * 31 + HasCross.GetHashCode();
        h = h * 31 + (CrossResult?.GetHashCode() ?? 0);
        return h;
      }
    }
  }

  static VectorSpec ExtractSpec(GeneratorAttributeSyntaxContext ctx)
  {
    var symbol = (INamedTypeSymbol)ctx.TargetSymbol;
    var attr = ctx.Attributes[0];

    int dimensions = (int)attr.ConstructorArguments[0].Value!;
    int simdSlots = dimensions;
    bool hasCross = false;
    string crossResult = "";

    foreach (var named in attr.NamedArguments)
    {
      switch (named.Key)
      {
        case "SimdSlots": simdSlots = (int)named.Value.Value!; break;
        case "HasCross": hasCross = (bool)named.Value.Value!; break;
        case "CrossResult": crossResult = (string)named.Value.Value! ?? ""; break;
      }
    }

    string name = symbol.Name;
    string ns = symbol.ContainingNamespace!.IsGlobalNamespace
      ? ""
      : symbol.ContainingNamespace.ToDisplayString();

    return new VectorSpec(name, ns, dimensions, simdSlots, hasCross, crossResult);
  }

  // ── Derived helpers ─────────────────────────────────────────────────

  static readonly string[][] ComponentsByDim =
  {
    Array.Empty<string>(),
    Array.Empty<string>(),
    new[] { "X", "Y" },
    new[] { "X", "Y", "Z" },
    new[] { "X", "Y", "Z", "W" },
  };

  struct SimdTier
  {
    public int SizeOfT;
    public int Bits;
    public string VecType => $"Vector{Bits}";
    public SimdTier(int sizeOfT, int bits) { SizeOfT = sizeOfT; Bits = bits; }
  }

  static SimdTier[] GetSimdTiers(int simdSlots)
  {
    if (simdSlots != 2 && simdSlots != 4) return Array.Empty<SimdTier>();
    var tiers = new List<SimdTier>();
    foreach (int s in new[] { 2, 4, 8 })
    {
      int bits = simdSlots * s * 8;
      if (bits == 64 || bits == 128 || bits == 256)
        tiers.Add(new SimdTier(s, bits));
    }
    return tiers.ToArray();
  }

  // ── Code generation ─────────────────────────────────────────────────

  const string MI = "  [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]";
  const string MI_INLINE = "  [MethodImpl(MethodImplOptions.AggressiveInlining)]";

  static string GenerateSource(VectorSpec spec)
  {
    var sb = new StringBuilder();
    string[] components = ComponentsByDim[spec.Dimensions];
    bool hasPadding = spec.SimdSlots > spec.Dimensions;
    var tiers = GetSimdTiers(spec.SimdSlots);
    string n = spec.Name;
    string t = $"{n}<T>";
    // Cross result type: self if empty, otherwise the specified type
    string crossResultType = string.IsNullOrEmpty(spec.CrossResult) ? t : $"{spec.CrossResult}<T>";
    bool crossReturnsSelf = crossResultType == t;

    // Usings
    sb.AppendLine("using System.Numerics;");
    sb.AppendLine("using System.Runtime.CompilerServices;");
    sb.AppendLine("using System.Runtime.InteropServices;");
    sb.AppendLine("using System.Runtime.Intrinsics;");
    if (spec.HasCross && hasPadding && crossReturnsSelf)
      sb.AppendLine("using System.Runtime.Intrinsics.X86;");
    sb.AppendLine();

    // Namespace
    if (!string.IsNullOrEmpty(spec.Namespace))
    {
      sb.AppendLine($"namespace {spec.Namespace};");
      sb.AppendLine();
    }

    // Partial struct with interfaces
    sb.AppendLine("[StructLayout(LayoutKind.Sequential)]");
    sb.Append($"public partial record struct {t} : IVector<{t}>, IVectorOfScalar<{t}, T>");
    if (spec.HasCross)
      sb.Append($", IVectorWithCrossProduct<{t}, {crossResultType}>");
    foreach (var c in components)
      sb.Append($", IVec{c}Component<T>");
    sb.AppendLine();
    sb.AppendLine("{");

    // Fields
    foreach (var c in components)
      sb.AppendLine($"  private T {c.ToLowerInvariant()};");
    if (hasPadding)
      sb.AppendLine("  private readonly T __w;");
    sb.AppendLine();

    // Constructors
    string broadcastArgs = string.Join(", ", components.Select(_ => "val"));
    sb.AppendLine($"  public {n}(T val) : this({broadcastArgs}) {{ }}");

    string ctorParams = string.Join(", ", components.Select(c => $"T {c.ToLowerInvariant()}"));
    sb.AppendLine($"  public {n}({ctorParams})");
    sb.AppendLine("  {");
    foreach (var c in components)
      sb.AppendLine($"    this.{c.ToLowerInvariant()} = {c.ToLowerInvariant()};");
    if (hasPadding)
      sb.AppendLine("    __w = T.Zero;");
    sb.AppendLine("  }");
    sb.AppendLine();

    // Properties
    foreach (var c in components)
      sb.AppendLine($"  public T {c} {{ get => this.{c.ToLowerInvariant()}; set => this.{c.ToLowerInvariant()} = value; }}");
    sb.AppendLine();

    // LengthSquared
    sb.AppendLine("  public T LengthSquared => Dot(this, this);");
    sb.AppendLine();

    // Static fields
    sb.AppendLine($"  public static readonly {t} Identity = new(T.One);");
    sb.AppendLine($"  public static readonly {t} Zero = new(T.Zero);");
    sb.AppendLine($"  public const int Dimensions = {components.Length};");
    sb.AppendLine();

    // Explicit interface implementations
    sb.AppendLine($"  static {t} IVector<{t}>.Identity => Identity;");
    sb.AppendLine($"  static {t} IVector<{t}>.Zero => Zero;");
    sb.AppendLine($"  static int IVector<{t}>.Dimensions => Dimensions;");
    sb.AppendLine();

    // DistanceSquared
    sb.AppendLine(MI);
    sb.AppendLine($"  public static T DistanceSquared(in {t} vec1, in {t} vec2) => Subtract(vec1, vec2).LengthSquared;");
    sb.AppendLine();

    // Binary operations
    foreach (var (op, sym) in new[] { ("Add", "+"), ("Subtract", "-"), ("Multiply", "*"), ("Divide", "/") })
    {
      EmitBinaryOp(sb, t, components, tiers, op, sym);
      sb.AppendLine();
    }

    // Dot
    EmitDot(sb, t, components, tiers);
    sb.AppendLine();

    // Cross
    if (spec.HasCross)
    {
      EmitCross(sb, t, crossResultType, spec.Dimensions, hasPadding && crossReturnsSelf);
      sb.AppendLine();
    }

    // Vec-vec operators
    foreach (var (op, sym) in new[] { ("Add", "+"), ("Subtract", "-"), ("Multiply", "*"), ("Divide", "/") })
    {
      sb.AppendLine(MI);
      sb.AppendLine($"  public static {t} operator {sym}({t} left, {t} right) => {op}(left, right);");
      sb.AppendLine();
    }

    // Scalar operations
    foreach (var (op, _) in new[] { ("Add", "+"), ("Subtract", "-"), ("Multiply", "*"), ("Divide", "/") })
    {
      sb.AppendLine(MI);
      sb.AppendLine($"  public static {t} {op}(in {t} left, T right) => {op}(left, new {t}(right));");
      sb.AppendLine();
    }

    // Scalar operators
    foreach (var (op, sym) in new[] { ("Add", "+"), ("Subtract", "-"), ("Multiply", "*"), ("Divide", "/") })
    {
      sb.AppendLine(MI);
      sb.AppendLine($"  public static {t} operator {sym}({t} left, T right) => {op}(left, right);");
      sb.AppendLine();
    }

    // Tuple conversions
    string tupleType = string.Join(", ", components.Select(c => $"T {c}"));
    string tupleFrom = string.Join(", ", components.Select(c => $"vec.{c}"));
    string tupleTo = string.Join(", ", components.Select(c => $"tuple.{c}"));

    sb.AppendLine(MI);
    sb.AppendLine($"  public static implicit operator ({tupleType})(in {t} vec) => ({tupleFrom});");
    sb.AppendLine();
    sb.AppendLine(MI);
    sb.AppendLine($"  public static implicit operator {t}(({tupleType}) tuple) => new {t}({tupleTo});");

    // SIMD helpers
    foreach (var tier in tiers)
    {
      sb.AppendLine();
      sb.AppendLine(MI_INLINE);
      sb.AppendLine($"  private static {tier.VecType}<T> AsV{tier.Bits}(in {t} v) => Unsafe.As<{t}, {tier.VecType}<T>>(ref Unsafe.AsRef(in v));");
      sb.AppendLine();
      sb.AppendLine(MI_INLINE);
      sb.AppendLine($"  private static {t} FromV{tier.Bits}(in {tier.VecType}<T> v) => Unsafe.As<{tier.VecType}<T>, {t}>(ref Unsafe.AsRef(in v));");
    }

    sb.AppendLine("}");
    return sb.ToString();
  }

  static void EmitBinaryOp(StringBuilder sb, string t, string[] components, SimdTier[] tiers, string op, string sym)
  {
    sb.AppendLine(MI);
    sb.AppendLine($"  public static {t} {op}(in {t} left, in {t} right)");
    sb.AppendLine("  {");
    foreach (var tier in tiers)
    {
      sb.AppendLine($"    if (Unsafe.SizeOf<T>() == {tier.SizeOfT} && {tier.VecType}.IsHardwareAccelerated)");
      sb.AppendLine("    {");
      sb.AppendLine($"      return FromV{tier.Bits}({tier.VecType}.{op}(AsV{tier.Bits}(left), AsV{tier.Bits}(right)));");
      sb.AppendLine("    }");
    }
    string args = string.Join(", ", components.Select(c => $"left.{c.ToLowerInvariant()} {sym} right.{c.ToLowerInvariant()}"));
    sb.AppendLine($"    return new({args});");
    sb.AppendLine("  }");
  }

  static void EmitDot(StringBuilder sb, string t, string[] components, SimdTier[] tiers)
  {
    sb.AppendLine(MI);
    sb.AppendLine($"  public static T Dot(in {t} left, in {t} right)");
    sb.AppendLine("  {");
    foreach (var tier in tiers)
    {
      sb.AppendLine($"    if (Unsafe.SizeOf<T>() == {tier.SizeOfT} && {tier.VecType}.IsHardwareAccelerated)");
      sb.AppendLine("    {");
      sb.AppendLine($"      return {tier.VecType}.Dot(AsV{tier.Bits}(left), AsV{tier.Bits}(right));");
      sb.AppendLine("    }");
    }
    string sum = string.Join(" + ", components.Select(c => $"prod.{c.ToLowerInvariant()}"));
    sb.AppendLine($"    {t} prod = Multiply(left, right);");
    sb.AppendLine($"    return {sum};");
    sb.AppendLine("  }");
  }

  static void EmitCross(StringBuilder sb, string selfType, string resultType, int dimensions, bool emitSimd)
  {
    sb.AppendLine(MI);
    sb.AppendLine($"  public static {resultType} Cross(in {selfType} left, in {selfType} right)");
    sb.AppendLine("  {");

    if (dimensions == 2)
    {
      // 2D cross product: returns a 3D vector with only the Z component set
      sb.AppendLine($"    return new(T.Zero, T.Zero, left.x * right.y - left.y * right.x);");
      sb.AppendLine("  }");
      return;
    }

    // 3D cross product (dimensions == 3)
    if (emitSimd)
    {
      sb.AppendLine($"    // float: 2x shufps + 2x mulps + 1x subps  (SSE)");
      sb.AppendLine($"    if (typeof(T) == typeof(float) && Sse.IsSupported)");
      sb.AppendLine("    {");
      sb.AppendLine($"      var a = Unsafe.As<{selfType}, Vector128<float>>(ref Unsafe.AsRef(in left));");
      sb.AppendLine($"      var b = Unsafe.As<{selfType}, Vector128<float>>(ref Unsafe.AsRef(in right));");
      sb.AppendLine("      var aYZX = Sse.Shuffle(a, a, 0xC9);");
      sb.AppendLine("      var bZXY = Sse.Shuffle(b, b, 0xD2);");
      sb.AppendLine("      var aZXY = Sse.Shuffle(a, a, 0xD2);");
      sb.AppendLine("      var bYZX = Sse.Shuffle(b, b, 0xC9);");
      sb.AppendLine("      var r = Sse.Subtract(Sse.Multiply(aYZX, bZXY), Sse.Multiply(aZXY, bYZX));");
      sb.AppendLine($"      return Unsafe.As<Vector128<float>, {resultType}>(ref r);");
      sb.AppendLine("    }");
      sb.AppendLine();
      sb.AppendLine($"    // double: 2x vpermq + 2x vmulpd + 1x vsubpd  (AVX2)");
      sb.AppendLine($"    if (typeof(T) == typeof(double) && Avx2.IsSupported)");
      sb.AppendLine("    {");
      sb.AppendLine($"      var a = Unsafe.As<{selfType}, Vector256<double>>(ref Unsafe.AsRef(in left));");
      sb.AppendLine($"      var b = Unsafe.As<{selfType}, Vector256<double>>(ref Unsafe.AsRef(in right));");
      sb.AppendLine("      var aYZX = Avx2.Permute4x64(a, 0xC9);");
      sb.AppendLine("      var bZXY = Avx2.Permute4x64(b, 0xD2);");
      sb.AppendLine("      var aZXY = Avx2.Permute4x64(a, 0xD2);");
      sb.AppendLine("      var bYZX = Avx2.Permute4x64(b, 0xC9);");
      sb.AppendLine("      var r = Avx.Subtract(Avx.Multiply(aYZX, bZXY), Avx.Multiply(aZXY, bYZX));");
      sb.AppendLine($"      return Unsafe.As<Vector256<double>, {resultType}>(ref r);");
      sb.AppendLine("    }");
      sb.AppendLine();
      sb.AppendLine($"    // Half: byte-level shuffle in Vector64 (ARM NEON)");
      sb.AppendLine($"    if (typeof(T) == typeof(Half) && Vector64.IsHardwareAccelerated)");
      sb.AppendLine("    {");
      sb.AppendLine("      var aBytes = AsV64(left).AsByte();");
      sb.AppendLine("      var bBytes = AsV64(right).AsByte();");
      sb.AppendLine("      var yzxIdx = Vector64.Create((byte)2, 3, 4, 5, 0, 1, 6, 7);");
      sb.AppendLine("      var zxyIdx = Vector64.Create((byte)4, 5, 0, 1, 2, 3, 6, 7);");
      sb.AppendLine("      var aYZXb = Vector64.Shuffle(aBytes, yzxIdx);");
      sb.AppendLine("      var bZXYb = Vector64.Shuffle(bBytes, zxyIdx);");
      sb.AppendLine("      var aZXYb = Vector64.Shuffle(aBytes, zxyIdx);");
      sb.AppendLine("      var bYZXb = Vector64.Shuffle(bBytes, yzxIdx);");
      sb.AppendLine("      var aYZX = Unsafe.As<Vector64<byte>, Vector64<T>>(ref aYZXb);");
      sb.AppendLine("      var bZXY = Unsafe.As<Vector64<byte>, Vector64<T>>(ref bZXYb);");
      sb.AppendLine("      var aZXY = Unsafe.As<Vector64<byte>, Vector64<T>>(ref aZXYb);");
      sb.AppendLine("      var bYZX = Unsafe.As<Vector64<byte>, Vector64<T>>(ref bYZXb);");
      sb.AppendLine("      return FromV64(Vector64.Subtract(Vector64.Multiply(aYZX, bZXY), Vector64.Multiply(aZXY, bYZX)));");
      sb.AppendLine("    }");
      sb.AppendLine();
    }

    // Scalar fallback (3D)
    sb.AppendLine("    return new(");
    sb.AppendLine("      left.y * right.z - left.z * right.y,");
    sb.AppendLine("      left.z * right.x - left.x * right.z,");
    sb.AppendLine("      left.x * right.y - left.y * right.x");
    sb.AppendLine("    );");
    sb.AppendLine("  }");
  }
}
