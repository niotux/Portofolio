namespace QuantiX.Engine.Tests.Rendering;

public class Chunk
{
  public const int SizeX = 16;
  public const int SizeY = 64;
  public const int SizeZ = 16;

  public readonly int ChunkX, ChunkZ;
  private readonly BlockType[,,] blocks = new BlockType[SizeX, SizeY, SizeZ];

  public Chunk(int chunkX, int chunkZ)
  {
    ChunkX = chunkX;
    ChunkZ = chunkZ;
  }

  public int WorldOffsetX => ChunkX * SizeX;
  public int WorldOffsetZ => ChunkZ * SizeZ;

  public BlockType GetLocal(int x, int y, int z)
  {
    if (x < 0 || x >= SizeX || y < 0 || y >= SizeY || z < 0 || z >= SizeZ)
      return BlockType.Air;
    return blocks[x, y, z];
  }

  public void SetLocal(int x, int y, int z, BlockType type)
  {
    if (x >= 0 && x < SizeX && y >= 0 && y < SizeY && z >= 0 && z < SizeZ)
      blocks[x, y, z] = type;
  }

  // ── Terrain generation ────────────────────────────────────────────

  public void Generate()
  {
    int ox = WorldOffsetX, oz = WorldOffsetZ;

    for (int x = 0; x < SizeX; x++)
    {
      for (int z = 0; z < SizeZ; z++)
      {
        int wx = ox + x, wz = oz + z;
        var (biome, elevRaw, riverFactor) = BiomeSelector.Select(wx, wz);
        var (height, isRiver) = BiomeSelector.GetHeight(wx, wz, elevRaw, riverFactor);

        // Bedrock floor
        blocks[x, 0, z] = BlockType.Bedrock;

        // Stone fill
        int stoneTop = Math.Max(1, height - 4);
        for (int y = 1; y < stoneTop; y++)
        {
          blocks[x, y, z] = BlockType.Stone;
        }

        // Subsurface (up to 3 blocks)
        BlockType sub = GetSubsurface(biome, isRiver);
        for (int y = stoneTop; y < height - 1; y++)
        {
          blocks[x, y, z] = sub;
        }

        // Surface
        if (height > 1)
        {
          blocks[x, height - 1, z] = GetSurface(biome, height, isRiver);
        }

        // Extra sand dune cap for desert
        if (!isRiver && biome == BiomeType.Desert && height < SizeY - 1)
        {
          blocks[x, height, z] = BlockType.Sand;
        }

        // Water fill
        if (height < BiomeSelector.SeaLevel)
        {
          for (int y = height; y < BiomeSelector.SeaLevel; y++)
          {
            blocks[x, y, z] = BlockType.Water;
          }
        }
      }
    }

    PlaceVegetation();
  }

  private static BlockType GetSubsurface(BiomeType biome, bool isRiver) => (isRiver, biome) switch
  {
    (true, _) => BlockType.Gravel,
    (_, BiomeType.Desert) => BlockType.Sandstone,
    (_, BiomeType.Ocean) => BlockType.Stone,
    (_, BiomeType.Mountains) => BlockType.Stone,
    (_, BiomeType.Swamp) => BlockType.Clay,
    _ => BlockType.Dirt,
  };

  private static BlockType GetSurface(BiomeType biome, int height, bool isRiver)
  {
    if (isRiver) return BlockType.Gravel;
    if (height >= 46) return BlockType.Snow;
    if (height >= 38) return BlockType.Stone;
    return biome switch
    {
      BiomeType.Ocean => height <= 10 ? BlockType.Sand : BlockType.Gravel,
      BiomeType.Plains => BlockType.Grass,
      BiomeType.OakForest => BlockType.Grass,
      BiomeType.Savanna => BlockType.SavannaGrass,
      BiomeType.Desert => BlockType.Sand,
      BiomeType.PineForest => BlockType.Grass,
      BiomeType.Swamp => BlockType.Grass,
      BiomeType.Mountains => BlockType.Stone,
      _ => BlockType.Stone,
    };
  }

  // ── Vegetation ────────────────────────────────────────────────────

  private void PlaceVegetation()
  {
    var (biome, _, _) = BiomeSelector.Select(WorldOffsetX + SizeX / 2, WorldOffsetZ + SizeZ / 2);
    uint seed = (uint)(ChunkX * 374761393 ^ ChunkZ * 668265263);

    switch (biome)
    {
      case BiomeType.OakForest: PlaceMany(seed, (int)(seed % 3) + 3, PlaceOakTree); break;
      case BiomeType.Plains: PlaceMany(seed, (int)(seed % 3), PlaceOakTree); break;
      case BiomeType.Savanna: PlaceMany(seed, (int)(seed % 2), PlaceOakTree); break;
      case BiomeType.Swamp: PlaceMany(seed, (int)(seed % 2) + 1, PlaceOakTree); break;
      case BiomeType.PineForest: PlaceMany(seed * 2u, (int)(seed % 3) + 2, PlacePineTree); break;
      case BiomeType.Desert: PlaceMany(seed * 3u, (int)(seed % 3) + 1, PlaceCactus); break;
    }
  }

  private void PlaceMany(uint seed, int count, Action<int, int> placer)
  {
    for (int i = 0; i < count; i++)
    {
      seed = seed * 2654435761u + 1;
      int tx = (int)(seed % (uint)(SizeX - 4)) + 2;
      seed = seed * 2654435761u + 1;
      int tz = (int)(seed % (uint)(SizeZ - 4)) + 2;
      placer(tx, tz);
    }
  }

  private int FindGround(int lx, int lz)
  {
    for (int y = SizeY - 1; y >= 0; y--)
    {
      BlockType b = blocks[lx, y, lz];
      if (b != BlockType.Air && b != BlockType.Water)
      {
        return y + 1;
      }
    }
    return -1;
  }

  private void PlaceOakTree(int tx, int tz)
  {
    if (tx < 2 || tx >= SizeX - 2 || tz < 2 || tz >= SizeZ - 2) return;
    int ground = FindGround(tx, tz);
    if (ground <= 0 || ground + 7 >= SizeY) return;

    BlockType surface = blocks[tx, ground - 1, tz];
    if (surface != BlockType.Grass && surface != BlockType.SavannaGrass) return;

    for (int i = 0; i < 4; i++)
    {
      blocks[tx, ground + i, tz] = BlockType.Wood;
    }

    int leafBase = ground + 3;
    for (int dy = 0; dy < 3; dy++)
    {
      for (int dx = -1; dx <= 1; dx++)
      {
        for (int dz = -1; dz <= 1; dz++)
        {
          if (dy != 1 && Math.Abs(dx) == 1 && Math.Abs(dz) == 1) continue;
          int lx = tx + dx, ly = leafBase + dy, lz = tz + dz;

          if (lx < 0 || lx >= SizeX || ly >= SizeY || lz < 0 || lz >= SizeZ) continue;

          if (blocks[lx, ly, lz] == BlockType.Air)
          {
            blocks[lx, ly, lz] = BlockType.Leaves;
          }
        }
      }
    }
  }

  private void PlacePineTree(int tx, int tz)
  {
    if (tx < 2 || tx >= SizeX - 2 || tz < 2 || tz >= SizeZ - 2) return;
    int ground = FindGround(tx, tz);
    if (ground <= 0 || ground + 8 >= SizeY) return;

    BlockType surface = blocks[tx, ground - 1, tz];
    if (surface != BlockType.Grass && surface != BlockType.Snow) return;

    for (int i = 0; i < 6; i++)
    {
      blocks[tx, ground + i, tz] = BlockType.PineLog;
    }

    blocks[tx, ground + 7, tz] = BlockType.PineLeaves;
    blocks[tx, ground + 6, tz] = BlockType.PineLeaves;
    PlacePineLeafRing(tx, tz, ground + 5, 1);
    PlacePineLeafRing(tx, tz, ground + 4, 1);
    PlacePineLeafRing(tx, tz, ground + 3, 2);
    PlacePineLeafRing(tx, tz, ground + 2, 2);
  }

  private void PlacePineLeafRing(int cx, int cz, int cy, int radius)
  {
    for (int dx = -radius; dx <= radius; dx++)
    {
      for (int dz = -radius; dz <= radius; dz++)
      {
        if (radius == 2 && Math.Abs(dx) == 2 && Math.Abs(dz) == 2) continue;
        int lx = cx + dx, lz = cz + dz;

        if (lx < 0 || lx >= SizeX || lz < 0 || lz >= SizeZ || cy >= SizeY) continue;

        if (blocks[lx, cy, lz] == BlockType.Air)
        {
          blocks[lx, cy, lz] = BlockType.PineLeaves;
        }
      }
    }
  }

  private void PlaceCactus(int tx, int tz)
  {
    int ground = FindGround(tx, tz);
    if (ground <= 0 || ground + 3 >= SizeY) return;
    if (blocks[tx, ground - 1, tz] != BlockType.Sand) return;

    uint h = (uint)(ChunkX * 1234567 ^ ChunkZ * 7654321 ^ tx * 999 ^ tz * 777);
    int height = (int)(h % 2) + 2;
    for (int i = 0; i < height; i++)
    {
      blocks[tx, ground + i, tz] = BlockType.Cactus;
    }
  }

  // ── Mesh building ─────────────────────────────────────────────────

  public (float[] vertices, uint[] indices) BuildMesh(Func<int, int, int, BlockType> globalGetBlock)
  {
    float atlasStep = TextureAtlas.StepV;
    var verts = new List<float>(8000);
    var inds = new List<uint>(12000);
    uint vertexCount = 0;
    int ox = WorldOffsetX, oz = WorldOffsetZ;

    for (int x = 0; x < SizeX; x++)
    {
      for (int y = 0; y < SizeY; y++)
      {
        for (int z = 0; z < SizeZ; z++)
        {
          BlockType b = blocks[x, y, z];
          if (b == BlockType.Air) continue;

          int wx = ox + x, wz = oz + z;

          if (globalGetBlock(wx, y + 1, wz) == BlockType.Air)
          {
            int tex = TextureAtlas.GetEntry(b, FaceDirection.Top);
            float v0 = tex * atlasStep, v1 = (tex + 1) * atlasStep;
            AddFace
            (
              verts, inds, ref vertexCount,
              wx, y + 1, wz + 1, wx + 1, y + 1, wz + 1,
              wx + 1, y + 1, wz, wx, y + 1, wz, v0, v1, 1.0f,
              0, 1, 0
            );
          }
          if (globalGetBlock(wx, y - 1, wz) == BlockType.Air)
          {
            int tex = TextureAtlas.GetEntry(b, FaceDirection.Bottom);
            float v0 = tex * atlasStep, v1 = (tex + 1) * atlasStep;
            AddFace
            (
              verts, inds, ref vertexCount,
              wx, y, wz, wx + 1, y, wz,
              wx + 1, y, wz + 1, wx, y, wz + 1, v0, v1, 0.5f,
              0, -1, 0
            );
          }
          if (globalGetBlock(wx, y, wz + 1) == BlockType.Air)
          {
            int tex = TextureAtlas.GetEntry(b, FaceDirection.Side);
            float v0 = tex * atlasStep, v1 = (tex + 1) * atlasStep;
            AddFace
            (
              verts, inds, ref vertexCount,
              wx, y, wz + 1, wx + 1, y, wz + 1,
              wx + 1, y + 1, wz + 1, wx, y + 1, wz + 1, v0, v1, 0.8f,
              0, 0, 1
            );
          }
          if (globalGetBlock(wx, y, wz - 1) == BlockType.Air)
          {
            int tex = TextureAtlas.GetEntry(b, FaceDirection.Side);
            float v0 = tex * atlasStep, v1 = (tex + 1) * atlasStep;
            AddFace
            (
              verts, inds, ref vertexCount,
              wx + 1, y, wz, wx, y, wz,
              wx, y + 1, wz, wx + 1, y + 1, wz, v0, v1, 0.8f,
              0, 0, -1
            );
          }
          if (globalGetBlock(wx + 1, y, wz) == BlockType.Air)
          {
            int tex = TextureAtlas.GetEntry(b, FaceDirection.Side);
            float v0 = tex * atlasStep, v1 = (tex + 1) * atlasStep;
            AddFace
            (
              verts, inds, ref vertexCount,
              wx + 1, y, wz + 1, wx + 1, y, wz,
              wx + 1, y + 1, wz, wx + 1, y + 1, wz + 1, v0, v1, 0.7f,
              1, 0, 0
            );
          }
          if (globalGetBlock(wx - 1, y, wz) == BlockType.Air)
          {
            int tex = TextureAtlas.GetEntry(b, FaceDirection.Side);
            float v0 = tex * atlasStep, v1 = (tex + 1) * atlasStep;
            AddFace
            (
              verts, inds, ref vertexCount,
              wx, y, wz, wx, y, wz + 1,
              wx, y + 1, wz + 1, wx, y + 1, wz, v0, v1, 0.6f,
              -1, 0, 0
            );
          }
        }
      }
    }

    return (verts.ToArray(), inds.ToArray());
  }

  private static void AddFace
  (
    List<float> verts, List<uint> inds, ref uint vertexCount,
    float x0, float y0, float z0,
    float x1, float y1, float z1,
    float x2, float y2, float z2,
    float x3, float y3, float z3,
    float v0, float v1, float brightness,
    float nx, float ny, float nz
  )
  {
    uint b = vertexCount;
    verts.Add(x0); verts.Add(y0); verts.Add(z0); verts.Add(0); verts.Add(v0); verts.Add(brightness); verts.Add(nx); verts.Add(ny); verts.Add(nz);
    verts.Add(x1); verts.Add(y1); verts.Add(z1); verts.Add(1); verts.Add(v0); verts.Add(brightness); verts.Add(nx); verts.Add(ny); verts.Add(nz);
    verts.Add(x2); verts.Add(y2); verts.Add(z2); verts.Add(1); verts.Add(v1); verts.Add(brightness); verts.Add(nx); verts.Add(ny); verts.Add(nz);
    verts.Add(x3); verts.Add(y3); verts.Add(z3); verts.Add(0); verts.Add(v1); verts.Add(brightness); verts.Add(nx); verts.Add(ny); verts.Add(nz);
    inds.Add(b); inds.Add(b + 1); inds.Add(b + 2);
    inds.Add(b + 2); inds.Add(b + 3); inds.Add(b);
    vertexCount += 4;
  }
}
