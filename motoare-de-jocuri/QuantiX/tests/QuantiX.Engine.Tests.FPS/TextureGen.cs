namespace QuantiX.Engine.Tests.FPS;

/// <summary>
/// Procedural texture atlas generator. 256x256 RGBA8.
/// Layout (each tile 64x64):
///   Row 0: Concrete, Brick, Metal, Wood
///   Row 1: Crate, Barrel, Grass, Sand
///   Row 2: EnemyRed, EnemyBlue, EnemyGreen, Dark
///   Row 3: Floor, RampMetal, SkyPlaceholder, White
/// </summary>
public static class TextureGen
{
  public const int AtlasSize = 256;
  public const int TileSize = 64;
  public const int TilesPerRow = 4;

  // UV coordinates for each texture tile (normalized 0-1)
  public static readonly (float uMin, float vMin, float uMax, float vMax) Concrete = TileUV(0, 0);
  public static readonly (float uMin, float vMin, float uMax, float vMax) Brick = TileUV(1, 0);
  public static readonly (float uMin, float vMin, float uMax, float vMax) Metal = TileUV(2, 0);
  public static readonly (float uMin, float vMin, float uMax, float vMax) Wood = TileUV(3, 0);
  public static readonly (float uMin, float vMin, float uMax, float vMax) Crate = TileUV(0, 1);
  public static readonly (float uMin, float vMin, float uMax, float vMax) Barrel = TileUV(1, 1);
  public static readonly (float uMin, float vMin, float uMax, float vMax) GrassTex = TileUV(2, 1);
  public static readonly (float uMin, float vMin, float uMax, float vMax) Sand = TileUV(3, 1);
  public static readonly (float uMin, float vMin, float uMax, float vMax) EnemyRed = TileUV(0, 2);
  public static readonly (float uMin, float vMin, float uMax, float vMax) EnemyBlue = TileUV(1, 2);
  public static readonly (float uMin, float vMin, float uMax, float vMax) EnemyGreen = TileUV(2, 2);
  public static readonly (float uMin, float vMin, float uMax, float vMax) Dark = TileUV(3, 2);
  public static readonly (float uMin, float vMin, float uMax, float vMax) Floor = TileUV(0, 3);
  public static readonly (float uMin, float vMin, float uMax, float vMax) RampMetal = TileUV(1, 3);
  public static readonly (float uMin, float vMin, float uMax, float vMax) White = TileUV(3, 3);

  private static (float, float, float, float) TileUV(int col, int row)
  {
    float s = 1f / TilesPerRow;
    return (col * s, row * s, (col + 1) * s, (row + 1) * s);
  }

  public static byte[] Generate()
  {
    byte[] data = new byte[AtlasSize * AtlasSize * 4];

    FillTile(data, 0, 0, GenConcrete);
    FillTile(data, 1, 0, GenBrick);
    FillTile(data, 2, 0, GenMetal);
    FillTile(data, 3, 0, GenWood);
    FillTile(data, 0, 1, GenCrate);
    FillTile(data, 1, 1, GenBarrel);
    FillTile(data, 2, 1, GenGrass);
    FillTile(data, 3, 1, GenSand);
    FillTile(data, 0, 2, GenEnemyRed);
    FillTile(data, 1, 2, GenEnemyBlue);
    FillTile(data, 2, 2, GenEnemyGreen);
    FillTile(data, 3, 2, GenDark);
    FillTile(data, 0, 3, GenFloor);
    FillTile(data, 1, 3, GenRampMetal);
    FillTile(data, 2, 3, GenDark); // placeholder
    FillTile(data, 3, 3, GenWhite);

    return data;
  }

  private static void FillTile(byte[] data, int col, int row, Action<byte[], int, int, int, int> gen)
  {
    int x0 = col * TileSize, y0 = row * TileSize;
    gen(data, x0, y0, TileSize, AtlasSize);
  }

  private static void SetPixel(byte[] data, int x, int y, int stride, byte r, byte g, byte b, byte a = 255)
  {
    int idx = (y * stride + x) * 4;
    data[idx] = r; data[idx + 1] = g; data[idx + 2] = b; data[idx + 3] = a;
  }

  private static int SimpleHash(int x, int y)
  {
    int h = x * 374761393 + y * 668265263;
    h = (h ^ (h >> 13)) * 1274126177;
    return h;
  }

  private static void GenConcrete(byte[] data, int x0, int y0, int size, int stride)
  {
    for (int y = 0; y < size; y++)
      for (int x = 0; x < size; x++)
      {
        int noise = (SimpleHash(x + x0, y + y0) & 31) - 16;
        byte v = (byte)Math.Clamp(160 + noise, 0, 255);
        SetPixel(data, x0 + x, y0 + y, stride, v, v, (byte)(v - 5));
      }
  }

  private static void GenBrick(byte[] data, int x0, int y0, int size, int stride)
  {
    for (int y = 0; y < size; y++)
      for (int x = 0; x < size; x++)
      {
        int brickRow = y / 8;
        int offset = (brickRow & 1) * 8;
        int bx = (x + offset) % 16;
        bool mortar = (y % 8 == 0) || (bx == 0);
        int noise = (SimpleHash(x + x0, y + y0) & 15) - 8;

        if (mortar)
          SetPixel(data, x0 + x, y0 + y, stride, (byte)(180 + noise), (byte)(175 + noise), (byte)(165 + noise));
        else
          SetPixel(data, x0 + x, y0 + y, stride, (byte)(140 + noise), (byte)(60 + noise), (byte)(45 + noise));
      }
  }

  private static void GenMetal(byte[] data, int x0, int y0, int size, int stride)
  {
    for (int y = 0; y < size; y++)
      for (int x = 0; x < size; x++)
      {
        int noise = (SimpleHash(x + x0, y + y0) & 15) - 8;
        bool scratch = ((x + y * 3) % 37) == 0;
        byte v = (byte)Math.Clamp(scratch ? 200 : 130 + noise, 0, 255);
        SetPixel(data, x0 + x, y0 + y, stride, (byte)(v - 10), (byte)(v - 5), v);
      }
  }

  private static void GenWood(byte[] data, int x0, int y0, int size, int stride)
  {
    for (int y = 0; y < size; y++)
      for (int x = 0; x < size; x++)
      {
        int ring = (int)(MathF.Sin(x * 0.3f + y * 0.05f) * 10 + 10);
        int noise = (SimpleHash(x + x0, y + y0) & 15) - 8;
        byte r = (byte)Math.Clamp(140 + ring + noise, 0, 255);
        byte g = (byte)Math.Clamp(90 + ring / 2 + noise, 0, 255);
        byte b = (byte)Math.Clamp(50 + noise, 0, 255);
        SetPixel(data, x0 + x, y0 + y, stride, r, g, b);
      }
  }

  private static void GenCrate(byte[] data, int x0, int y0, int size, int stride)
  {
    for (int y = 0; y < size; y++)
      for (int x = 0; x < size; x++)
      {
        int noise = (SimpleHash(x + x0, y + y0) & 15) - 8;
        bool border = x < 3 || x >= size - 3 || y < 3 || y >= size - 3;
        bool cross = MathF.Abs(x - size / 2) + MathF.Abs(y - size / 2) < 8;
        byte r, g, b;
        if (border || cross)
        {
          r = (byte)Math.Clamp(100 + noise, 0, 255);
          g = (byte)Math.Clamp(65 + noise, 0, 255);
          b = (byte)Math.Clamp(30 + noise, 0, 255);
        }
        else
        {
          r = (byte)Math.Clamp(160 + noise, 0, 255);
          g = (byte)Math.Clamp(120 + noise, 0, 255);
          b = (byte)Math.Clamp(60 + noise, 0, 255);
        }
        SetPixel(data, x0 + x, y0 + y, stride, r, g, b);
      }
  }

  private static void GenBarrel(byte[] data, int x0, int y0, int size, int stride)
  {
    for (int y = 0; y < size; y++)
      for (int x = 0; x < size; x++)
      {
        int noise = (SimpleHash(x + x0, y + y0) & 15) - 8;
        bool band = (y % 16) < 3;
        byte r, g, b;
        if (band)
        {
          r = (byte)Math.Clamp(80 + noise, 0, 255);
          g = (byte)Math.Clamp(80 + noise, 0, 255);
          b = (byte)Math.Clamp(90 + noise, 0, 255);
        }
        else
        {
          r = (byte)Math.Clamp(50 + noise, 0, 255);
          g = (byte)Math.Clamp(80 + noise, 0, 255);
          b = (byte)Math.Clamp(40 + noise, 0, 255);
        }
        SetPixel(data, x0 + x, y0 + y, stride, r, g, b);
      }
  }

  private static void GenGrass(byte[] data, int x0, int y0, int size, int stride)
  {
    for (int y = 0; y < size; y++)
      for (int x = 0; x < size; x++)
      {
        int noise = (SimpleHash(x + x0, y + y0) & 31) - 16;
        byte r = (byte)Math.Clamp(60 + noise, 0, 255);
        byte g = (byte)Math.Clamp(130 + noise, 0, 255);
        byte b = (byte)Math.Clamp(40 + noise, 0, 255);
        SetPixel(data, x0 + x, y0 + y, stride, r, g, b);
      }
  }

  private static void GenSand(byte[] data, int x0, int y0, int size, int stride)
  {
    for (int y = 0; y < size; y++)
      for (int x = 0; x < size; x++)
      {
        int noise = (SimpleHash(x + x0, y + y0) & 15) - 8;
        byte r = (byte)Math.Clamp(210 + noise, 0, 255);
        byte g = (byte)Math.Clamp(190 + noise, 0, 255);
        byte b = (byte)Math.Clamp(140 + noise, 0, 255);
        SetPixel(data, x0 + x, y0 + y, stride, r, g, b);
      }
  }

  private static void GenEnemyRed(byte[] data, int x0, int y0, int size, int stride)
  {
    for (int y = 0; y < size; y++)
      for (int x = 0; x < size; x++)
      {
        int noise = (SimpleHash(x + x0, y + y0) & 15) - 8;
        SetPixel(data, x0 + x, y0 + y, stride,
          (byte)Math.Clamp(200 + noise, 0, 255),
          (byte)Math.Clamp(50 + noise, 0, 255),
          (byte)Math.Clamp(50 + noise, 0, 255));
      }
  }

  private static void GenEnemyBlue(byte[] data, int x0, int y0, int size, int stride)
  {
    for (int y = 0; y < size; y++)
      for (int x = 0; x < size; x++)
      {
        int noise = (SimpleHash(x + x0, y + y0) & 15) - 8;
        SetPixel(data, x0 + x, y0 + y, stride,
          (byte)Math.Clamp(50 + noise, 0, 255),
          (byte)Math.Clamp(80 + noise, 0, 255),
          (byte)Math.Clamp(200 + noise, 0, 255));
      }
  }

  private static void GenEnemyGreen(byte[] data, int x0, int y0, int size, int stride)
  {
    for (int y = 0; y < size; y++)
      for (int x = 0; x < size; x++)
      {
        int noise = (SimpleHash(x + x0, y + y0) & 15) - 8;
        SetPixel(data, x0 + x, y0 + y, stride,
          (byte)Math.Clamp(50 + noise, 0, 255),
          (byte)Math.Clamp(180 + noise, 0, 255),
          (byte)Math.Clamp(50 + noise, 0, 255));
      }
  }

  private static void GenDark(byte[] data, int x0, int y0, int size, int stride)
  {
    for (int y = 0; y < size; y++)
      for (int x = 0; x < size; x++)
        SetPixel(data, x0 + x, y0 + y, stride, 20, 20, 25);
  }

  private static void GenFloor(byte[] data, int x0, int y0, int size, int stride)
  {
    for (int y = 0; y < size; y++)
      for (int x = 0; x < size; x++)
      {
        int noise = (SimpleHash(x + x0, y + y0) & 15) - 8;
        bool tile = ((x / 16) + (y / 16)) % 2 == 0;
        byte v = (byte)Math.Clamp((tile ? 140 : 120) + noise, 0, 255);
        SetPixel(data, x0 + x, y0 + y, stride, v, (byte)(v - 3), (byte)(v - 8));
      }
  }

  private static void GenRampMetal(byte[] data, int x0, int y0, int size, int stride)
  {
    for (int y = 0; y < size; y++)
      for (int x = 0; x < size; x++)
      {
        int noise = (SimpleHash(x + x0, y + y0) & 15) - 8;
        bool stripe = (x + y) % 8 < 4;
        byte r = (byte)Math.Clamp((stripe ? 180 : 60) + noise, 0, 255);
        byte g = (byte)Math.Clamp((stripe ? 160 : 60) + noise, 0, 255);
        byte b = (byte)Math.Clamp(60 + noise, 0, 255);
        SetPixel(data, x0 + x, y0 + y, stride, r, g, b);
      }
  }

  private static void GenWhite(byte[] data, int x0, int y0, int size, int stride)
  {
    for (int y = 0; y < size; y++)
      for (int x = 0; x < size; x++)
        SetPixel(data, x0 + x, y0 + y, stride, 255, 255, 255);
  }
}
