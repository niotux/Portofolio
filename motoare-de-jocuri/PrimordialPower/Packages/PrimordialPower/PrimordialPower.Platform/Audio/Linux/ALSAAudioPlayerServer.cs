using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PrimordialPower.Runtime;

namespace PrimordialPower.Platform.Linux;

[SupportedOSPlatform("linux")]
public class ALSAAudioPlayerServer : AppServer, IAudioPlayer
{
  public override AppServerLifetime Lifetime => AppServerLifetime.Async;

  private readonly IntPtr pcm;
  private readonly string device;
  private readonly AudioFormat format;
  private readonly int latency = 50000;
  private readonly int bufferSampleCount;
  private readonly HashSet<IAudioSource> sources = new();
  public ALSAAudioPlayerServer(in AudioFormat format, int bufferSampleCount)
  {
    this.format = format;
    this.device = "default";
    this.bufferSampleCount = bufferSampleCount;
    int err;

    err = ALSA.SndPcmOpen(out this.pcm, this.device, ALSA.SND_PCM_STREAM_PLAYBACK, 0);
    if (err < 0)
    {
      ALSA.SndPcmClose(this.pcm);
      throw new Exception($"Failed to open audio ALSA device \"{this.device}\"! Error: {err}");
    }

    err = ALSA.SndPcmSetParams(this.pcm, ALSA.ToAlsaFormat(format.SampleFormat), ALSA.SND_PCM_ACCESS_RW_INTERLEAVED, format.Channels, format.SampleRate, 1, this.latency);
    if (err < 0)
    {
      ALSA.SndPcmClose(this.pcm);
      throw new Exception($"Failed to open audio ALSA device \"{this.device}\"! Error: {err}");

    }
  }



  public AudioFormat Format => this.format;



  private void CheckBound(IAudioSource audio)
  {
    if (!this.sources.Contains(audio)) throw new InvalidOperationException("Audio source is not bound to this player");
  }



  private readonly TaskCompletionSource bindingsFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
  public void Begin()
  {
    if (this.bindingsFinished.Task.IsCompleted) return;
    this.bindingsFinished.SetResult();
  }



  public void Bind(IAudioSource audio)
  {
    if (this.bindingsFinished.Task.IsCompleted) throw new InvalidOperationException("Cannot bind audio sources after Begin() is called!");
    this.sources.Add(audio);
  }

  public void Pause(IAudioSource audio)
  {
    CheckBound(audio);
    audio.Pause();
  }

  public void Play(IAudioSource audio)
  {
    CheckBound(audio);
    audio.Play();
  }

  public void PlayFull(IAudioSource audio)
  {

  }

  public Task PlayFullAsync(IAudioSource audio)
  {
    return Task.CompletedTask;
  }



  public void Beep(double frequency, double duration, double volume)
  {

  }

  public Task BeepAsync(double frequency, double duration, double volume)
  {
    return Task.Run(() => Beep(frequency, duration, volume));
  }


  public override async Task RunAsync()
  {
    await this.bindingsFinished.Task;
    this.Run();
  }

  public override void OnStop()
  {
    Begin();
  }

  public override void Run()
  {
    int outBytesPerSample = this.format.BytesPerSample;
    int outSize = this.bufferSampleCount * this.format.BytesPerFrame;

    int maxBytesPerFrame = this.format.BytesPerFrame;
    foreach (var source in this.sources)
    {
      maxBytesPerFrame = int.Max(maxBytesPerFrame, source.Format.BytesPerFrame);
    }

    Span<byte> outBuff = stackalloc byte[outSize];
    Span<byte> srcBuff = stackalloc byte[this.bufferSampleCount * maxBytesPerFrame];

    while (!this.StopRequested)
    {
      outBuff.Clear();

      foreach (IAudioSource source in this.sources)
      {
        if (source.State == AudioState.Paused) continue;

        int readBytes = source.Read(srcBuff.Slice(0, this.bufferSampleCount * source.Format.BytesPerFrame));
        int readSamples = readBytes / source.Format.BytesPerSample;

        if (source.Format.SampleFormat != this.format.SampleFormat)
        {
          AudioFormatConverter.Convert(srcBuff, source.Format, srcBuff, this.format, readSamples);
        }

        int convertedBytes = readSamples * outBytesPerSample;
        switch (this.format.SampleFormat)
        {
          case AudioSampleFormat.Int8:
          case AudioSampleFormat.UInt8:
            Mix8Bit(srcBuff, outBuff, convertedBytes);
            break;

          case AudioSampleFormat.Int16_LE:
          case AudioSampleFormat.UInt16_LE:
            Mix16Bit(srcBuff, outBuff, convertedBytes);
            break;

          case AudioSampleFormat.Int24_LE:
          case AudioSampleFormat.UInt24_LE:
          case AudioSampleFormat.Int32_LE:
          case AudioSampleFormat.UInt32_LE:
            Mix32Bit(srcBuff, outBuff, convertedBytes);
            break;

          case AudioSampleFormat.Single_LE:
            MixSingle(srcBuff, outBuff, convertedBytes);
            break;

          case AudioSampleFormat.Double_LE:
            MixDouble(srcBuff, outBuff, convertedBytes);
            break;
        }
      }

      int err = ALSA.SndPcmWrite(this.pcm, outBuff, (ulong)this.bufferSampleCount);
      if (err < 0)
      {
        err = ALSA.SndPcmRecover(this.pcm, err, 0);
        if (err < 0) throw new Exception("Audio buffer is corrupted!");
        continue;
      }
    }
  }

  public override void Shutdown()
  {
    ALSA.SndPcmDrain(this.pcm);
    ALSA.SndPcmClose(this.pcm);
  }

  private static void Mix8Bit(ReadOnlySpan<byte> source, Span<byte> output, int count)
  {
    for (int i = 0; i < count; i++)
    {
      int mixed = output[i] + source[i];
      output[i] = byte.CreateSaturating(mixed);
    }
  }

  private static void Mix16Bit(ReadOnlySpan<byte> source, Span<byte> output, int count)
  {
    ReadOnlySpan<short> src = MemoryMarshal.Cast<byte, short>(source);
    Span<short> dst = MemoryMarshal.Cast<byte, short>(output);

    int samples = count / 2;
    for (int i = 0; i < samples; i++)
    {
      int mixed = dst[i] + src[i];
      dst[i] = short.CreateSaturating(mixed);
    }
  }

  private static void Mix32Bit(ReadOnlySpan<byte> source, Span<byte> output, int count)
  {
    ReadOnlySpan<int> src = MemoryMarshal.Cast<byte, int>(source);
    Span<int> dst = MemoryMarshal.Cast<byte, int>(output);

    int samples = count / 4;
    for (int i = 0; i < samples; i++)
    {
      long mixed = (long)dst[i] + src[i];
      dst[i] = int.CreateSaturating(mixed);
    }
  }

  private static void MixSingle(ReadOnlySpan<byte> source, Span<byte> output, int count)
  {
    ReadOnlySpan<float> src = MemoryMarshal.Cast<byte, float>(source);
    Span<float> dst = MemoryMarshal.Cast<byte, float>(output);

    int samples = count / 4;
    for (int i = 0; i < samples; i++)
    {
      dst[i] += src[i];
    }
  }

  private static void MixDouble(ReadOnlySpan<byte> source, Span<byte> output, int count)
  {
    ReadOnlySpan<double> src = MemoryMarshal.Cast<byte, double>(source);
    Span<double> dst = MemoryMarshal.Cast<byte, double>(output);

    int samples = count / 8;
    for (int i = 0; i < samples; i++)
    {
      dst[i] += src[i];
    }
  }

}
