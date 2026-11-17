using N = NLayer;
using System.Runtime.InteropServices;

namespace PrimordialPower.Platform;

public class Mp3AudioSource : IAudioSource, IDisposable
{
  private readonly N.MpegFile mpegFile;
  private readonly AudioFormat format;
  private AudioState state;
  private readonly float[] floatBuffer;
  private int floatBufferPos;
  private int floatBufferCount;

  public Mp3AudioSource(string filePath)
  {
    if (File.Exists(filePath))
    {
      this.mpegFile = new(filePath);
    }

    // NLayer outputs: SampleRate, Channels (1 or 2)
    this.format = new AudioFormat
    {
      SampleRate = this.mpegFile?.SampleRate ?? 1,
      Channels = this.mpegFile?.Channels ?? 1,
      SampleFormat = AudioSampleFormat.Int16_LE,
    };

    this.mpegFile ??= null!;

    this.state = AudioState.Paused;
    this.floatBuffer = new float[8192]; // Internal buffer for NLayer output
    this.floatBufferPos = 0;
    this.floatBufferCount = 0;
  }

  public AudioFormat Format => this.format;
  public AudioState State => this.state;

  public void Play() => this.state = AudioState.Playing;
  public void Pause() => this.state = AudioState.Paused;

  public void Reset()
  {
    // NLayer doesn't support seeking in the free version easily
    // You'd need to recreate the MpegFile or use a stream that supports seeking
    this.floatBufferPos = 0;
    this.floatBufferCount = 0;
    this.state = AudioState.Paused;
  }

  public int Read(Span<byte> buffer)
  {
    if (this.state == AudioState.Paused)
    {
      buffer.Clear();
      return buffer.Length;
    }

    int bytesNeeded = buffer.Length;
    int samplesNeeded = bytesNeeded / 2; // Int16 = 2 bytes per sample
    Span<short> output = MemoryMarshal.Cast<byte, short>(buffer);
    int samplesWritten = 0;

    while (samplesWritten < samplesNeeded)
    {
      // Do we have buffered data?
      if (this.floatBufferPos >= this.floatBufferCount)
      {
        // Need to read more from NLayer
        this.floatBufferCount = this.mpegFile.ReadSamples(this.floatBuffer, 0, this.floatBuffer.Length);
        this.floatBufferPos = 0;

        if (this.floatBufferCount == 0)
        {
          // End of file
          this.state = AudioState.Paused;
          break;
        }
      }

      // Convert float to int16
      int samplesToConvert = Math.Min(samplesNeeded - samplesWritten, this.floatBufferCount - this.floatBufferPos);
      for (int i = 0; i < samplesToConvert; i++)
      {
        float sample = this.floatBuffer[this.floatBufferPos++];
        output[samplesWritten++] = (short)(Math.Clamp(sample, -1f, 1f) * 32767f);
      }
    }

    // Fill remaining with silence if we hit EOF
    while (samplesWritten < samplesNeeded)
    {
      output[samplesWritten++] = 0;
    }

    return buffer.Length;
  }

  public void Dispose()
  {
    this.Pause();
    this.mpegFile?.Dispose();
  }
}