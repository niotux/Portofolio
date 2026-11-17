using System.Runtime.InteropServices;

namespace QuantiX.Engine.Audio;

/// <summary>
/// Converts audio samples between any two sample formats.
/// Supports in-place conversion (overlapping src/dst) by iterating
/// backwards when the destination sample size is larger than the source.
/// </summary>
public static class AudioFormatConverter
{
  /// <summary>
  /// Convert <paramref name="sampleCount"/> individual samples from
  /// <paramref name="srcFmt"/> to <paramref name="dstFmt"/>.
  /// <paramref name="src"/> and <paramref name="dst"/> may be the same buffer.
  /// </summary>
  public static void Convert(ReadOnlySpan<byte> src, AudioFormat srcFmt, Span<byte> dst, AudioFormat dstFmt, int sampleCount)
  {
    if (srcFmt.SampleFormat == dstFmt.SampleFormat)
    {
      int bytes = sampleCount * srcFmt.BytesPerSample;
      src.Slice(0, bytes).CopyTo(dst);
      return;
    }

    if (TryConvertFast(src, srcFmt.SampleFormat, dst, dstFmt.SampleFormat, sampleCount))
    {
      return;
    }

    if (dstFmt.BytesPerSample > srcFmt.BytesPerSample)
    {
      for (int i = sampleCount - 1; i >= 0; i--)
      {
        EncodeSample(dst, dstFmt.SampleFormat, i, DecodeSample(src, srcFmt.SampleFormat, i));
      }
    }
    else
    {
      for (int i = 0; i < sampleCount; i++)
      {
        EncodeSample(dst, dstFmt.SampleFormat, i, DecodeSample(src, srcFmt.SampleFormat, i));
      }
    }
  }

  private static bool TryConvertFast(ReadOnlySpan<byte> src, AudioSampleFormat srcFmt, Span<byte> dst, AudioSampleFormat dstFmt, int count)
  {
    switch ((srcFmt, dstFmt))
    {
      // Int8 ↔ Int16 (shift)
      case (AudioSampleFormat.Int8, AudioSampleFormat.Int16_LE):
        {
          Span<short> d = MemoryMarshal.Cast<byte, short>(dst);
          for (int i = count - 1; i >= 0; i--)
          {
            d[i] = (short)((sbyte)src[i] << 8);
          }
          return true;
        }
      case (AudioSampleFormat.Int16_LE, AudioSampleFormat.Int8):
        {
          ReadOnlySpan<short> s = MemoryMarshal.Cast<byte, short>(src);
          for (int i = 0; i < count; i++)
          {
            dst[i] = (byte)(sbyte)(s[i] >> 8);
          }
          return true;
        }

      // UInt8 - Int16 (bias + shift)
      case (AudioSampleFormat.UInt8, AudioSampleFormat.Int16_LE):
        {
          Span<short> d = MemoryMarshal.Cast<byte, short>(dst);
          for (int i = count - 1; i >= 0; i--)
          {
            d[i] = (short)((src[i] - 128) << 8);
          }
          return true;
        }
      case (AudioSampleFormat.Int16_LE, AudioSampleFormat.UInt8):
        {
          ReadOnlySpan<short> s = MemoryMarshal.Cast<byte, short>(src);
          for (int i = 0; i < count; i++)
          {
            dst[i] = (byte)((s[i] >> 8) + 128);
          }
          return true;
        }

      // Int8 - UInt8 (bias only, same size)
      case (AudioSampleFormat.Int8, AudioSampleFormat.UInt8):
        {
          for (int i = 0; i < count; i++)
          {
            dst[i] = (byte)(src[i] + 128);
          }
          return true;
        }
      case (AudioSampleFormat.UInt8, AudioSampleFormat.Int8):
        {
          for (int i = 0; i < count; i++)
          {
            dst[i] = (byte)(src[i] - 128);
          }
          return true;
        }

      // Int16 - Int32 (shift)
      case (AudioSampleFormat.Int16_LE, AudioSampleFormat.Int32_LE):
        {
          ReadOnlySpan<short> s = MemoryMarshal.Cast<byte, short>(src);
          Span<int> d = MemoryMarshal.Cast<byte, int>(dst);
          for (int i = count - 1; i >= 0; i--)
          {
            d[i] = s[i] << 16;
          }
          return true;
        }
      case (AudioSampleFormat.Int32_LE, AudioSampleFormat.Int16_LE):
        {
          ReadOnlySpan<int> s = MemoryMarshal.Cast<byte, int>(src);
          Span<short> d = MemoryMarshal.Cast<byte, short>(dst);
          for (int i = 0; i < count; i++)
          {
            d[i] = short.CreateSaturating(s[i] >> 16);
          }
          return true;
        }

      // Int16 - Single (scale)
      case (AudioSampleFormat.Int16_LE, AudioSampleFormat.Single_LE):
        {
          ReadOnlySpan<short> s = MemoryMarshal.Cast<byte, short>(src);
          Span<float> d = MemoryMarshal.Cast<byte, float>(dst);
          for (int i = count - 1; i >= 0; i--)
          {
            d[i] = s[i] / 32768f;
          }
          return true;
        }
      case (AudioSampleFormat.Single_LE, AudioSampleFormat.Int16_LE):
        {
          ReadOnlySpan<float> s = MemoryMarshal.Cast<byte, float>(src);
          Span<short> d = MemoryMarshal.Cast<byte, short>(dst);
          for (int i = 0; i < count; i++)
          {
            d[i] = short.CreateSaturating(s[i] * 32768f);
          }
          return true;
        }

      // Int32 - Single (same size, scale)
      case (AudioSampleFormat.Int32_LE, AudioSampleFormat.Single_LE):
        {
          ReadOnlySpan<int> s = MemoryMarshal.Cast<byte, int>(src);
          Span<float> d = MemoryMarshal.Cast<byte, float>(dst);
          for (int i = 0; i < count; i++)
          {
            d[i] = s[i] / 2147483648f;
          }
          return true;
        }
      case (AudioSampleFormat.Single_LE, AudioSampleFormat.Int32_LE):
        {
          ReadOnlySpan<float> s = MemoryMarshal.Cast<byte, float>(src);
          Span<int> d = MemoryMarshal.Cast<byte, int>(dst);
          for (int i = 0; i < count; i++)
          {
            d[i] = int.CreateSaturating(s[i] * 2147483648f);
          }
          return true;
        }

      // Single - Double
      case (AudioSampleFormat.Single_LE, AudioSampleFormat.Double_LE):
        {
          ReadOnlySpan<float> s = MemoryMarshal.Cast<byte, float>(src);
          Span<double> d = MemoryMarshal.Cast<byte, double>(dst);
          for (int i = count - 1; i >= 0; i--)
          {
            d[i] = s[i];
          }
          return true;
        }
      case (AudioSampleFormat.Double_LE, AudioSampleFormat.Single_LE):
        {
          ReadOnlySpan<double> s = MemoryMarshal.Cast<byte, double>(src);
          Span<float> d = MemoryMarshal.Cast<byte, float>(dst);
          for (int i = 0; i < count; i++)
          {
            d[i] = (float)s[i];
          }
          return true;
        }

      // Int16 - Double
      case (AudioSampleFormat.Int16_LE, AudioSampleFormat.Double_LE):
        {
          ReadOnlySpan<short> s = MemoryMarshal.Cast<byte, short>(src);
          Span<double> d = MemoryMarshal.Cast<byte, double>(dst);
          for (int i = count - 1; i >= 0; i--)
          {
            d[i] = s[i] / 32768.0;
          }
          return true;
        }
      case (AudioSampleFormat.Double_LE, AudioSampleFormat.Int16_LE):
        {
          ReadOnlySpan<double> s = MemoryMarshal.Cast<byte, double>(src);
          Span<short> d = MemoryMarshal.Cast<byte, short>(dst);
          for (int i = 0; i < count; i++)
          {
            d[i] = short.CreateSaturating(s[i] * 32768.0);
          }
          return true;
        }

      default:
        return false;
    }
  }


  private static double DecodeSample(ReadOnlySpan<byte> data, AudioSampleFormat fmt, int i) => fmt switch
  {
    AudioSampleFormat.Int8 => (sbyte)data[i] / 128.0,
    AudioSampleFormat.UInt8 => (data[i] - 128) / 128.0,
    AudioSampleFormat.Int16_LE => MemoryMarshal.Cast<byte, short>(data)[i] / 32768.0,
    AudioSampleFormat.UInt16_LE => (MemoryMarshal.Cast<byte, ushort>(data)[i] - 32768) / 32768.0,
    AudioSampleFormat.Int24_LE => ((MemoryMarshal.Cast<byte, int>(data)[i] << 8) >> 8) / 8388608.0,
    AudioSampleFormat.UInt24_LE => ((MemoryMarshal.Cast<byte, int>(data)[i] & 0xFFFFFF) - 8388608) / 8388608.0,
    AudioSampleFormat.Int32_LE => MemoryMarshal.Cast<byte, int>(data)[i] / 2147483648.0,
    AudioSampleFormat.UInt32_LE => (MemoryMarshal.Cast<byte, uint>(data)[i] - 2147483648.0) / 2147483648.0,
    AudioSampleFormat.Single_LE => MemoryMarshal.Cast<byte, float>(data)[i],
    AudioSampleFormat.Double_LE => MemoryMarshal.Cast<byte, double>(data)[i],
    _ => throw new ArgumentOutOfRangeException(nameof(fmt))
  };


  private static void EncodeSample(Span<byte> data, AudioSampleFormat fmt, int i, double val)
  {
    switch (fmt)
    {
      case AudioSampleFormat.Int8:
        data[i] = (byte)sbyte.CreateSaturating(val * 128.0);
        break;
      case AudioSampleFormat.UInt8:
        data[i] = byte.CreateSaturating(val * 128.0 + 128.0);
        break;
      case AudioSampleFormat.Int16_LE:
        MemoryMarshal.Cast<byte, short>(data)[i] = short.CreateSaturating(val * 32768.0);
        break;
      case AudioSampleFormat.UInt16_LE:
        MemoryMarshal.Cast<byte, ushort>(data)[i] = ushort.CreateSaturating(val * 32768.0 + 32768.0);
        break;
      case AudioSampleFormat.Int24_LE:
        MemoryMarshal.Cast<byte, int>(data)[i] = int.CreateSaturating(val * 8388608.0) & 0xFFFFFF;
        break;
      case AudioSampleFormat.UInt24_LE:
        MemoryMarshal.Cast<byte, int>(data)[i] = int.CreateSaturating(val * 8388608.0 + 8388608.0) & 0xFFFFFF;
        break;
      case AudioSampleFormat.Int32_LE:
        MemoryMarshal.Cast<byte, int>(data)[i] = int.CreateSaturating(val * 2147483648.0);
        break;
      case AudioSampleFormat.UInt32_LE:
        MemoryMarshal.Cast<byte, uint>(data)[i] = uint.CreateSaturating(val * 2147483648.0 + 2147483648.0);
        break;
      case AudioSampleFormat.Single_LE:
        MemoryMarshal.Cast<byte, float>(data)[i] = (float)val;
        break;
      case AudioSampleFormat.Double_LE:
        MemoryMarshal.Cast<byte, double>(data)[i] = val;
        break;
    }
  }
}
