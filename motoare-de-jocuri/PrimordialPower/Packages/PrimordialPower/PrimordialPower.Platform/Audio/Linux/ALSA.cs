using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PrimordialPower.Platform.Linux;

[SupportedOSPlatform("linux")]
public static partial class ALSA
{
  [LibraryImport(LibNames.ASound, EntryPoint = "snd_pcm_open", StringMarshalling = StringMarshalling.Utf8)]
  public static partial int SndPcmOpen(out IntPtr pcm, string name, int stream, int mode);


  [LibraryImport(LibNames.ASound, EntryPoint = "snd_pcm_set_params")]
  public static partial int SndPcmSetParams(IntPtr pcm, int format, int access, int channels, int rate, int soft_resample, int latency);


  [LibraryImport(LibNames.ASound, EntryPoint = "snd_pcm_writei")]
  public static partial int SndPcmWrite(IntPtr pcm, Span<byte> buffer, ulong frames);


  [LibraryImport(LibNames.ASound, EntryPoint = "snd_pcm_drain")]
  public static partial int SndPcmDrain(IntPtr pcm);


  [LibraryImport(LibNames.ASound, EntryPoint = "snd_pcm_close")]
  public static partial int SndPcmClose(IntPtr pcm);


  [LibraryImport(LibNames.ASound, EntryPoint = "snd_pcm_prepare")]
  public static partial int SndPcmPrepare(IntPtr pcm);


  [LibraryImport(LibNames.ASound, EntryPoint = "snd_pcm_recover")]
  public static partial int SndPcmRecover(IntPtr pcm, int err, int silent);


  [LibraryImport(LibNames.ASound, EntryPoint = "snd_pcm_avail")]
  public static partial int SndPcmAvail(IntPtr pcm);


  [LibraryImport(LibNames.ASound, EntryPoint = "snd_pcm_wait")]
  public static partial int SndPcmWait(IntPtr pcm, int timeout);



  public static int ToAlsaFormat(AudioSampleFormat format) => format switch
  {
    AudioSampleFormat.Int8 => SND_PCM_FORMAT_S8,
    AudioSampleFormat.UInt8 => SND_PCM_FORMAT_U8,

    AudioSampleFormat.Int16_LE => SND_PCM_FORMAT_S16_LE,
    AudioSampleFormat.UInt16_LE => SND_PCM_FORMAT_U16_LE,
    AudioSampleFormat.Int16_BE => SND_PCM_FORMAT_S16_BE,
    AudioSampleFormat.UInt16_BE => SND_PCM_FORMAT_U16_BE,

    AudioSampleFormat.Int24_LE => SND_PCM_FORMAT_S24_LE,
    AudioSampleFormat.UInt24_LE => SND_PCM_FORMAT_U24_LE,
    AudioSampleFormat.Int24_BE => SND_PCM_FORMAT_S24_BE,
    AudioSampleFormat.UInt24_BE => SND_PCM_FORMAT_U24_BE,

    AudioSampleFormat.Int32_LE => SND_PCM_FORMAT_S32_LE,
    AudioSampleFormat.UInt32_LE => SND_PCM_FORMAT_U32_LE,
    AudioSampleFormat.Int32_BE => SND_PCM_FORMAT_S32_BE,
    AudioSampleFormat.UInt32_BE => SND_PCM_FORMAT_U32_BE,

    AudioSampleFormat.Single_LE => SND_PCM_FORMAT_FLOAT_LE,
    AudioSampleFormat.Single_BE => SND_PCM_FORMAT_FLOAT_BE,
    AudioSampleFormat.Double_LE => SND_PCM_FORMAT_FLOAT64_LE,
    AudioSampleFormat.Double_BE => SND_PCM_FORMAT_FLOAT64_BE,

    _ => throw new ArgumentOutOfRangeException(nameof(format))
  };



  // Stream Types
  public const int SND_PCM_STREAM_PLAYBACK = 0;
  public const int SND_PCM_STREAM_CAPTURE = 1;

  // Access Types
  public const int SND_PCM_ACCESS_MMAP_INTERLEAVED = 0;
  public const int SND_PCM_ACCESS_MMAP_NONINTERLEAVED = 1;
  public const int SND_PCM_ACCESS_MMAP_COMPLEX = 2;
  public const int SND_PCM_ACCESS_RW_INTERLEAVED = 3;
  public const int SND_PCM_ACCESS_RW_NONINTERLEAVED = 4;

  // Format Constants - Standard PCM Formats
  public const int SND_PCM_FORMAT_UNKNOWN = -1;

  // 8-bit formats
  public const int SND_PCM_FORMAT_S8 = 0;          // Signed 8-bit
  public const int SND_PCM_FORMAT_U8 = 1;          // Unsigned 8-bit

  // 16-bit formats
  public const int SND_PCM_FORMAT_S16_LE = 2;      // Signed 16-bit Little Endian
  public const int SND_PCM_FORMAT_S16_BE = 3;      // Signed 16-bit Big Endian
  public const int SND_PCM_FORMAT_U16_LE = 4;      // Unsigned 16-bit Little Endian
  public const int SND_PCM_FORMAT_U16_BE = 5;      // Unsigned 16-bit Big Endian

  // 24-bit formats (stored in 32-bit, lower 3 bytes used)
  public const int SND_PCM_FORMAT_S24_LE = 6;      // Signed 24-bit Little Endian
  public const int SND_PCM_FORMAT_S24_BE = 7;      // Signed 24-bit Big Endian
  public const int SND_PCM_FORMAT_U24_LE = 8;      // Unsigned 24-bit Little Endian
  public const int SND_PCM_FORMAT_U24_BE = 9;      // Unsigned 24-bit Big Endian

  // 32-bit formats
  public const int SND_PCM_FORMAT_S32_LE = 10;     // Signed 32-bit Little Endian
  public const int SND_PCM_FORMAT_S32_BE = 11;     // Signed 32-bit Big Endian
  public const int SND_PCM_FORMAT_U32_LE = 12;     // Unsigned 32-bit Little Endian
  public const int SND_PCM_FORMAT_U32_BE = 13;     // Unsigned 32-bit Big Endian

  // Float formats
  public const int SND_PCM_FORMAT_FLOAT_LE = 14;   // 32-bit float Little Endian
  public const int SND_PCM_FORMAT_FLOAT_BE = 15;   // 32-bit float Big Endian
  public const int SND_PCM_FORMAT_FLOAT64_LE = 16; // 64-bit float Little Endian
  public const int SND_PCM_FORMAT_FLOAT64_BE = 17; // 64-bit float Big Endian

  // IEC958 (S/PDIF) formats
  public const int SND_PCM_FORMAT_IEC958_SUBFRAME_LE = 18;
  public const int SND_PCM_FORMAT_IEC958_SUBFRAME_BE = 19;

  // Compressed/Special formats
  public const int SND_PCM_FORMAT_MU_LAW = 20;     // Mu-Law
  public const int SND_PCM_FORMAT_A_LAW = 21;      // A-Law
  public const int SND_PCM_FORMAT_IMA_ADPCM = 22;  // IMA ADPCM
  public const int SND_PCM_FORMAT_MPEG = 23;       // MPEG
  public const int SND_PCM_FORMAT_GSM = 24;        // GSM

  // 20-bit formats (stored in 32-bit)
  public const int SND_PCM_FORMAT_S20_LE = 25;     // Signed 20-bit Little Endian
  public const int SND_PCM_FORMAT_S20_BE = 26;     // Signed 20-bit Big Endian
  public const int SND_PCM_FORMAT_U20_LE = 27;     // Unsigned 20-bit Little Endian
  public const int SND_PCM_FORMAT_U20_BE = 28;     // Unsigned 20-bit Big Endian

  public const int SND_PCM_FORMAT_SPECIAL = 31;

  // 24-bit formats (packed in 3 bytes)
  public const int SND_PCM_FORMAT_S24_3LE = 32;    // Signed 24-bit Little Endian (3 bytes)
  public const int SND_PCM_FORMAT_S24_3BE = 33;    // Signed 24-bit Big Endian (3 bytes)
  public const int SND_PCM_FORMAT_U24_3LE = 34;    // Unsigned 24-bit Little Endian (3 bytes)
  public const int SND_PCM_FORMAT_U24_3BE = 35;    // Unsigned 24-bit Big Endian (3 bytes)

  // 20-bit formats (packed in 3 bytes)
  public const int SND_PCM_FORMAT_S20_3LE = 36;    // Signed 20-bit Little Endian (3 bytes)
  public const int SND_PCM_FORMAT_S20_3BE = 37;    // Signed 20-bit Big Endian (3 bytes)
  public const int SND_PCM_FORMAT_U20_3LE = 38;    // Unsigned 20-bit Little Endian (3 bytes)
  public const int SND_PCM_FORMAT_U20_3BE = 39;    // Unsigned 20-bit Big Endian (3 bytes)

  // 18-bit formats (packed in 3 bytes)
  public const int SND_PCM_FORMAT_S18_3LE = 40;    // Signed 18-bit Little Endian (3 bytes)
  public const int SND_PCM_FORMAT_S18_3BE = 41;    // Signed 18-bit Big Endian (3 bytes)
  public const int SND_PCM_FORMAT_U18_3LE = 42;    // Unsigned 18-bit Little Endian (3 bytes)
  public const int SND_PCM_FORMAT_U18_3BE = 43;
}