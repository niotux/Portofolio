namespace QuantiX.Engine.Tests.Rendering;

public static class TextureAtlas
{
  // ── Existing entries ──────────────────────────────────────────────
  public const int GrassTop = 0;
  public const int GrassSide = 1;
  public const int Dirt = 2;
  public const int Stone = 3;
  public const int WoodSide = 4;
  public const int WoodTop = 5;
  public const int Leaves = 6;

  // ── New entries ───────────────────────────────────────────────────
  public const int Sand = 7;
  public const int Sandstone = 8;
  public const int SandstoneTop = 9;
  public const int Gravel = 10;
  public const int Water = 11;
  public const int Snow = 12;
  public const int Ice = 13;
  public const int Cactus = 14;
  public const int Clay = 15;
  public const int PineLogSide = 16;
  public const int PineLogTop = 17;
  public const int PineLeaves = 18;
  public const int SavannaGrassTop = 19;
  public const int SavannaGrassSide = 20;
  public const int Bedrock = 21;

  public const int EntryCount = 22;
  public const uint PixelsPerEntry = 4;
  public const uint Width = 4;
  public const uint Height = PixelsPerEntry * EntryCount;

  public static float StepV => 1f / EntryCount;

  public static int GetEntry(BlockType block, FaceDirection face) => block switch
  {
    BlockType.Grass => face switch { FaceDirection.Top => GrassTop, FaceDirection.Bottom => Dirt, _ => GrassSide },
    BlockType.Dirt => Dirt,
    BlockType.Stone => Stone,
    BlockType.Wood => face is FaceDirection.Top or FaceDirection.Bottom ? WoodTop : WoodSide,
    BlockType.Leaves => Leaves,
    BlockType.Sand => Sand,
    BlockType.Sandstone => face is FaceDirection.Top or FaceDirection.Bottom ? SandstoneTop : Sandstone,
    BlockType.Gravel => Gravel,
    BlockType.Water => Water,
    BlockType.Snow => Snow,
    BlockType.Ice => Ice,
    BlockType.Cactus => Cactus,
    BlockType.Clay => Clay,
    BlockType.PineLog => face is FaceDirection.Top or FaceDirection.Bottom ? PineLogTop : PineLogSide,
    BlockType.PineLeaves => PineLeaves,
    BlockType.SavannaGrass => face switch { FaceDirection.Top => SavannaGrassTop, FaceDirection.Bottom => Dirt, _ => SavannaGrassSide },
    BlockType.Bedrock => Bedrock,
    _ => Stone,
  };

  public static byte[] Generate()
  {
    byte[] data = new byte[Width * Height * 4];

    void Set(int entry, int x, int y, byte r, byte g, byte b)
    {
      int idx = (int)(((entry * PixelsPerEntry + y) * Width + x) * 4);
      data[idx] = r; data[idx + 1] = g; data[idx + 2] = b; data[idx + 3] = 255;
    }

    void Fill(int entry, byte r, byte g, byte b)
    {
      for (int y = 0; y < PixelsPerEntry; y++)
      {
        for (int x = 0; x < PixelsPerEntry; x++)
        {
          Set(entry, x, y, r, g, b);
        }
      }
    }

    void FillRow(int entry, int row, byte r, byte g, byte b)
    {
      for (int x = 0; x < PixelsPerEntry; x++)
      {
        Set(entry, x, row, r, g, b);
      }
    }

    // ── Existing ──────────────────────────────────────────────────

    Fill(GrassTop, 76, 153, 0);
    Set(GrassTop, 1, 1, 60, 130, 0);
    Set(GrassTop, 3, 2, 90, 170, 10);
    Set(GrassTop, 0, 3, 65, 140, 5);

    Fill(GrassSide, 134, 96, 67);
    FillRow(GrassSide, 3, 76, 153, 0);
    Set(GrassSide, 2, 3, 60, 130, 0);

    Fill(Dirt, 134, 96, 67);
    Set(Dirt, 0, 0, 120, 85, 58);
    Set(Dirt, 2, 1, 145, 105, 75);
    Set(Dirt, 1, 3, 115, 80, 55);
    Set(Dirt, 3, 2, 140, 100, 70);

    Fill(Stone, 128, 128, 128);
    Set(Stone, 1, 0, 100, 100, 100);
    Set(Stone, 3, 1, 105, 105, 105);
    Set(Stone, 0, 2, 95, 95, 95);
    Set(Stone, 2, 3, 110, 110, 110);

    Fill(WoodSide, 101, 67, 33);

    for (int y = 0; y < 4; y++)
    {
      Set(WoodSide, 1, y, 85, 55, 25); Set(WoodSide, 3, y, 90, 58, 28);
    }

    Fill(WoodTop, 168, 135, 80);
    Set(WoodTop, 1, 1, 140, 110, 60);
    Set(WoodTop, 2, 1, 140, 110, 60);
    Set(WoodTop, 1, 2, 140, 110, 60);
    Set(WoodTop, 2, 2, 140, 110, 60);

    Fill(Leaves, 50, 120, 30);
    Set(Leaves, 0, 1, 35, 90, 20);
    Set(Leaves, 2, 0, 40, 100, 25);
    Set(Leaves, 3, 3, 30, 85, 15);
    Set(Leaves, 1, 2, 60, 135, 40);

    // ── New ───────────────────────────────────────────────────────

    // Sand — warm beige with slight variation
    Fill(Sand, 210, 190, 140);
    Set(Sand, 0, 0, 220, 200, 150);
    Set(Sand, 2, 1, 200, 182, 130);
    Set(Sand, 1, 3, 215, 196, 145);
    Set(Sand, 3, 2, 205, 188, 136);

    // Sandstone side — horizontal banding
    Fill(Sandstone, 186, 162, 100);
    FillRow(Sandstone, 1, 170, 148, 88);
    FillRow(Sandstone, 3, 170, 148, 88);
    Set(Sandstone, 0, 0, 194, 170, 108);
    Set(Sandstone, 3, 2, 178, 155, 94);

    // Sandstone top — lighter
    Fill(SandstoneTop, 200, 178, 112);
    Set(SandstoneTop, 1, 1, 188, 165, 100);
    Set(SandstoneTop, 3, 0, 210, 188, 120);
    Set(SandstoneTop, 0, 3, 195, 172, 106);

    // Gravel — mixed gray-brown
    Fill(Gravel, 138, 132, 126);
    Set(Gravel, 0, 0, 120, 115, 110);
    Set(Gravel, 2, 0, 150, 144, 138);
    Set(Gravel, 1, 2, 125, 120, 114);
    Set(Gravel, 3, 1, 145, 140, 133);
    Set(Gravel, 0, 3, 130, 126, 120);
    Set(Gravel, 2, 3, 112, 108, 102);

    // Water — deep blue
    Fill(Water, 38, 88, 200);
    Set(Water, 1, 0, 45, 95, 210);
    Set(Water, 3, 1, 32, 80, 192);
    Set(Water, 0, 2, 42, 90, 205);
    Set(Water, 2, 3, 50, 98, 215);

    // Snow — near-white with faint blue tint
    Fill(Snow, 232, 238, 245);
    Set(Snow, 1, 1, 220, 228, 238);
    Set(Snow, 3, 0, 240, 245, 250);
    Set(Snow, 0, 3, 228, 235, 244);

    // Ice — pale translucent blue
    Fill(Ice, 158, 204, 232);
    Set(Ice, 0, 0, 140, 192, 224);
    Set(Ice, 2, 1, 170, 212, 238);
    Set(Ice, 1, 3, 148, 198, 228);
    Set(Ice, 3, 2, 165, 208, 235);

    // Cactus — dark green with lighter stripe
    Fill(Cactus, 50, 135, 42);
    for (int y = 0; y < 4; y++)
    {
      Set(Cactus, 2, y, 70, 155, 55);
    }
    Set(Cactus, 0, 1, 40, 120, 35);
    Set(Cactus, 3, 3, 38, 118, 32);

    // Clay — pinkish gray
    Fill(Clay, 155, 142, 152);
    Set(Clay, 0, 0, 145, 132, 142);
    Set(Clay, 2, 1, 162, 148, 158);
    Set(Clay, 1, 3, 148, 136, 146);
    Set(Clay, 3, 2, 158, 145, 155);

    // Pine log side — dark bark with streaks
    Fill(PineLogSide, 72, 48, 26);
    for (int y = 0; y < 4; y++)
    {
      Set(PineLogSide, 2, y, 88, 62, 36);
    }
    Set(PineLogSide, 0, 1, 62, 40, 20);
    Set(PineLogSide, 3, 3, 66, 44, 22);

    // Pine log top — dark concentric rings
    Fill(PineLogTop, 98, 70, 40);
    Set(PineLogTop, 1, 1, 72, 50, 26);
    Set(PineLogTop, 2, 1, 72, 50, 26);
    Set(PineLogTop, 1, 2, 72, 50, 26);
    Set(PineLogTop, 2, 2, 72, 50, 26);

    // Pine leaves — very dark forest green
    Fill(PineLeaves, 20, 85, 18);
    Set(PineLeaves, 0, 0, 15, 72, 14);
    Set(PineLeaves, 2, 1, 28, 95, 24);
    Set(PineLeaves, 3, 3, 14, 68, 12);
    Set(PineLeaves, 1, 2, 32, 100, 28);

    // Savanna grass top — dry yellow-green
    Fill(SavannaGrassTop, 150, 145, 42);
    Set(SavannaGrassTop, 1, 1, 138, 132, 36);
    Set(SavannaGrassTop, 3, 2, 162, 158, 50);
    Set(SavannaGrassTop, 0, 3, 144, 140, 40);

    // Savanna grass side — dirt with dry-grass top row
    Fill(SavannaGrassSide, 134, 96, 67);
    FillRow(SavannaGrassSide, 3, 150, 145, 42);
    Set(SavannaGrassSide, 2, 3, 138, 132, 36);

    // Bedrock — very dark with gray specks
    Fill(Bedrock, 22, 22, 22);
    Set(Bedrock, 1, 0, 38, 38, 38);
    Set(Bedrock, 3, 1, 32, 32, 32);
    Set(Bedrock, 0, 2, 42, 42, 42);
    Set(Bedrock, 2, 3, 28, 28, 28);

    return data;
  }
}

public enum FaceDirection
{
  Top,
  Bottom,
  Side
}
