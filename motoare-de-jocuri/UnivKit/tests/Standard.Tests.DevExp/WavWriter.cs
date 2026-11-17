using System.Buffers.Binary;
using UnivKit.Audio;

internal static class WavWriter
{
  private const ushort FormatPcm = 1;
  private const ushort FormatIeeeFloat = 3;

  public static void Write(Stream dest, IAudioSource pcm, AudioFormat format)
  {
    if (format.SampleLayout != AudioSampleLayout.Interleaved)
    {
      throw new ArgumentException("WAV requires interleaved samples.", nameof(format));
    }

    (ushort formatTag, ushort bitsPerSample) = MapFormat(format.SampleFormat);
    ushort channels = (ushort)format.Channels;
    uint sampleRate = format.SampleRate;
    ushort blockAlign = (ushort)(channels * (bitsPerSample / 8));
    uint byteRate = sampleRate * blockAlign;

    using Stream output = dest;

    long headerEnd;
    long dataStart;

    Span<byte> buf = stackalloc byte[44];

    // RIFF header (we'll patch sizes at the end).
    "RIFF"u8.CopyTo(buf[..4]);
    BinaryPrimitives.WriteUInt32LittleEndian(buf.Slice(4, 4), 0);
    "WAVE"u8.CopyTo(buf.Slice(8, 4));

    // fmt chunk.
    "fmt "u8.CopyTo(buf.Slice(12, 4));
    BinaryPrimitives.WriteUInt32LittleEndian(buf.Slice(16, 4), 16);
    BinaryPrimitives.WriteUInt16LittleEndian(buf.Slice(20, 2), formatTag);
    BinaryPrimitives.WriteUInt16LittleEndian(buf.Slice(22, 2), channels);
    BinaryPrimitives.WriteUInt32LittleEndian(buf.Slice(24, 4), sampleRate);
    BinaryPrimitives.WriteUInt32LittleEndian(buf.Slice(28, 4), byteRate);
    BinaryPrimitives.WriteUInt16LittleEndian(buf.Slice(32, 2), blockAlign);
    BinaryPrimitives.WriteUInt16LittleEndian(buf.Slice(34, 2), bitsPerSample);

    // data chunk.
    "data"u8.CopyTo(buf.Slice(36, 4));
    BinaryPrimitives.WriteUInt32LittleEndian(buf.Slice(40, 4), 0);

    output.Write(buf);
    dataStart = output.Position;

    byte[] copyBuffer = new byte[64 * 1024];
    long dataBytes = 0;
    int read;
    while ((read = pcm.Read(copyBuffer)) > 0)
    {
      output.Write(copyBuffer, 0, read);
      dataBytes += read;
    }

    headerEnd = output.Position;

    // Patch data size.
    output.Position = dataStart - 4;
    Span<byte> size = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)dataBytes);
    output.Write(size);

    // Patch RIFF size (file size - 8).
    output.Position = 4;
    BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)(headerEnd - 8));
    output.Write(size);

    output.Position = headerEnd;
  }

  private static (ushort Tag, ushort Bits) MapFormat(AudioSampleFormat fmt) => fmt switch
  {
    AudioSampleFormat.Byte => (FormatPcm, (ushort)8),
    AudioSampleFormat.Int16 => (FormatPcm, (ushort)16),
    AudioSampleFormat.Int32 => (FormatPcm, (ushort)32),
    AudioSampleFormat.Single => (FormatIeeeFloat, (ushort)32),
    AudioSampleFormat.Double => (FormatIeeeFloat, (ushort)64),
    _ => throw new NotSupportedException($"WAV does not support {fmt}.")
  };
}
