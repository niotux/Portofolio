using System.Text;
using LAN.Assembler.Pipeline2;
using LAN.Logging;

namespace LAN.Assembler;

public static class Keywords
{
  private static readonly Dictionary<SourceView, byte> OpCodes = new()
  {
    { "fun", OpCode.Fun },
    { "ldarg", OpCode.LdArg },
    { "ret", OpCode.Ret },
    { "call", OpCode.Call },
    { "arg", OpCode.Arg },
    { "set", OpCode.Set },
    { "add", OpCode.Add },
    { "sub", OpCode.Sub },
    { "mul", OpCode.Mul },
    { "div", OpCode.Div },
    { "mod", OpCode.Mod },
    { "idiv", OpCode.IDiv },
    { "imod", OpCode.IMod },
    { "fadd", OpCode.FAdd },
    { "fsub", OpCode.FSub },
    { "fmul", OpCode.FMul },
    { "fdiv", OpCode.FDiv },
    { "fmod", OpCode.FMod },
    { "or", OpCode.Or },
    { "and", OpCode.And },
    { "xor", OpCode.Xor },
    { "shl", OpCode.ShL },
    { "shr", OpCode.ShR },
    { "ishr", OpCode.IShR },
    { "not", OpCode.Not },
    { "alloc", OpCode.Alloc },
    { "write", OpCode.Write },
    { "read", OpCode.Read },
    { "gep", OpCode.Gep },
    { "conv", OpCode.Conv },
    { "iconv", OpCode.IConv },
    { "fconv", OpCode.FConv },
    { "itof", OpCode.IToF },
    { "utof", OpCode.UToF },
    { "ftoi", OpCode.FToI },
    { "ftou", OpCode.FToU },
    { "label", OpCode.Label },
    { "jmp", OpCode.Jmp },
    { "jeq", OpCode.Jeq },
    { "jne", OpCode.Jne },
    { "jlt", OpCode.Jlt },
    { "jgt", OpCode.Jgt },
    { "jle", OpCode.Jle },
    { "jge", OpCode.Jge },
    { "ijlt", OpCode.IJlt },
    { "ijgt", OpCode.IJgt },
    { "ijle", OpCode.IJle },
    { "ijge", OpCode.IJge },
    { "fjlt", OpCode.FJlt },
    { "fjgt", OpCode.FJgt },
    { "fjle", OpCode.FJle },
    { "fjge", OpCode.FJge },
    { "inlfun", OpCode.InlFun },
    { "vcall", OpCode.VCall },
    { "varg", OpCode.VArg },
    { "type", OpCode.Type },
    { "meta", OpCode.Meta },
    { "export", OpCode.Export },
    { "module", OpCode.Module },
    { "ldmodule", OpCode.LdModule },
    { "keplersthirdlaw", OpCode.KeplersThirdLaw },
  };

  public static byte GetOpCode(SourceView view)
  {
    if (OpCodes.TryGetValue(view, out byte op))
    {
      return op;
    }
    return OpCode.Unknown;
  }

  private static readonly Dictionary<SourceView, Encoding> StringEncodings = new()
  {
    { Grammar.EncodingAscii, Encoding.ASCII },
    { Grammar.EncodingUtf8, Encoding.UTF8 },
    { Grammar.EncodingUtf16, Encoding.Unicode },
    { Grammar.EncodingUtf32, Encoding.UTF32 },
    { Grammar.EncodingLatin, Encoding.Latin1 },
  };

  public static Encoding? GetEncoding(SourceView view)
  {
    if (StringEncodings.TryGetValue(view, out var encoding))
    {
      return encoding;
    }
    return null;
  }

  private static readonly (string String, uint Radix)[] IntegerBases =
  [
    ( Grammar.BinaryMarker, Grammar.BinaryRadix ),
    ( Grammar.OctalMarker, Grammar.OctalRadix ),
    ( Grammar.HexMarker, Grammar.HexRadix ),
    ( Grammar.DecimalMarker, Grammar.DecimalRadix ),
  ];

  public static uint? GetRadix(ReadOnlySpan<char> source)
  {
    for (uint i = 0; i < IntegerBases.Length; i++)
    {
      var mark = IntegerBases[i].String;
      if (mark.Length == 0)
      {
        return IntegerBases[i].Radix;
      }
      if (source.StartsWith(IntegerBases[i].String))
      {
        return IntegerBases[i].Radix;
      }
    }
    return null;
  }

  // Type markers
  public const string Ptr = "ptr";

  // Boolean literals
  public const string True = "true";
  public const string False = "false";

  // Float keyword literals
  public const string Nan = "nan";
  public const string Inf = "inf";
  public const string NegInf = "neginf";

  private static readonly Dictionary<SourceView, ulong> ValueKeywords = new()
  {
    { True, 0x1 },
    { False, 0x0 },
    { Nan, (ulong)BitConverter.DoubleToInt64Bits(double.NaN) },
    { Inf, (ulong)BitConverter.DoubleToInt64Bits(double.PositiveInfinity) },
    { NegInf, (ulong)BitConverter.DoubleToInt64Bits(double.NegativeInfinity) },
  };

  public static bool TryGetValueKeyword(SourceView view, out ulong bits)
    => ValueKeywords.TryGetValue(view, out bits);
}
