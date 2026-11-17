namespace LAN;

public readonly record struct Version
(
  byte Major,
  byte Minor,
  ushort Patch
)
{
  public static Version CurrentVersion { get; } = new(0, 1, 1);
}