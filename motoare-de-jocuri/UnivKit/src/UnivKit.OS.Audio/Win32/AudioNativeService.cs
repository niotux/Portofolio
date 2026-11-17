using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace UnivKit.OS.Audio.Win32;

[SupportedOSPlatform("windows")]
public sealed class AudioNativeService : AudioNativeServiceBase
{
  protected override void WriteSamplesCore(ReadOnlySpan<byte> samples)
  {
    ReadOnlySpan<int> ints = MemoryMarshal.Cast<byte, int>(samples);
    foreach (int sample in ints)
    {
      Console.Beep(sample, 1000 / 4800);
    }
  }
}