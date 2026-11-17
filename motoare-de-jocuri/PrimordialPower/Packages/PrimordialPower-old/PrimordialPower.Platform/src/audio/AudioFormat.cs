using System.Numerics;

namespace PrimordialPower.Platform;

public struct AudioFormat
{
  public required int SampleRate { get; init; }
  public required int Channels { get; init; }
  public required int BitsPerSample { get; init; }
  public required bool IsFloat { get; init; }
}