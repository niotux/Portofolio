using System.Runtime.InteropServices;

namespace UnivKit.OS.Platform.Linux;

public static partial class LibASound
{
  public const string LibName = "libasound.so.2";


  [LibraryImport(LibName, EntryPoint = "snd_pcm_open", StringMarshalling = StringMarshalling.Utf8)]
  public static partial int SndPcmOpen(out IntPtr pcm, string name, StreamType stream, int mode);


  [LibraryImport(LibName, EntryPoint = "snd_pcm_set_params")]
  public static partial int SndPcmSetParams(IntPtr pcm, Format format, AccessType access, int channels, int rate, int soft_resample, int latency);


  [LibraryImport(LibName, EntryPoint = "snd_pcm_writei")]
  public static partial int SndPcmWrite(IntPtr pcm, Span<byte> buffer, ulong frames);


  [LibraryImport(LibName, EntryPoint = "snd_pcm_drain")]
  public static partial int SndPcmDrain(IntPtr pcm);


  [LibraryImport(LibName, EntryPoint = "snd_pcm_close")]
  public static partial int SndPcmClose(IntPtr pcm);


  [LibraryImport(LibName, EntryPoint = "snd_pcm_prepare")]
  public static partial int SndPcmPrepare(IntPtr pcm);


  [LibraryImport(LibName, EntryPoint = "snd_pcm_recover")]
  public static partial int SndPcmRecover(IntPtr pcm, int err, int silent);


  [LibraryImport(LibName, EntryPoint = "snd_pcm_avail")]
  public static partial int SndPcmAvail(IntPtr pcm);


  [LibraryImport(LibName, EntryPoint = "snd_pcm_wait")]
  public static partial int SndPcmWait(IntPtr pcm, int timeout);



  // Stream Types
  public enum StreamType : int
  {
    PLAYBACK = 0,
    CAPTURE = 1,
  }


  // Access Types
  public enum AccessType : int
  {
    MMAP_INTERLEAVED = 0,
    MMAP_NONINTERLEAVED = 1,
    MMAP_COMPLEX = 2,
    RW_INTERLEAVED = 3,
    RW_NONINTERLEAVED = 4,
  }


  // PCM Formats
  public enum Format : int
  {
    UNKNOWN = -1,

    // 8-bit formats
    S8 = 0,
    U8 = 1,

    // 16-bit formats
    S16_LE = 2,
    S16_BE = 3,
    U16_LE = 4,
    U16_BE = 5,

    // 24-bit formats (stored in 32-bit, lower 3 bytes used)
    S24_LE = 6,
    S24_BE = 7,
    U24_LE = 8,
    U24_BE = 9,

    // 32-bit formats
    S32_LE = 10,
    S32_BE = 11,
    U32_LE = 12,
    U32_BE = 13,

    // Float formats
    FLOAT_LE = 14,
    FLOAT_BE = 15,
    FLOAT64_LE = 16,
    FLOAT64_BE = 17,

    // IEC958 (S/PDIF) formats
    IEC958_SUBFRAME_LE = 18,
    IEC958_SUBFRAME_BE = 19,

    // Compressed/Special formats
    MU_LAW = 20,
    A_LAW = 21,
    IMA_ADPCM = 22,
    MPEG = 23,
    GSM = 24,

    // 20-bit formats (stored in 32-bit)
    S20_LE = 25,
    S20_BE = 26,
    U20_LE = 27,
    U20_BE = 28,

    SPECIAL = 31,

    // 24-bit formats (packed in 3 bytes)
    S24_3LE = 32,
    S24_3BE = 33,
    U24_3LE = 34,
    U24_3BE = 35,

    // 20-bit formats (packed in 3 bytes)
    S20_3LE = 36,
    S20_3BE = 37,
    U20_3LE = 38,
    U20_3BE = 39,

    // 18-bit formats (packed in 3 bytes)
    S18_3LE = 40,
    S18_3BE = 41,
    U18_3LE = 42,
    U18_3BE = 43,
  }
}
