using System;
using System.Runtime.Versioning;
using UnivKit.Audio;
using UnivKit.OS.Platform.Linux;

namespace QuantiX.Engine.Audio.Linux;

[SupportedOSPlatform("linux")]
public static class ALSAHelpers
{
  public static LibASound.Format ToAlsaFormat(AudioSampleFormat format) => format switch
  {
    AudioSampleFormat.Byte => LibASound.Format.S8,
    AudioSampleFormat.Int16 => BitConverter.IsLittleEndian
      ? LibASound.Format.S16_LE
      : LibASound.Format.S16_BE,
    AudioSampleFormat.Int24 => BitConverter.IsLittleEndian
      ? LibASound.Format.S24_LE
      : LibASound.Format.S24_BE,
    AudioSampleFormat.Int32 => BitConverter.IsLittleEndian
      ? LibASound.Format.S32_LE
      : LibASound.Format.S32_BE,
    AudioSampleFormat.Single => BitConverter.IsLittleEndian
      ? LibASound.Format.FLOAT_LE
      : LibASound.Format.FLOAT_BE,
    AudioSampleFormat.Double => BitConverter.IsLittleEndian
      ? LibASound.Format.FLOAT64_LE
      : LibASound.Format.FLOAT64_BE,

    _ => throw new ArgumentOutOfRangeException(nameof(format))
  };
}
