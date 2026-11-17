namespace LAN;

public static class OpCode
{
  public const byte Unknown = 0x00;

  public const byte Fun = 0x01;
  public const byte LdArg = 0x02;
  public const byte Ret = 0x03;
  public const byte Call = 0x04;
  public const byte Arg = 0x05;

  public const byte Set = 0x06;

  public const byte Add = 0x07;
  public const byte Sub = 0x08;
  public const byte Mul = 0x09;
  public const byte Div = 0x0A;
  public const byte Mod = 0x0B;

  public const byte IDiv = 0x0C;
  public const byte IMod = 0x0D;

  public const byte FAdd = 0x0E;
  public const byte FSub = 0x0F;
  public const byte FMul = 0x10;
  public const byte FDiv = 0x11;
  public const byte FMod = 0x12;

  public const byte Or = 0x13;
  public const byte And = 0x14;
  public const byte Xor = 0x15;
  public const byte ShL = 0x16;
  public const byte ShR = 0x17;
  public const byte IShR = 0x18;
  public const byte Not = 0x19;

  public const byte Alloc = 0x1A;
  public const byte Write = 0x1B;
  public const byte Read = 0x1C;
  public const byte Gep = 0x1D;

  public const byte Conv = 0x1E;
  public const byte IConv = 0x1F;
  public const byte FConv = 0x20;
  public const byte IToF = 0x21;
  public const byte UToF = 0x22;
  public const byte FToI = 0x23;
  public const byte FToU = 0x24;

  public const byte Label = 0x25;
  public const byte Jmp = 0x26;
  public const byte Jeq = 0x27;
  public const byte Jne = 0x28;
  public const byte Jlt = 0x29;
  public const byte Jgt = 0x2A;
  public const byte Jle = 0x2B;
  public const byte Jge = 0x2C;
  public const byte IJlt = 0x2D;
  public const byte IJgt = 0x2E;
  public const byte IJle = 0x2F;
  public const byte IJge = 0x30;
  public const byte FJlt = 0x31;
  public const byte FJgt = 0x32;
  public const byte FJle = 0x33;
  public const byte FJge = 0x34;

  public const byte InlFun = 0x35;
  public const byte VCall = 0x36;
  public const byte VArg = 0x37;

  public const byte Type = 0x38;

  public const byte Meta = 0x39;
  public const byte Export = 0x3A;

  public const byte Module = 0x3B;
  public const byte LdModule = 0x3C;

  public const byte KeplersThirdLaw = 0x3D;
}