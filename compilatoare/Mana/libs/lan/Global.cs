namespace LAN.Bytecode;

public static class GlobalVisibility
{
  public const byte Private = 0x0;
  public const byte Public = 0x1;
  public const byte Protected = 0x2;
  public const byte Internal = 0x3;
}

public readonly record struct Global
(
  uint DeclarationInstruction
);