using QuantiX.Engine.Mathematics.Vectors;

namespace QuantiX.Engine.Tests.FPS;

/// <summary>
/// Procedural arena map generator. Source-engine style tactical arena.
/// </summary>
public class Arena
{
  public const float Size = 100f;
  public const float HalfSize = Size / 2f;
  public const float WallHeight = 6f;
  public const float CoverHeight = 1.5f;

  public readonly List<AABB> StaticColliders = new();
  public readonly List<(Vec3<float> pos, float rotY, Vec3<float> scale, int textureId)> PhysicsObjects = new();
  public readonly List<Vec3<float> > SpawnPoints = new();
  public readonly List<Vec3<float>> EnemySpawnPoints = new();

  // Pre-built mesh data
  public float[] Vertices = Array.Empty<float>();
  public uint[] Indices = Array.Empty<uint>();

  // Grass blade positions
  public readonly List<Vec3<float>> GrassPositions = new();

  public void Generate()
  {
    var mb = new MeshBuilder();
    StaticColliders.Clear();
    SpawnPoints.Clear();
    EnemySpawnPoints.Clear();
    GrassPositions.Clear();

    var floor = TextureGen.Floor;
    var concrete = TextureGen.Concrete;
    var brick = TextureGen.Brick;
    var metal = TextureGen.Metal;
    var wood = TextureGen.Wood;
    var crate = TextureGen.Crate;
    var ramp = TextureGen.RampMetal;
    var grass = TextureGen.GrassTex;
    var sand = TextureGen.Sand;

    // ── Main floor ──
    mb.AddFloor(-HalfSize, -HalfSize, HalfSize, HalfSize, 0, floor.uMin, floor.vMin, floor.uMax, floor.vMax, 4f);
    StaticColliders.Add(new AABB(new(-HalfSize, -1, -HalfSize), new(HalfSize, 0, HalfSize)));

    // ── Outer walls ──
    AddWall(mb, new(-HalfSize, 0, -HalfSize), new(HalfSize, WallHeight, -HalfSize + 1), concrete);
    AddWall(mb, new(-HalfSize, 0, HalfSize - 1), new(HalfSize, WallHeight, HalfSize), concrete);
    AddWall(mb, new(-HalfSize, 0, -HalfSize), new(-HalfSize + 1, WallHeight, HalfSize), concrete);
    AddWall(mb, new(HalfSize - 1, 0, -HalfSize), new(HalfSize, WallHeight, HalfSize), concrete);

    // ── Center courtyard structure (raised platform) ──
    float platSize = 8f;
    float platH = 1.5f;
    mb.AddBox(new(-platSize, 0, -platSize), new(platSize, platH, platSize),
      concrete.uMin, concrete.vMin, concrete.uMax, concrete.vMax, 0.9f);
    StaticColliders.Add(new AABB(new(-platSize, 0, -platSize), new(platSize, platH, platSize)));

    // Ramps onto center platform (4 sides)
    float rampLen = 5f;
    mb.AddRamp(new(-3, 0, -platSize - rampLen), new(3, platH, -platSize), ramp.uMin, ramp.vMin, ramp.uMax, ramp.vMax);
    mb.AddRamp(new(-3, 0, platSize), new(3, platH, platSize + rampLen), ramp.uMin, ramp.vMin, ramp.uMax, ramp.vMax);
    // Ramp colliders (approximated as stairs)
    for (int s = 0; s < 4; s++)
    {
      float sy = s * platH / 4f;
      float sz0 = -platSize - rampLen + s * rampLen / 4f;
      float sz1 = sz0 + rampLen / 4f;
      StaticColliders.Add(new AABB(new(-3, sy, sz0), new(3, sy + platH / 4f, sz1)));
    }
    for (int s = 0; s < 4; s++)
    {
      float sy = s * platH / 4f;
      float sz0 = platSize + s * rampLen / 4f;
      float sz1 = sz0 + rampLen / 4f;
      StaticColliders.Add(new AABB(new(-3, sy, sz0), new(3, sy + platH / 4f, sz1)));
    }

    // ── Buildings (4 corners) ──
    float bSize = 10f;
    float bOff = 28f;

    // NW building
    AddBuilding(mb, new(-bOff - bSize, 0, -bOff - bSize), new(-bOff, WallHeight, -bOff), brick, concrete);
    // NE building
    AddBuilding(mb, new(bOff, 0, -bOff - bSize), new(bOff + bSize, WallHeight, -bOff), brick, concrete);
    // SW building
    AddBuilding(mb, new(-bOff - bSize, 0, bOff), new(-bOff, WallHeight, bOff + bSize), brick, concrete);
    // SE building
    AddBuilding(mb, new(bOff, 0, bOff), new(bOff + bSize, WallHeight, bOff + bSize), brick, concrete);

    // ── Cover walls (scattered half-walls) ──
    AddCoverWall(mb, new(-15, 0, -5), new(-13, CoverHeight, 5), concrete);
    AddCoverWall(mb, new(13, 0, -5), new(15, CoverHeight, 5), concrete);
    AddCoverWall(mb, new(-5, 0, -18), new(5, CoverHeight, -16), concrete);
    AddCoverWall(mb, new(-5, 0, 16), new(5, CoverHeight, 18), concrete);

    // ── Long corridor walls (mid sections) ──
    AddWall(mb, new(-20, 0, -22), new(-18, WallHeight, -12), brick);
    AddWall(mb, new(18, 0, 12), new(20, WallHeight, 22), brick);

    // ── Crates (scattered) ──
    Vec3<float>[] cratePositions =
    [
      new(-8, 0, -20), new(-6, 0, -20), new(-8, 1, -20), // stacked
      new(10, 0, 15), new(12, 0, 15),
      new(-25, 0, 25), new(-23, 0, 25), new(-25, 1, 25),
      new(20, 0, -25),
      new(0, platH, 0), // on platform
      new(-2, platH, 2),
      new(30, 0, 0), new(32, 0, 0),
      new(-30, 0, -5),
    ];

    foreach (var pos in cratePositions)
    {
      mb.AddBox(pos, pos + new Vec3<float>(1.5f, 1f, 1.5f), crate.uMin, crate.vMin, crate.uMax, crate.vMax);
      StaticColliders.Add(new AABB(pos, pos + new Vec3<float>(1.5f, 1f, 1.5f)));
    }

    // ── Barrels ──
    Vec3<float>[] barrelPositions =
    [
      new(5, 0.6f, -25), new(7, 0.6f, -25), new(6, 0.6f, -23),
      new(-20, 0.6f, 10), new(-18, 0.6f, 10),
      new(25, 0.6f, -10),
      new(-10, 0.6f, 30),
      new(15, platH + 0.6f, 3),
    ];

    foreach (var pos in barrelPositions)
    {
      mb.AddCylinder(pos, 0.5f, 0.6f, metal.uMin, metal.vMin, metal.uMax, metal.vMax);
      StaticColliders.Add(new AABB(new(pos.X - 0.5f, pos.Y - 0.6f, pos.Z - 0.5f),
                                    new(pos.X + 0.5f, pos.Y + 0.6f, pos.Z + 0.5f)));
    }

    // ── Grass areas (outdoor zones between buildings) ──
    Random rng = new(42);
    for (int i = 0; i < 4000; i++)
    {
      float gx = (float)(rng.NextDouble() * Size - HalfSize);
      float gz = (float)(rng.NextDouble() * Size - HalfSize);

      // Skip if inside building or on platform
      if (MathF.Abs(gx) < platSize + 2 && MathF.Abs(gz) < platSize + 2) continue;
      bool inBuilding = false;
      if ((gx < -bOff + 1 && gx > -bOff - bSize - 1) && (gz < -bOff + 1 && gz > -bOff - bSize - 1)) inBuilding = true;
      if ((gx > bOff - 1 && gx < bOff + bSize + 1) && (gz < -bOff + 1 && gz > -bOff - bSize - 1)) inBuilding = true;
      if ((gx < -bOff + 1 && gx > -bOff - bSize - 1) && (gz > bOff - 1 && gz < bOff + bSize + 1)) inBuilding = true;
      if ((gx > bOff - 1 && gx < bOff + bSize + 1) && (gz > bOff - 1 && gz < bOff + bSize + 1)) inBuilding = true;
      if (inBuilding) continue;

      GrassPositions.Add(new(gx, 0, gz));
    }

    // ── Spawn points ──
    SpawnPoints.Add(new(0, platH + 0.1f, 0)); // center platform
    SpawnPoints.Add(new(-40, 0.1f, -40));
    SpawnPoints.Add(new(40, 0.1f, -40));
    SpawnPoints.Add(new(-40, 0.1f, 40));
    SpawnPoints.Add(new(40, 0.1f, 40));

    // ── Enemy spawn points (edges + buildings) ──
    EnemySpawnPoints.Add(new(-45, 0.1f, 0));
    EnemySpawnPoints.Add(new(45, 0.1f, 0));
    EnemySpawnPoints.Add(new(0, 0.1f, -45));
    EnemySpawnPoints.Add(new(0, 0.1f, 45));
    EnemySpawnPoints.Add(new(-35, 0.1f, -35));
    EnemySpawnPoints.Add(new(35, 0.1f, -35));
    EnemySpawnPoints.Add(new(-35, 0.1f, 35));
    EnemySpawnPoints.Add(new(35, 0.1f, 35));
    EnemySpawnPoints.Add(new(-20, 0.1f, -20));
    EnemySpawnPoints.Add(new(20, 0.1f, 20));
    EnemySpawnPoints.Add(new(-20, 0.1f, 20));
    EnemySpawnPoints.Add(new(20, 0.1f, -20));

    Vertices = mb.GetVertices();
    Indices = mb.GetIndices();
  }

  private void AddWall(MeshBuilder mb, Vec3<float> min, Vec3<float> max,
    (float uMin, float vMin, float uMax, float vMax) tex)
  {
    mb.AddBox(min, max, tex.uMin, tex.vMin, tex.uMax, tex.vMax, 0.85f);
    StaticColliders.Add(new AABB(min, max));
  }

  private void AddCoverWall(MeshBuilder mb, Vec3<float> min, Vec3<float> max,
    (float uMin, float vMin, float uMax, float vMax) tex)
  {
    mb.AddBox(min, max, tex.uMin, tex.vMin, tex.uMax, tex.vMax, 0.7f);
    StaticColliders.Add(new AABB(min, max));
  }

  private void AddBuilding(MeshBuilder mb, Vec3<float> min, Vec3<float> max,
    (float uMin, float vMin, float uMax, float vMax) wallTex,
    (float uMin, float vMin, float uMax, float vMax) floorTex)
  {
    float wallThick = 0.8f;
    float doorWidth = 2.5f;
    float doorHeight = 3f;
    float cx = (min.X + max.X) * 0.5f;
    float cz = (min.Z + max.Z) * 0.5f;

    // Floor inside
    mb.AddFloor(min.X, min.Z, max.X, max.Z, 0.01f, floorTex.uMin, floorTex.vMin, floorTex.uMax, floorTex.vMax, 2f);

    // Back wall (z-) - solid
    AddWall(mb, new(min.X, 0, min.Z), new(max.X, max.Y, min.Z + wallThick), wallTex);

    // Front wall (z+) - door opening in center
    AddWall(mb, new(min.X, 0, max.Z - wallThick), new(cx - doorWidth / 2, max.Y, max.Z), wallTex);
    AddWall(mb, new(cx + doorWidth / 2, 0, max.Z - wallThick), new(max.X, max.Y, max.Z), wallTex);
    AddWall(mb, new(cx - doorWidth / 2, doorHeight, max.Z - wallThick), new(cx + doorWidth / 2, max.Y, max.Z), wallTex);

    // Left wall (x-) - with window
    AddWall(mb, new(min.X, 0, min.Z), new(min.X + wallThick, max.Y, cz - 1.5f), wallTex);
    AddWall(mb, new(min.X, 0, cz + 1.5f), new(min.X + wallThick, max.Y, max.Z), wallTex);
    AddWall(mb, new(min.X, 0, cz - 1.5f), new(min.X + wallThick, 1.5f, cz + 1.5f), wallTex);
    AddWall(mb, new(min.X, 4f, cz - 1.5f), new(min.X + wallThick, max.Y, cz + 1.5f), wallTex);

    // Right wall (x+) - solid
    AddWall(mb, new(max.X - wallThick, 0, min.Z), new(max.X, max.Y, max.Z), wallTex);
  }
}
