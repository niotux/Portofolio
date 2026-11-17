using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using UnivKit.Application;
using UnivKit.Audio;

namespace UnivKit.OS.Audio;

public abstract class AudioNativeServiceBase : IService, IAudioManager
{
  protected const int StackBufferSize = 8192;
  //private readonly AsyncGate gate = new();
  private readonly ManualResetEventSlim gate = new();
  public event AudioSampleMixer<int>? Mixer;
  public Task Execute(CancellationToken cancellationToken)
  {
    cancellationToken.Register(gate.Set);

    Span<byte> stagingBuff = stackalloc byte[StackBufferSize];
    Span<byte> mixBuff = stackalloc byte[StackBufferSize];

    while (!cancellationToken.IsCancellationRequested)
    {
      gate.Wait();
      var e = Mixer;
      if (e is null)
      {
        foreach (var audio in this.sources)
        {
          if (audio.State != AudioState.Playing) continue;

          int readBytes = audio.Read(stagingBuff);
          Span<int> staging = MemoryMarshal.Cast<byte, int>(stagingBuff);
          Span<int> mixed = MemoryMarshal.Cast<byte, int>(mixBuff);

          for (int i = 0; i < readBytes / sizeof(int); i++)
          {
            mixed[i] += staging[i];
          }
        }
      }
      else
      {
        foreach (var audio in this.sources)
        {
          if (audio.State != AudioState.Playing) continue;

          int readBytes = audio.Read(stagingBuff);
          Span<int> staging = MemoryMarshal.Cast<byte, int>(stagingBuff);
          Span<int> mixed = MemoryMarshal.Cast<byte, int>(mixBuff);

          for (int i = 0; i < readBytes / sizeof(int); i++)
          {
            mixed[i] = e(mixed[i], staging[i], audio);
          }
        }
      }
    }
    return Task.CompletedTask;
  }


  protected abstract void WriteSamplesCore(ReadOnlySpan<byte> samples);


  private readonly ConcurrentBag<IAudioSource> sources = new();
  public void Bind(IAudioSource source)
  {
    throw new NotImplementedException();
  }
}