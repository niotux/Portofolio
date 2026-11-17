# AudioStream Rewrite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rewrite `AudioStream` to correctly decode audio from any FFmpeg-supported format into block-aligned interleaved PCM bytes, with optional format conversion via SwrContext, partial frame buffering, proper seeking, and safe resource management.

**Architecture:** `AudioStream` extends `Stream` and wraps FFmpeg's decode pipeline (`AVFormatContext` → `AVCodecContext` → `AVFrame`). When the caller requests a different output format (or the native format is planar), a `SwrContext` handles all conversion (resampling, channel mixing, sample format, planar→interleaved) so `CopyFromCurrentFrame` always does a simple flat copy. A `frameSampleOffset` field tracks how far into the current decoded frame we've consumed, preventing data loss across `Read()` calls. All byte positions snap to block boundaries (`Format.BytesPerSample`).

**Tech Stack:** .NET 8, C# unsafe, FFmpeg.AutoGen 3.3.3.2

**Spec:** See `src/UnivKit.Assets.Common/AudioStream.spec.md`

---

### Task 1: Fix AudioFormat.cs

**Files:**
- Modify: `src/UnivKit.Assets.Common/AudioFormat.cs:10-18`

- [ ] **Step 1: Fix Int24 ByteDepth and throw on unknown format**

`Int24` currently falls to the default case and returns `2` (wrong — should be `3`). The default case silently returns a wrong value — it should throw.

Replace the `ByteDepth` property:

```csharp
public uint ByteDepth => this.SampleFormat switch
{
  AudioSampleFormat.Byte => 1,
  AudioSampleFormat.Int16 => 2,
  AudioSampleFormat.Int24 => 3,
  AudioSampleFormat.Int32 => 4,
  AudioSampleFormat.Single => 4,
  AudioSampleFormat.Int64 => 8,
  AudioSampleFormat.Double => 8,
  _ => throw new NotSupportedException($"Unsupported AudioSampleFormat: {this.SampleFormat}")
};
```

- [ ] **Step 2: Build**

```bash
dotnet build src/UnivKit.Assets.Common/UnivKit.Assets.Common.csproj
```

Expected: success.

- [ ] **Step 3: Commit**

```bash
git add src/UnivKit.Assets.Common/AudioFormat.cs
git commit -m "fix: correct Int24 ByteDepth and throw on unknown format"
```

---

### Task 2: AudioStream scaffolding — namespace, fields, properties, stubs

This replaces the entire file with the correct structure. Everything compiles but `Read` and `Seek` throw `NotImplementedException` (filled in by later tasks).

**Files:**
- Modify: `src/UnivKit.Assets.Common/AudioStream.cs` (full rewrite)

- [ ] **Step 1: Write the class shell**

Replace the entire file with:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace UnivKit.Assets.Common;

public sealed class AudioStream : Stream
{
  // ── Source ──
  private readonly Stream source;

  // ── FFmpeg contexts ──
  private unsafe AVFormatContext* formatCtx;
  private unsafe AVIOContext* ioCtx;
  private unsafe AVCodecContext* codecCtx;
  private unsafe AVPacket* packet;
  private unsafe AVFrame* frame;
  private unsafe SwrContext* swrCtx;

  // ── AVIO buffer ──
  private unsafe byte* ioBuffer;
  private const int IoBufferSize = 4096;

  // ── Prevent GC of delegates ──
  private readonly avio_alloc_context_read_packet readCallback;
  private readonly avio_alloc_context_seek seekCallback;

  // ── Stream info ──
  private int audioStreamIndex = -1;
  private readonly AudioFormat nativeFormat;

  // ── Frame buffer state ──
  private int frameSampleOffset;   // samples consumed from current decoded frame
  private int frameSampleCount;    // total samples in current frame (post-conversion)
  private bool hasFrame;           // current frame has unconsumed data

  // ── Converted sample buffer (when SwrContext is active) ──
  private unsafe byte* convertedBuffer;
  private int convertedBufferCapacity;  // in samples

  // ── Position / EOF ──
  private long position;
  private bool reachedEof;
  private bool sentFlushPacket;

  // ── Disposal ──
  private bool disposed;

  // ── Constants ──
  private const int AvErrorEof = -1094995501;
  private const int AvErrorEagain = -11;


  // ═══════════════════════════════════════
  //  Public properties
  // ═══════════════════════════════════════

  public AudioFormat Format { get; }
  public TimeSpan Duration { get; }

  public override bool CanRead => !this.disposed;
  public override bool CanSeek => this.source.CanSeek;
  public override bool CanWrite => false;

  public override long Length
  {
    get
    {
      if (this.Duration <= TimeSpan.Zero)
      {
        return -1;
      }
      return (long)(this.Duration.TotalSeconds * this.Format.SampleRate)
        * (long)this.Format.BytesPerSample;
    }
  }

  public override long Position
  {
    get => this.position;
    set => this.Seek(value, SeekOrigin.Begin);
  }


  // ═══════════════════════════════════════
  //  Constructor (placeholder — Task 3 + 4)
  // ═══════════════════════════════════════

  public unsafe AudioStream(Stream source, AudioFormat? format = null)
  {
    this.source = source;
    this.readCallback = this.ReadCallback;
    this.seekCallback = this.SeekCallback;
    // Filled in by Task 3 + 4
    this.nativeFormat = null!;
    this.Format = null!;
    throw new NotImplementedException("Task 3");
  }


  // ═══════════════════════════════════════
  //  Stream overrides — not supported
  // ═══════════════════════════════════════

  public override void Write(byte[] buffer, int offset, int count)
  {
    throw new NotSupportedException();
  }

  public override void SetLength(long value)
  {
    throw new NotSupportedException();
  }

  public override void Flush()
  {
  }


  // ═══════════════════════════════════════
  //  Read (placeholder — Task 5)
  // ═══════════════════════════════════════

  public override int Read(byte[] buffer, int offset, int count)
  {
    return this.Read(buffer.AsSpan(offset, count));
  }

  public override int Read(Span<byte> buffer)
  {
    throw new NotImplementedException("Task 5");
  }


  // ═══════════════════════════════════════
  //  Seek (placeholder — Task 6)
  // ═══════════════════════════════════════

  public override long Seek(long offset, SeekOrigin origin)
  {
    throw new NotImplementedException("Task 6");
  }

  public long Seek(TimeSpan timestamp)
  {
    throw new NotImplementedException("Task 6");
  }


  // ═══════════════════════════════════════
  //  AVIO callbacks
  // ═══════════════════════════════════════

  private unsafe int ReadCallback(void* opaque, byte* buf, int bufSize)
  {
    int read = this.source.Read(new Span<byte>(buf, bufSize));
    return read == 0
      ? AvErrorEof
      : read;
  }

  private unsafe long SeekCallback(void* opaque, long offset, int whence)
  {
    if (whence == ffmpeg.AVSEEK_SIZE)
    {
      return this.source.CanSeek
        ? this.source.Length
        : -1;
    }

    if (!this.source.CanSeek)
    {
      return -1;
    }

    SeekOrigin origin = whence switch
    {
      0 => SeekOrigin.Begin,
      1 => SeekOrigin.Current,
      2 => SeekOrigin.End,
      _ => (SeekOrigin)(-1),
    };

    if ((int)origin == -1)
    {
      return -1;
    }

    return this.source.Seek(offset, origin);
  }


  // ═══════════════════════════════════════
  //  Disposal (placeholder — Task 7)
  // ═══════════════════════════════════════

  protected override void Dispose(bool disposing)
  {
    base.Dispose(disposing);
  }


  // ═══════════════════════════════════════
  //  Error helpers
  // ═══════════════════════════════════════

  [DoesNotReturn]
  private static void ThrowFFmpeg([ConstantExpected] string methodName)
  {
    throw new InvalidOperationException($"FFmpeg error in <{methodName}>");
  }

  private static unsafe void ThrowOnError(int errorCode, [ConstantExpected] string methodName)
  {
    if (errorCode < 0)
    {
      Span<byte> msg = stackalloc byte[1024];
      fixed (byte* p = msg)
      {
        ffmpeg.av_strerror(errorCode, p, 1024);
      }
      throw new InvalidOperationException
      (
        $"FFmpeg error in <{methodName}>: " +
        $"{System.Text.Encoding.UTF8.GetString(msg).TrimEnd('\0')} ({errorCode})"
      );
    }
  }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build src/UnivKit.Assets.Common/UnivKit.Assets.Common.csproj
```

Expected: success.

- [ ] **Step 3: Commit**

```bash
git add src/UnivKit.Assets.Common/AudioStream.cs
git commit -m "refactor: AudioStream scaffolding with correct namespace and Stream contract"
```

---

### Task 3: Constructor — FFmpeg initialization with cleanup on failure

**Files:**
- Modify: `src/UnivKit.Assets.Common/AudioStream.cs` — replace the constructor placeholder and add `CleanupFFmpeg`, `MapToAudioFormat`, `ToInterleaved`

- [ ] **Step 1: Add the format mapping helpers**

Add above the constructor:

```csharp
private static (AudioSampleFormat format, AudioSampleLayout layout) MapToAudioFormat
(
  AVSampleFormat avFmt
)
{
  return avFmt switch
  {
    AVSampleFormat.AV_SAMPLE_FMT_U8   => (AudioSampleFormat.Byte,   AudioSampleLayout.Interleaved),
    AVSampleFormat.AV_SAMPLE_FMT_U8P  => (AudioSampleFormat.Byte,   AudioSampleLayout.Planar),
    AVSampleFormat.AV_SAMPLE_FMT_S16  => (AudioSampleFormat.Int16,  AudioSampleLayout.Interleaved),
    AVSampleFormat.AV_SAMPLE_FMT_S16P => (AudioSampleFormat.Int16,  AudioSampleLayout.Planar),
    AVSampleFormat.AV_SAMPLE_FMT_S32  => (AudioSampleFormat.Int32,  AudioSampleLayout.Interleaved),
    AVSampleFormat.AV_SAMPLE_FMT_S32P => (AudioSampleFormat.Int32,  AudioSampleLayout.Planar),
    AVSampleFormat.AV_SAMPLE_FMT_S64  => (AudioSampleFormat.Int64,  AudioSampleLayout.Interleaved),
    AVSampleFormat.AV_SAMPLE_FMT_S64P => (AudioSampleFormat.Int64,  AudioSampleLayout.Planar),
    AVSampleFormat.AV_SAMPLE_FMT_FLT  => (AudioSampleFormat.Single, AudioSampleLayout.Interleaved),
    AVSampleFormat.AV_SAMPLE_FMT_FLTP => (AudioSampleFormat.Single, AudioSampleLayout.Planar),
    AVSampleFormat.AV_SAMPLE_FMT_DBL  => (AudioSampleFormat.Double, AudioSampleLayout.Interleaved),
    AVSampleFormat.AV_SAMPLE_FMT_DBLP => (AudioSampleFormat.Double, AudioSampleLayout.Planar),
    _ => throw new NotSupportedException($"Unsupported AVSampleFormat: {avFmt}")
  };
}

private static AVSampleFormat ToInterleaved(AudioSampleFormat fmt)
{
  return fmt switch
  {
    AudioSampleFormat.Byte   => AVSampleFormat.AV_SAMPLE_FMT_U8,
    AudioSampleFormat.Int16  => AVSampleFormat.AV_SAMPLE_FMT_S16,
    AudioSampleFormat.Int32  => AVSampleFormat.AV_SAMPLE_FMT_S32,
    AudioSampleFormat.Int64  => AVSampleFormat.AV_SAMPLE_FMT_S64,
    AudioSampleFormat.Single => AVSampleFormat.AV_SAMPLE_FMT_FLT,
    AudioSampleFormat.Double => AVSampleFormat.AV_SAMPLE_FMT_DBL,
    _ => throw new NotSupportedException($"Cannot map {fmt} to interleaved AVSampleFormat")
  };
}
```

- [ ] **Step 2: Add CleanupFFmpeg**

Add this private method (used by both constructor failure path and Dispose):

```csharp
private unsafe void CleanupFFmpeg()
{
  if (this.swrCtx != null)
  {
    var p = this.swrCtx;
    ffmpeg.swr_free(&p);
    this.swrCtx = null;
  }

  if (this.convertedBuffer != null)
  {
    ffmpeg.av_free(this.convertedBuffer);
    this.convertedBuffer = null;
  }

  if (this.packet != null)
  {
    ffmpeg.av_packet_unref(this.packet);
    var p = this.packet;
    ffmpeg.av_packet_free(&p);
    this.packet = null;
  }

  if (this.frame != null)
  {
    var p = this.frame;
    ffmpeg.av_frame_free(&p);
    this.frame = null;
  }

  if (this.codecCtx != null)
  {
    var p = this.codecCtx;
    ffmpeg.avcodec_free_context(&p);
    this.codecCtx = null;
  }

  if (this.formatCtx != null)
  {
    var p = this.formatCtx;
    ffmpeg.avformat_close_input(&p);
    this.formatCtx = null;
  }

  // AVIO context and its buffer are NOT freed by avformat_close_input.
  // FFmpeg may have reallocated the buffer, so use ioCtx->buffer.
  if (this.ioCtx != null)
  {
    ffmpeg.av_free(this.ioCtx->buffer);
    this.ioCtx->buffer = null;
    ffmpeg.av_free(this.ioCtx);
    this.ioCtx = null;
    this.ioBuffer = null;
  }
  else if (this.ioBuffer != null)
  {
    ffmpeg.av_free(this.ioBuffer);
    this.ioBuffer = null;
  }
}
```

- [ ] **Step 3: Implement the constructor**

Replace the constructor placeholder:

```csharp
public unsafe AudioStream(Stream source, AudioFormat? format = null)
{
  this.source = source;
  this.readCallback = this.ReadCallback;
  this.seekCallback = this.SeekCallback;

  bool success = false;
  try
  {
    // 1. AVIO buffer + context
    this.ioBuffer = (byte*)ffmpeg.av_malloc(IoBufferSize);
    if (this.ioBuffer == null)
    {
      ThrowFFmpeg(nameof(ffmpeg.av_malloc));
    }

    this.ioCtx = ffmpeg.avio_alloc_context
    (
      buffer: this.ioBuffer,
      buffer_size: IoBufferSize,
      write_flag: 0,
      opaque: null,
      read_packet: this.readCallback,
      write_packet: null,
      seek: this.seekCallback
    );
    if (this.ioCtx == null)
    {
      ThrowFFmpeg(nameof(ffmpeg.avio_alloc_context));
    }

    // 2. Format context + open input
    this.formatCtx = ffmpeg.avformat_alloc_context();
    if (this.formatCtx == null)
    {
      ThrowFFmpeg(nameof(ffmpeg.avformat_alloc_context));
    }
    this.formatCtx->pb = this.ioCtx;

    var fmtCtxCopy = this.formatCtx;
    ThrowOnError
    (
      ffmpeg.avformat_open_input(&fmtCtxCopy, url: null, fmt: null, options: null),
      nameof(ffmpeg.avformat_open_input)
    );
    this.formatCtx = fmtCtxCopy;

    ThrowOnError
    (
      ffmpeg.avformat_find_stream_info(this.formatCtx, options: null),
      nameof(ffmpeg.avformat_find_stream_info)
    );

    // 3. Find best audio stream + codec
    AVCodec* codec = null;
    this.audioStreamIndex = ffmpeg.av_find_best_stream
    (
      ic: this.formatCtx,
      type: AVMediaType.AVMEDIA_TYPE_AUDIO,
      wanted_stream_nb: -1,
      related_stream: -1,
      decoder_ret: &codec,
      flags: 0
    );
    if (this.audioStreamIndex < 0)
    {
      ThrowFFmpeg(nameof(ffmpeg.av_find_best_stream));
    }

    // 4. Codec context
    this.codecCtx = ffmpeg.avcodec_alloc_context3(codec);
    if (this.codecCtx == null)
    {
      ThrowFFmpeg(nameof(ffmpeg.avcodec_alloc_context3));
    }

    AVStream* audioStream = this.formatCtx->streams[this.audioStreamIndex];
    ThrowOnError
    (
      ffmpeg.avcodec_parameters_to_context(this.codecCtx, audioStream->codecpar),
      nameof(ffmpeg.avcodec_parameters_to_context)
    );
    ThrowOnError
    (
      ffmpeg.avcodec_open2(this.codecCtx, codec, options: null),
      nameof(ffmpeg.avcodec_open2)
    );

    // 5. Detect native format
    var (sampleFmt, sampleLayout) = MapToAudioFormat(this.codecCtx->sample_fmt);
    this.nativeFormat = new AudioFormat
    {
      SampleRate = (uint)this.codecCtx->sample_rate,
      Channels = (uint)this.codecCtx->channels,
      SampleFormat = sampleFmt,
      SampleLayout = sampleLayout,
    };

    // 6. Determine output format (always interleaved)
    this.Format = format is not null
      ? format with { SampleLayout = AudioSampleLayout.Interleaved }
      : this.nativeFormat with { SampleLayout = AudioSampleLayout.Interleaved };

    // 7. SwrContext setup
    this.SetupSwrContext();

    // 8. Packet + frame
    this.packet = ffmpeg.av_packet_alloc();
    this.frame = ffmpeg.av_frame_alloc();

    // 9. Duration
    if (audioStream->duration > 0 && audioStream->time_base.den > 0)
    {
      double seconds = audioStream->duration * ffmpeg.av_q2d(audioStream->time_base);
      this.Duration = TimeSpan.FromSeconds(seconds);
    }
    else if (this.formatCtx->duration > 0)
    {
      this.Duration = TimeSpan.FromSeconds
      (
        (double)this.formatCtx->duration / ffmpeg.AV_TIME_BASE
      );
    }
    else
    {
      this.Duration = TimeSpan.Zero;
    }

    success = true;
  }
  finally
  {
    if (!success)
    {
      this.CleanupFFmpeg();
    }
  }
}
```

- [ ] **Step 4: Add SetupSwrContext placeholder**

Add this private method (filled in by Task 4):

```csharp
private unsafe void SetupSwrContext()
{
  bool needsSwr = false
    || this.nativeFormat.SampleLayout == AudioSampleLayout.Planar
    || this.Format.SampleRate != this.nativeFormat.SampleRate
    || this.Format.Channels != this.nativeFormat.Channels
    || this.Format.SampleFormat != this.nativeFormat.SampleFormat;

  if (!needsSwr)
  {
    return;
  }

  // TODO: Task 4
  throw new NotImplementedException("Task 4");
}
```

- [ ] **Step 5: Build**

```bash
dotnet build src/UnivKit.Assets.Common/UnivKit.Assets.Common.csproj
```

Expected: success.

- [ ] **Step 6: Commit**

```bash
git add src/UnivKit.Assets.Common/AudioStream.cs
git commit -m "feat: AudioStream constructor with FFmpeg init and cleanup on failure"
```

---

### Task 4: SwrContext setup for format conversion

**Files:**
- Modify: `src/UnivKit.Assets.Common/AudioStream.cs` — replace the `SetupSwrContext` placeholder, add `EnsureConvertedBufferCapacity`

- [ ] **Step 1: Implement SetupSwrContext**

Replace the `SetupSwrContext` method:

```csharp
private unsafe void SetupSwrContext()
{
  bool needsSwr = false
    || this.nativeFormat.SampleLayout == AudioSampleLayout.Planar
    || this.Format.SampleRate != this.nativeFormat.SampleRate
    || this.Format.Channels != this.nativeFormat.Channels
    || this.Format.SampleFormat != this.nativeFormat.SampleFormat;

  if (!needsSwr)
  {
    return;
  }

  long inLayout = ffmpeg.av_get_default_channel_layout((int)this.nativeFormat.Channels);
  long outLayout = ffmpeg.av_get_default_channel_layout((int)this.Format.Channels);
  AVSampleFormat outFmt = ToInterleaved(this.Format.SampleFormat);

  this.swrCtx = ffmpeg.swr_alloc_set_opts
  (
    null,
    outLayout, outFmt, (int)this.Format.SampleRate,
    inLayout, this.codecCtx->sample_fmt, this.codecCtx->sample_rate,
    0, null
  );
  if (this.swrCtx == null)
  {
    ThrowFFmpeg(nameof(ffmpeg.swr_alloc_set_opts));
  }

  ThrowOnError
  (
    ffmpeg.swr_init(this.swrCtx),
    nameof(ffmpeg.swr_init)
  );

  // Allocate conversion buffer (initial capacity, grows if needed)
  this.convertedBufferCapacity = 8192;
  int bufferBytes = this.convertedBufferCapacity * (int)this.Format.BytesPerSample;
  this.convertedBuffer = (byte*)ffmpeg.av_malloc((ulong)bufferBytes);
  if (this.convertedBuffer == null)
  {
    ThrowFFmpeg(nameof(ffmpeg.av_malloc));
  }
}
```

- [ ] **Step 2: Add EnsureConvertedBufferCapacity**

```csharp
private unsafe void EnsureConvertedBufferCapacity(int requiredSamples)
{
  if (requiredSamples <= this.convertedBufferCapacity)
  {
    return;
  }

  if (this.convertedBuffer != null)
  {
    ffmpeg.av_free(this.convertedBuffer);
  }

  this.convertedBufferCapacity = requiredSamples;
  int bufferBytes = this.convertedBufferCapacity * (int)this.Format.BytesPerSample;
  this.convertedBuffer = (byte*)ffmpeg.av_malloc((ulong)bufferBytes);
  if (this.convertedBuffer == null)
  {
    ThrowFFmpeg(nameof(ffmpeg.av_malloc));
  }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build src/UnivKit.Assets.Common/UnivKit.Assets.Common.csproj
```

Expected: success.

- [ ] **Step 4: Commit**

```bash
git add src/UnivKit.Assets.Common/AudioStream.cs
git commit -m "feat: SwrContext setup for audio format conversion"
```

---

### Task 5: Read loop — decode, convert, copy with partial frame tracking

This is the core of the class. Three private helpers + the `Read` override.

**Files:**
- Modify: `src/UnivKit.Assets.Common/AudioStream.cs` — replace `Read` placeholder, add `TryReadAndSendPacket`, `TryReceiveFrame`, `CopyFromCurrentFrame`

- [ ] **Step 1: Add TryReadAndSendPacket**

Reads packets from the container in a loop (not recursive), skipping non-audio packets, and sends the first audio packet to the decoder:

```csharp
private unsafe bool TryReadAndSendPacket()
{
  while (true)
  {
    ffmpeg.av_packet_unref(this.packet);

    int ret = ffmpeg.av_read_frame(this.formatCtx, this.packet);
    if (ret < 0)
    {
      return false;
    }

    if (this.packet->stream_index != this.audioStreamIndex)
    {
      continue;
    }

    ret = ffmpeg.avcodec_send_packet(this.codecCtx, this.packet);
    return ret >= 0;
  }
}
```

- [ ] **Step 2: Add TryReceiveFrame**

Receives a decoded frame. If SwrContext is active, converts and stores in `convertedBuffer`. Sets `frameSampleCount` and `frameSampleOffset`:

```csharp
private unsafe bool TryReceiveFrame()
{
  int ret = ffmpeg.avcodec_receive_frame(this.codecCtx, this.frame);
  if (ret < 0)
  {
    return false;
  }

  if (this.swrCtx != null)
  {
    // Estimate output samples (may differ from input due to resampling)
    int outSamples = (int)ffmpeg.av_rescale_rnd
    (
      this.frame->nb_samples,
      (long)this.Format.SampleRate,
      this.codecCtx->sample_rate,
      AVRounding.AV_ROUND_UP
    ) + 256;

    this.EnsureConvertedBufferCapacity(outSamples);

    byte* outBuf = this.convertedBuffer;
    int converted = ffmpeg.swr_convert
    (
      this.swrCtx,
      &outBuf, this.convertedBufferCapacity,
      this.frame->extended_data, this.frame->nb_samples
    );

    if (converted <= 0)
    {
      return false;
    }

    this.frameSampleCount = converted;
  }
  else
  {
    this.frameSampleCount = this.frame->nb_samples;
  }

  this.frameSampleOffset = 0;
  this.hasFrame = this.frameSampleCount > 0;
  return this.hasFrame;
}
```

- [ ] **Step 3: Add CopyFromCurrentFrame**

Copies interleaved PCM bytes from either `convertedBuffer` (SwrContext active) or `frame->data[0]` (native interleaved). Returns bytes written, always a multiple of `BytesPerSample`:

```csharp
private unsafe int CopyFromCurrentFrame(Span<byte> buffer)
{
  int blockSize = (int)this.Format.BytesPerSample;
  int samplesAvailable = this.frameSampleCount - this.frameSampleOffset;
  int samplesRequested = buffer.Length / blockSize;
  int samplesToCopy = Math.Min(samplesAvailable, samplesRequested);

  if (samplesToCopy <= 0)
  {
    return 0;
  }

  int bytesToCopy = samplesToCopy * blockSize;
  int srcByteOffset = this.frameSampleOffset * blockSize;

  byte* src = this.swrCtx != null
    ? this.convertedBuffer + srcByteOffset
    : this.frame->data[0] + srcByteOffset;

  new ReadOnlySpan<byte>(src, bytesToCopy).CopyTo(buffer);

  this.frameSampleOffset += samplesToCopy;
  if (this.frameSampleOffset >= this.frameSampleCount)
  {
    this.hasFrame = false;
  }

  return bytesToCopy;
}
```

- [ ] **Step 4: Implement Read**

Replace the `Read(Span<byte>)` placeholder:

```csharp
public override int Read(Span<byte> buffer)
{
  ObjectDisposedException.ThrowIf(this.disposed, this);

  int blockSize = (int)this.Format.BytesPerSample;
  int usableLength = (buffer.Length / blockSize) * blockSize;
  if (usableLength == 0)
  {
    return 0;
  }

  int totalBytesWritten = 0;

  while (totalBytesWritten < usableLength)
  {
    // 1. Copy remaining data from the current frame
    if (this.hasFrame)
    {
      int written = this.CopyFromCurrentFrame
      (
        buffer.Slice(totalBytesWritten, usableLength - totalBytesWritten)
      );
      totalBytesWritten += written;
      continue;
    }

    if (this.reachedEof)
    {
      break;
    }

    // 2. Try to receive another frame (current packet may yield multiple)
    if (this.TryReceiveFrame())
    {
      continue;
    }

    // 3. If we already sent the flush packet and receive failed, we're done
    if (this.sentFlushPacket)
    {
      this.reachedEof = true;
      break;
    }

    // 4. Read and send next packet to decoder
    if (this.TryReadAndSendPacket())
    {
      continue;
    }

    // 5. No more packets — send null to drain remaining frames from decoder
    ffmpeg.avcodec_send_packet(this.codecCtx, null);
    this.sentFlushPacket = true;
  }

  this.position += totalBytesWritten;
  return totalBytesWritten;
}
```

- [ ] **Step 5: Build**

```bash
dotnet build src/UnivKit.Assets.Common/UnivKit.Assets.Common.csproj
```

Expected: success.

- [ ] **Step 6: Commit**

```bash
git add src/UnivKit.Assets.Common/AudioStream.cs
git commit -m "feat: Read loop with partial frame tracking and multi-frame draining"
```

---

### Task 6: Seeking

**Files:**
- Modify: `src/UnivKit.Assets.Common/AudioStream.cs` — replace both `Seek` placeholders

- [ ] **Step 1: Implement Seek(long, SeekOrigin)**

Replace the placeholder:

```csharp
public override long Seek(long offset, SeekOrigin origin)
{
  ObjectDisposedException.ThrowIf(this.disposed, this);

  if (!this.CanSeek)
  {
    throw new NotSupportedException("Source stream does not support seeking");
  }

  int blockSize = (int)this.Format.BytesPerSample;

  long targetByte = origin switch
  {
    SeekOrigin.Begin   => offset,
    SeekOrigin.Current => this.position + offset,
    SeekOrigin.End     => this.Length + offset,
    _ => throw new ArgumentOutOfRangeException(nameof(origin))
  };

  // Snap down to block boundary
  targetByte = Math.Max(0, targetByte);
  targetByte = (targetByte / blockSize) * blockSize;

  // Convert byte position to sample index, then to stream timestamp
  long sampleIndex = targetByte / blockSize;

  unsafe
  {
    AVStream* audioStream = this.formatCtx->streams[this.audioStreamIndex];

    long timestamp = ffmpeg.av_rescale_q
    (
      sampleIndex,
      new AVRational { num = 1, den = (int)this.Format.SampleRate },
      audioStream->time_base
    );

    int ret = ffmpeg.av_seek_frame
    (
      this.formatCtx,
      this.audioStreamIndex,
      timestamp,
      ffmpeg.AVSEEK_FLAG_BACKWARD
    );
    ThrowOnError(ret, nameof(ffmpeg.av_seek_frame));

    // Reset decoder state
    ffmpeg.avcodec_flush_buffers(this.codecCtx);
  }

  this.hasFrame = false;
  this.frameSampleOffset = 0;
  this.frameSampleCount = 0;
  this.reachedEof = false;
  this.sentFlushPacket = false;
  this.position = targetByte;

  return this.position;
}
```

- [ ] **Step 2: Implement Seek(TimeSpan)**

Replace the placeholder:

```csharp
public long Seek(TimeSpan timestamp)
{
  long sampleIndex = (long)(timestamp.TotalSeconds * this.Format.SampleRate);
  long byteOffset = sampleIndex * (long)this.Format.BytesPerSample;
  return this.Seek(byteOffset, SeekOrigin.Begin);
}
```

- [ ] **Step 3: Build**

```bash
dotnet build src/UnivKit.Assets.Common/UnivKit.Assets.Common.csproj
```

Expected: success.

- [ ] **Step 4: Commit**

```bash
git add src/UnivKit.Assets.Common/AudioStream.cs
git commit -m "feat: block-aligned seeking with av_seek_frame and TimeSpan overload"
```

---

### Task 7: Disposal and finalizer

**Files:**
- Modify: `src/UnivKit.Assets.Common/AudioStream.cs` — replace `Dispose` placeholder, add finalizer

- [ ] **Step 1: Implement Dispose and finalizer**

Replace the `Dispose(bool)` placeholder:

```csharp
~AudioStream()
{
  this.Dispose(false);
}

protected override void Dispose(bool disposing)
{
  if (this.disposed)
  {
    return;
  }
  this.disposed = true;

  this.CleanupFFmpeg();

  if (disposing)
  {
    this.source.Dispose();
    GC.SuppressFinalize(this);
  }

  base.Dispose(disposing);
}
```

- [ ] **Step 2: Build the project**

```bash
dotnet build src/UnivKit.Assets.Common/UnivKit.Assets.Common.csproj
```

Expected: success.

- [ ] **Step 3: Build the full solution to check for downstream breakage**

```bash
dotnet build UnivKit.slnx
```

Expected: success (or known unrelated errors — check output).

- [ ] **Step 4: Commit**

```bash
git add src/UnivKit.Assets.Common/AudioStream.cs
git commit -m "feat: proper disposal with finalizer safety net"
```

---

## Notes

- **AVERROR constants:** The hardcoded values (`-1094995501` for EOF, `-11` for EAGAIN) were carried over from the original code. Verify they match your installed FFmpeg version. If `ffmpeg.AVERROR_EOF` exists in the binding, prefer that.
- **`swr_convert` output pointer:** `swr_convert` takes `byte**` for both input and output. For interleaved output, we pass `&outBuf` where `outBuf` is a single `byte*`. FFmpeg writes to `outBuf[0..n]` and does not modify the pointer.
- **`av_get_default_channel_layout`:** This maps a channel count to a standard layout bitmask (e.g., 2 → stereo). It's the FFmpeg 3.x/4.x API. If the binding uses the newer `AVChannelLayout` struct API, the SwrContext setup in Task 4 needs adjustment.
- **Seek accuracy:** The current implementation seeks to the nearest keyframe before the target (`AVSEEK_FLAG_BACKWARD`). For compressed formats this means the actual decode position may be slightly before the requested position. Accurate seeking (decode-and-discard until exact target) can be added later if needed.
- **Int24:** `AudioSampleFormat.Int24` has no FFmpeg equivalent. `ToInterleaved()` will throw if someone requests Int24 output. This is intentional — Int24 isn't supported as a decode target.
