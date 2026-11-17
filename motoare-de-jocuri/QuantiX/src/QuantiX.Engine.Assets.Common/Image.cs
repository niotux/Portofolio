using System;

namespace QuantiX.Engine.Assets.Common;

public class Image
{
  public Image(byte[] data, uint width, uint height, uint colorChannels)
  {
    this.Width = width;
    this.Height = height;
    this.Data = data;
    this.ColorChannels = colorChannels;
  }

  public byte[] Data { get; }
  public uint Width { get; }
  public uint Height { get; }
  public uint ColorChannels { get; }
}

public enum ImageFileFormat
{
  Png,
  Jpg,
  Jpeg,
  Bmp,
}