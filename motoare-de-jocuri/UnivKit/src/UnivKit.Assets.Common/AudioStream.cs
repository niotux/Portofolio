using System.Diagnostics.CodeAnalysis;
using FFmpeg.AutoGen;
using UnivKit.Utility.Async;
using UnivKit.Audio.FFmpeg;
using UnivKit.Audio;

namespace UnivKit.Assets.Common;

public sealed class AudioStream : Stream, IAudioSource, IAssetLoading<AudioStream, AudioFormat>
{
  public static AudioStream LoadAsset(Stream stream)
    => new AudioStream(stream);

  public static AudioStream LoadAsset(Stream stream, AudioFormat targetFormat)
    => new AudioStream(stream, targetFormat);


  private const int IoBufferSize = 65536;
  // private const int AvErrorEof = -1094995501;
  // private const int AvErrorEagain = -11;



  private readonly Stream source;

  private unsafe AVFormatContext* formatCtx;
  private unsafe AVIOContext* ioCtx;
  private unsafe AVCodecContext* codecCtx;
  private unsafe AVPacket* packet;
  private unsafe AVFrame* frame;
  private unsafe SwrContext* swrCtx;
  private unsafe byte* ioBuffer;

  private readonly avio_alloc_context_read_packet readCallback;
  private readonly avio_alloc_context_seek seekCallback;

  private int audioStreamIndex = -1;
  private readonly AudioFormat nativeFormat;
  private bool disposed;



  static AudioStream()
  {
    FFmpegLoader.EnsureInitialized();
  }

  public unsafe AudioStream(Stream source, AudioFormat? format = null)
  {
    if (format is not null && format.Value.SampleLayout != AudioSampleLayout.Interleaved)
    {
      throw new ArgumentException("AudioStream can only produce interleaved sample layouts. Plannar will fail.");
    }
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
      this.nativeFormat = new AudioFormat()
      {
        SampleRate = (uint)this.codecCtx->sample_rate,
        Channels = (uint)this.codecCtx->ch_layout.nb_channels,
        SampleFormat = sampleFmt,
        SampleLayout = sampleLayout,
      };

      // 6. Determine output format (always interleaved)
      this.Format = format is not null
        ? format.Value with { SampleLayout = AudioSampleLayout.Interleaved }
        : this.nativeFormat with { SampleLayout = AudioSampleLayout.Interleaved };

      // 7. SwrContext setup
      this.SetupSwrContext();

      // 8. Packet + frame
      this.packet = ffmpeg.av_packet_alloc();
      this.frame = ffmpeg.av_frame_alloc();

      // 9. Duration
      if (audioStream->duration > 0 && audioStream->time_base.den > 0)
      {
        double seconds = audioStream->duration
          * ((double)audioStream->time_base.num / audioStream->time_base.den);
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



  private unsafe int ReadCallback(void* opaque, byte* buf, int bufSize)
  {
    int read = this.source.Read(new Span<byte>(buf, bufSize));
    return read == 0
      ? ffmpeg.AVERROR_EOF
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



  private static (AudioSampleFormat Format, AudioSampleLayout Layout) MapToAudioFormat(AVSampleFormat avFmt)
  {
    return avFmt switch
    {
      AVSampleFormat.AV_SAMPLE_FMT_U8 => (AudioSampleFormat.Byte, AudioSampleLayout.Interleaved),
      AVSampleFormat.AV_SAMPLE_FMT_U8P => (AudioSampleFormat.Byte, AudioSampleLayout.Planar),
      AVSampleFormat.AV_SAMPLE_FMT_S16 => (AudioSampleFormat.Int16, AudioSampleLayout.Interleaved),
      AVSampleFormat.AV_SAMPLE_FMT_S16P => (AudioSampleFormat.Int16, AudioSampleLayout.Planar),
      AVSampleFormat.AV_SAMPLE_FMT_S32 => (AudioSampleFormat.Int32, AudioSampleLayout.Interleaved),
      AVSampleFormat.AV_SAMPLE_FMT_S32P => (AudioSampleFormat.Int32, AudioSampleLayout.Planar),
      AVSampleFormat.AV_SAMPLE_FMT_S64 => (AudioSampleFormat.Int64, AudioSampleLayout.Interleaved),
      AVSampleFormat.AV_SAMPLE_FMT_S64P => (AudioSampleFormat.Int64, AudioSampleLayout.Planar),
      AVSampleFormat.AV_SAMPLE_FMT_FLT => (AudioSampleFormat.Single, AudioSampleLayout.Interleaved),
      AVSampleFormat.AV_SAMPLE_FMT_FLTP => (AudioSampleFormat.Single, AudioSampleLayout.Planar),
      AVSampleFormat.AV_SAMPLE_FMT_DBL => (AudioSampleFormat.Double, AudioSampleLayout.Interleaved),
      AVSampleFormat.AV_SAMPLE_FMT_DBLP => (AudioSampleFormat.Double, AudioSampleLayout.Planar),
      _ => throw new NotSupportedException($"Unsupported AVSampleFormat: {avFmt}")
    };
  }

  private static AVSampleFormat ToInterleaved(AudioSampleFormat fmt)
  {
    return fmt switch
    {
      AudioSampleFormat.Byte => AVSampleFormat.AV_SAMPLE_FMT_U8,
      AudioSampleFormat.Int16 => AVSampleFormat.AV_SAMPLE_FMT_S16,
      AudioSampleFormat.Int32 => AVSampleFormat.AV_SAMPLE_FMT_S32,
      AudioSampleFormat.Int64 => AVSampleFormat.AV_SAMPLE_FMT_S64,
      AudioSampleFormat.Single => AVSampleFormat.AV_SAMPLE_FMT_FLT,
      AudioSampleFormat.Double => AVSampleFormat.AV_SAMPLE_FMT_DBL,
      _ => throw new NotSupportedException($"Cannot map {fmt} to interleaved AVSampleFormat")
    };
  }



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

    AVChannelLayout inLayout;
    AVChannelLayout outLayout;
    ffmpeg.av_channel_layout_default(&inLayout, (int)this.nativeFormat.Channels);
    ffmpeg.av_channel_layout_default(&outLayout, (int)this.Format.Channels);
    AVSampleFormat outFmt = ToInterleaved(this.Format.SampleFormat);

    SwrContext* swr = null;
    ThrowOnError
    (
      ffmpeg.swr_alloc_set_opts2
      (
        &swr,
        &outLayout, outFmt, (int)this.Format.SampleRate,
        &inLayout, this.codecCtx->sample_fmt, this.codecCtx->sample_rate,
        0, null
      ),
      nameof(ffmpeg.swr_alloc_set_opts2)
    );
    this.swrCtx = swr;
    if (this.swrCtx == null)
    {
      ThrowFFmpeg(nameof(ffmpeg.swr_alloc_set_opts2));
    }

    ThrowOnError
    (
      ffmpeg.swr_init(this.swrCtx),
      nameof(ffmpeg.swr_init)
    );

    // Initial capacity; grows on demand in EnsureConvertedBufferCapacity.
    this.convertedBufferCapacity = 8192;
    int bufferBytes = this.convertedBufferCapacity * (int)this.Format.BytesPerSample;
    this.convertedBuffer = (byte*)ffmpeg.av_malloc((ulong)bufferBytes);
    if (this.convertedBuffer == null)
    {
      ThrowFFmpeg(nameof(ffmpeg.av_malloc));
    }
  }

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



  public AudioFormat Format { get; }
  public TimeSpan Duration { get; }
  public TimeSpan CurrentTimestamp => TimeSpan.FromSeconds
  (
    (double)this.Position / (this.Format.SampleRate * this.Format.BytesPerSample)
  );

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
      return (long)(this.Duration.TotalSeconds * this.Format.SampleRate) * (long)this.Format.BytesPerSample;
    }
  }

  public override long Position
  {
    get => this.position;
    set => this.Seek(value, SeekOrigin.Begin);
  }

  private AudioState state = AudioState.Paused;
  public AudioState State => this.state;

  private readonly AsyncGate finishGate = new();
  public Task FinishPlaying => finishGate.WaitToOpenAsync().AsTask();

  private double volume;
  public double Volume
  {
    get => this.volume;
    set
    {
      Interlocked.Exchange(ref this.volume, double.Clamp(value, 0.0, 1.0));
    }
  }

  public long SampleCount => throw new NotImplementedException();

  public long CurrentSample { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
  TimeSpan IAudioSource.CurrentTimestamp { get => CurrentTimestamp; set => throw new NotImplementedException(); }

  private int frameSampleOffset;
  private int frameSampleCount;
  private bool hasFrame;

  private unsafe byte* convertedBuffer;
  private int convertedBufferCapacity;

  private long position;
  private bool reachedEof;
  private bool sentFlushPacket;

  public override int Read(byte[] buffer, int offset, int count)
  {
    return this.Read(buffer.AsSpan(offset, count));
  }

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
      unsafe
      {
        ffmpeg.avcodec_send_packet(this.codecCtx, null);
      }
      this.sentFlushPacket = true;
    }

    this.position += totalBytesWritten;
    return totalBytesWritten;
  }

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

  private unsafe bool TryReceiveFrame()
  {
    int ret = ffmpeg.avcodec_receive_frame(this.codecCtx, this.frame);
    if (ret < 0)
    {
      return false;
    }

    if (this.swrCtx != null)
    {
      // Estimate output samples (may differ from input due to resampling).
      int outSamples = (int)ffmpeg.av_rescale_rnd
      (
        this.frame->nb_samples,
        (long)this.Format.SampleRate,
        this.codecCtx->sample_rate,
        AVRounding.AV_ROUND_UP
      );
      outSamples += 256;

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

    // SwrContext path reads from the converted buffer; native path reads directly from the frame.
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



  public override long Seek(long offset, SeekOrigin origin = SeekOrigin.Begin)
  {
    ObjectDisposedException.ThrowIf(this.disposed, this);

    if (!this.CanSeek)
    {
      throw new NotSupportedException("Source stream does not support seeking");
    }

    int blockSize = (int)this.Format.BytesPerSample;

    long targetByte = origin switch
    {
      SeekOrigin.Begin => offset,
      SeekOrigin.Current => this.position + offset,
      SeekOrigin.End => this.Length + offset,
      _ => throw new ArgumentOutOfRangeException(nameof(origin))
    };

    // Snap down to block boundary.
    targetByte = Math.Max(0, targetByte);
    targetByte = (targetByte / blockSize) * blockSize;

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

  public long Seek(TimeSpan timestamp, SeekOrigin origin = SeekOrigin.Begin)
  {
    long sampleIndex = origin == SeekOrigin.Begin
      ? (long)(timestamp.TotalSeconds * this.Format.SampleRate)
      : (long)((this.Duration - timestamp).TotalSeconds * this.Format.SampleRate);
    long byteOffset = sampleIndex * (long)this.Format.BytesPerSample;
    return this.Seek(byteOffset, SeekOrigin.Begin);
  }



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
        $"FFmpeg error in <{methodName}>: {System.Text.Encoding.UTF8.GetString(msg).TrimEnd('\0')} ({errorCode})"
      );
    }
  }

  public void Play()
  {
    throw new NotImplementedException();
  }

  public void Pause()
  {
    throw new NotImplementedException();
  }

  public void Reset()
  {
    throw new NotImplementedException();
  }
}
