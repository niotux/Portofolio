namespace OpenUL.Assets.Media;

public interface IImage : IReadOnlyImage
{
  new Memory<byte> Data { get; }
  ReadOnlyMemory<byte> IReadOnlyImage.Data { get => Data; }
}

public interface IImage<TPixel> : IReadOnlyImage<TPixel>, IImage
  where TPixel : IPixel
{
  new ref TPixel this[int x, int y] { get; }
  TPixel IReadOnlyImage<TPixel>.this[int x, int y] { get => this[x, y]; }
}