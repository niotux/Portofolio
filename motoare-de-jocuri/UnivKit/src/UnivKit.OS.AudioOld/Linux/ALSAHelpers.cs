using System;
using System.Runtime.Versioning;
using QuantiX.Engine.Platform.Linux;

namespace QuantiX.Engine.Audio.Linux;

[SupportedOSPlatform("linux")]
public static class ALSAHelpers
{
  public static int ToAlsaFormat(AudioSampleFormat format) => format switch
  {
    AudioSampleFormat.Int8 => ALSAApi.SND_PCM_FORMAT_S8,
    AudioSampleFormat.UInt8 => ALSAApi.SND_PCM_FORMAT_U8,

    AudioSampleFormat.Int16_LE => ALSAApi.SND_PCM_FORMAT_S16_LE,
    AudioSampleFormat.UInt16_LE => ALSAApi.SND_PCM_FORMAT_U16_LE,
    AudioSampleFormat.Int16_BE => ALSAApi.SND_PCM_FORMAT_S16_BE,
    AudioSampleFormat.UInt16_BE => ALSAApi.SND_PCM_FORMAT_U16_BE,

    AudioSampleFormat.Int24_LE => ALSAApi.SND_PCM_FORMAT_S24_LE,
    AudioSampleFormat.UInt24_LE => ALSAApi.SND_PCM_FORMAT_U24_LE,
    AudioSampleFormat.Int24_BE => ALSAApi.SND_PCM_FORMAT_S24_BE,
    AudioSampleFormat.UInt24_BE => ALSAApi.SND_PCM_FORMAT_U24_BE,

    AudioSampleFormat.Int32_LE => ALSAApi.SND_PCM_FORMAT_S32_LE,
    AudioSampleFormat.UInt32_LE => ALSAApi.SND_PCM_FORMAT_U32_LE,
    AudioSampleFormat.Int32_BE => ALSAApi.SND_PCM_FORMAT_S32_BE,
    AudioSampleFormat.UInt32_BE => ALSAApi.SND_PCM_FORMAT_U32_BE,

    AudioSampleFormat.Single_LE => ALSAApi.SND_PCM_FORMAT_FLOAT_LE,
    AudioSampleFormat.Single_BE => ALSAApi.SND_PCM_FORMAT_FLOAT_BE,
    AudioSampleFormat.Double_LE => ALSAApi.SND_PCM_FORMAT_FLOAT64_LE,
    AudioSampleFormat.Double_BE => ALSAApi.SND_PCM_FORMAT_FLOAT64_BE,

    _ => throw new ArgumentOutOfRangeException(nameof(format))
  };
}
