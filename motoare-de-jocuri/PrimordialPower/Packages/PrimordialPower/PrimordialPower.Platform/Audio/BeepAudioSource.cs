using System;
using System.Runtime.InteropServices;

namespace PrimordialPower.Platform;

public class BeepAudioSource : IAudioSource
{
  private readonly AudioFormat format;
  private readonly double frequency;
  private readonly int totalSamples;
  private int samplesGenerated;
  private AudioState state;

  public BeepAudioSource(AudioFormat format, double frequency, double durationSeconds)
  {
    this.format = format;
    this.frequency = frequency;
    this.totalSamples = (int)(format.SampleRate * durationSeconds);
    this.samplesGenerated = 0;
    this.state = AudioState.Paused;
  }

  public AudioFormat Format => this.format;
  public AudioState State => this.state;

  public void Play()
  {
    if (this.samplesGenerated >= this.totalSamples)
    {
      this.samplesGenerated = 0;
    }
    this.state = AudioState.Playing;
  }

  public void Pause() => this.state = AudioState.Paused;

  public void Reset()
  {
    this.samplesGenerated = 0;
    this.state = AudioState.Paused;
  }

  public int Read(Span<byte> buffer)
  {
    if (this.state == AudioState.Paused)
    {
      buffer.Clear();
      return buffer.Length;
    }

    int framesRequested = buffer.Length / this.format.BytesPerFrame;
    int framesToGenerate = Math.Min(framesRequested, this.totalSamples - this.samplesGenerated);

    if (framesToGenerate <= 0)
    {
      // Finished playing
      buffer.Clear();
      this.state = AudioState.Paused;
      return buffer.Length;
    }

    // Generate based on format
    switch (this.format.SampleFormat)
    {
      case AudioSampleFormat.Int16_LE:
        GenerateInt16(buffer, framesToGenerate);
        break;
      case AudioSampleFormat.Single_LE:
        GenerateSingle(buffer, framesToGenerate);
        break;
      default:
        throw new NotSupportedException($"Format {this.format.SampleFormat} not implemented in BeepAudioSource");
    }

    this.samplesGenerated += framesToGenerate;

    // Clear remaining buffer if we generated less than requested
    if (framesToGenerate < framesRequested)
    {
      int bytesGenerated = framesToGenerate * this.format.BytesPerFrame;
      buffer.Slice(bytesGenerated).Clear();
    }

    if (buffer.Length == 0) this.Pause();
    return buffer.Length;
  }

  private void GenerateInt16(Span<byte> buffer, int frames)
  {
    Span<short> samples = MemoryMarshal.Cast<byte, short>(buffer);
    double twoPiF = 2.0 * Math.PI * this.frequency;
    double sampleRate = this.format.SampleRate;

    for (int i = 0; i < frames; i++)
    {
      double time = (this.samplesGenerated + i) / sampleRate;
      double value = Math.Sin(twoPiF * time);
      short sample = (short)(value * 16000); // 16000 for ~50% volume

      // Write to all channels
      for (int ch = 0; ch < this.format.Channels; ch++)
      {
        samples[i * this.format.Channels + ch] = sample;
      }
    }
  }

  private void GenerateSingle(Span<byte> buffer, int frames)
  {
    Span<float> samples = MemoryMarshal.Cast<byte, float>(buffer);
    double twoPiF = 2.0 * Math.PI * this.frequency;
    double sampleRate = this.format.SampleRate;

    for (int i = 0; i < frames; i++)
    {
      double time = (this.samplesGenerated + i) / sampleRate;
      float value = (float)(Math.Sin(twoPiF * time) * 0.5); // 50% volume

      // Write to all channels
      for (int ch = 0; ch < this.format.Channels; ch++)
      {
        samples[i * this.format.Channels + ch] = value;
      }
    }
  }
}