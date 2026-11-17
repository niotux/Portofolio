namespace QuantiX.Engine.Tests.Rendering;

internal enum BiomeType { Ocean, Plains, OakForest, Savanna, Desert, PineForest, Swamp, Mountains }

internal static class BiomeSelector
{
  public const int SeaLevel = 20;
  private const int WorldSeed = 12345;

  /// Returns biome, raw elevation [0,1], and river proximity factor (0=center, 1=far).
  public static (BiomeType biome, float elevRaw, float riverFactor) Select(int wx, int wz)
  {
    float elev = Noise.Fbm(wx / 200f, wz / 200f, WorldSeed, octaves: 6);

    if (elev < 0.38f) return (BiomeType.Ocean, elev, 1f);
    if (elev > 0.75f) return (BiomeType.Mountains, elev, 1f);

    float temp = Noise.Fbm(wx / 600f, wz / 600f, WorldSeed + 1_000_000, octaves: 4);
    float moist = Noise.Fbm(wx / 600f, wz / 600f, WorldSeed + 2_000_000, octaves: 4);

    float riverRaw = Noise.Fbm(wx / 120f, wz / 120f, WorldSeed + 3_000_000, octaves: 3);
    float riverFactor = MathF.Abs(riverRaw - 0.5f) * 2f; // 0 = river center, 1 = far away

    BiomeType biome;
    if (temp < 0.40f)
    {
      biome = moist >= 0.65f
        ? BiomeType.Swamp
        : BiomeType.PineForest;
    }
    else if (temp < 0.65f)
    {
      biome = moist < 0.35f
        ? BiomeType.Plains
        : BiomeType.OakForest;
    }
    else
    {
      biome = moist >= 0.35f
        ? BiomeType.Savanna
        : BiomeType.Desert;
    }
    return (biome, elev, riverFactor);
  }

  public static (int height, bool isRiver) GetHeight(int wx, int wz, float elevRaw, float riverFactor)
  {
    // Continuous height curve based on elevation
    float h;
    if (elevRaw < 0.32f)
    {
      // Deep ocean: [4, 16]
      float t = elevRaw / 0.32f;
      h = 4f + t * 12f;
    }
    else if (elevRaw < 0.42f)
    {
      // Coastal transition: [16, 22] with smoothstep
      float t = (elevRaw - 0.32f) / 0.10f;
      t = t * t * (3f - 2f * t); // smoothstep
      h = 16f + t * 6f;
    }
    else if (elevRaw < 0.58f)
    {
      // Flatlands: [22, 28]
      float t = (elevRaw - 0.42f) / 0.16f;
      h = 22f + t * 6f;
    }
    else if (elevRaw < 0.72f)
    {
      // Hills: [28, 36]
      float t = (elevRaw - 0.58f) / 0.14f;
      h = 28f + t * 8f;
    }
    else
    {
      // Mountains: [36, 58] with quadratic for dramatic peaks
      float t = (elevRaw - 0.72f) / 0.28f;
      t = t * t; // quadratic — dramatic peaks
      h = 36f + t * 22f;
    }

    // Micro-detail noise for terrain roughness
    float detail = Noise.Fbm(wx / 40f, wz / 40f, WorldSeed + 4_000_000, octaves: 3);
    float detailScale = elevRaw > 0.55f ? 5f : 2.5f;
    h += (detail - 0.5f) * detailScale;

    // Ridge noise for dramatic mountain terrain
    if (elevRaw > 0.65f)
    {
      float ridgeBlend = (elevRaw - 0.65f) / 0.35f;
      ridgeBlend = MathF.Min(ridgeBlend, 1f);
      float ridge = Noise.Ridge(wx / 80f, wz / 80f, WorldSeed + 5_000_000);
      h += ridge * 10f * ridgeBlend;
    }

    // Smooth river valley carving
    bool isRiver = false;
    if (riverFactor < 0.15f && elevRaw >= 0.38f)
    {
      float riverDepth = 1f - (riverFactor / 0.15f);
      riverDepth = riverDepth * riverDepth; // quadratic for smooth valley walls
      float riverTarget = SeaLevel - 2f;
      h = h * (1f - riverDepth) + riverTarget * riverDepth;
      if (h < SeaLevel) isRiver = true;
    }

    int result = (int)MathF.Round(h);
    return (Math.Clamp(result, 2, Chunk.SizeY - 2), isRiver);
  }
}
