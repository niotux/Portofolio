using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PrimordialPower.Platform;

public class AudioStream
{
  public AudioStream(in AudioFormat format, int sampleCount)
  {
    this.Format = format;
    this.samples = Marshal.AllocHGlobal(format.BitsPerSample * sampleCount);
    unsafe { Span<short> s = new((void*)samples, 3); }
  }

  ~AudioStream()
  {
    Marshal.FreeHGlobal(this.samples);
  }



  private IntPtr samples;
  public ulong SampleCount { get; }
  public AudioFormat Format { get; }
}