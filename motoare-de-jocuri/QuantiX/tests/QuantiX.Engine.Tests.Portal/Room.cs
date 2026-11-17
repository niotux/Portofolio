using QuantiX.Engine.Mathematics.Vectors;

namespace QuantiX.Engine.Tests.Portal;

public enum WallSide { East, West, North, South }

/// <summary>
/// A rectangular opening in a wall where a portal is placed.
/// Position is relative to the wall's bottom-left corner (in the wall's local 2D space).
/// </summary>
public class PortalOpening
{
  public required WallSide Wall;
  public required float OffsetAlongWall; // horizontal offset from wall's left edge
  public required float OffsetY;          // vertical offset from floor
  public required float Width;
  public required float Height;

  // Link to the destination
  public required int DestRoomIndex;
  public required WallSide DestWall;

  // Computed: 3D center position of the opening in room-local space
  public Vec3<float> Center;
  // Computed: yaw delta when passing through this portal
  public float YawDelta;
  // Computed: 4x4 transform from this room's space to dest room's space
  public float[] Transform = Mat4.Identity();
}

public class Room
{
  public required string Name;
  public required float Width;  // X extent: -W/2 to +W/2
  public required float Height; // Y extent: 0 to H
  public required float Depth;  // Z extent: -D/2 to +D/2
  public required Vec3<float> WallColor;
  public required Vec3<float> FloorColor;
  public required Vec3<float> CeilColor;
  public List<PortalOpening> Portals = [];
  // Optional decorative AABBs (pillars, boxes)
  public List<(Vec3<float> Min, Vec3<float> Max, Vec3<float> Color)> Decorations = [];

  /// <summary>
  /// Gets the outward normal for a wall (pointing out of the room).
  /// </summary>
  public static Vec3<float> OutwardNormal(WallSide wall) => wall switch
  {
    WallSide.East => new(1, 0, 0),
    WallSide.West => new(-1, 0, 0),
    WallSide.North => new(0, 0, -1),
    WallSide.South => new(0, 0, 1),
    _ => default
  };

  /// <summary>
  /// Gets the yaw angle of a wall's outward normal.
  /// </summary>
  public static float OutwardYaw(WallSide wall) => wall switch
  {
    WallSide.East => MathF.PI / 2f,
    WallSide.West => -MathF.PI / 2f,
    WallSide.North => 0f,
    WallSide.South => MathF.PI,
    _ => 0f
  };

  /// <summary>
  /// Gets the wall's extent along its surface (width of the wall).
  /// </summary>
  public float WallExtent(WallSide wall) => wall is WallSide.East or WallSide.West ? Depth : Width;

  /// <summary>
  /// Computes the 3D center of a portal opening in room-local space.
  /// </summary>
  public Vec3<float> PortalCenter(PortalOpening p)
  {
    float wallExt = WallExtent(p.Wall);
    float along = -wallExt / 2f + p.OffsetAlongWall + p.Width / 2f;
    float y = p.OffsetY + p.Height / 2f;

    return p.Wall switch
    {
      WallSide.East => new(Width / 2f, y, along),
      WallSide.West => new(-Width / 2f, y, -along),
      WallSide.North => new(along, y, -Depth / 2f),
      WallSide.South => new(-along, y, Depth / 2f),
      _ => default
    };
  }

  /// <summary>
  /// Build collision AABBs for the room (walls, floor, ceiling — excluding portal openings).
  /// Thin slabs for each wall segment.
  /// </summary>
  public AABB[] BuildColliders()
  {
    const float T = 0.1f; // wall thickness
    var colliders = new List<AABB>();

    // Floor
    colliders.Add(new(new(-Width / 2f, -T, -Depth / 2f), new(Width / 2f, 0, Depth / 2f)));
    // Ceiling
    colliders.Add(new(new(-Width / 2f, Height, -Depth / 2f), new(Width / 2f, Height + T, Depth / 2f)));

    // For each wall, build collider segments around portal holes
    BuildWallColliders(colliders, WallSide.East, T);
    BuildWallColliders(colliders, WallSide.West, T);
    BuildWallColliders(colliders, WallSide.North, T);
    BuildWallColliders(colliders, WallSide.South, T);

    // Decorations
    foreach (var (min, max, _) in Decorations)
      colliders.Add(new(min, max));

    return colliders.ToArray();
  }

  private void BuildWallColliders(List<AABB> colliders, WallSide wall, float T)
  {
    var portalsOnWall = Portals.Where(p => p.Wall == wall).ToList();

    if (portalsOnWall.Count == 0)
    {
      // Solid wall
      colliders.Add(WallAABB(wall, T, 0, 0, WallExtent(wall), Height));
      return;
    }

    // For simplicity, build the wall as a solid slab and add a "pass-through" check
    // in the portal teleportation logic instead. The wall still blocks movement
    // except when the player is aligned with a portal opening.
    // Build 4 segments around each portal hole.
    foreach (var p in portalsOnWall)
    {
      float wallExt = WallExtent(wall);
      float left = p.OffsetAlongWall;
      float right = p.OffsetAlongWall + p.Width;
      float bottom = p.OffsetY;
      float top = p.OffsetY + p.Height;

      // Bottom strip
      if (bottom > 0.01f)
        colliders.Add(WallAABB(wall, T, 0, 0, wallExt, bottom));
      // Top strip
      if (top < Height - 0.01f)
        colliders.Add(WallAABB(wall, T, 0, top, wallExt, Height - top));
      // Left strip
      if (left > 0.01f)
        colliders.Add(WallAABB(wall, T, 0, bottom, left, top - bottom));
      // Right strip
      if (right < wallExt - 0.01f)
        colliders.Add(WallAABB(wall, T, right, bottom, wallExt - right, top - bottom));
    }
  }

  /// <summary>
  /// Creates an AABB for a wall segment.
  /// offsetAlong/offsetY are relative to the wall's bottom-left.
  /// extAlong/extY are the size of the segment.
  /// </summary>
  private AABB WallAABB(WallSide wall, float thickness, float offsetAlong, float offsetY, float extAlong, float extY)
  {
    float wallExt = WallExtent(wall);
    float along0 = -wallExt / 2f + offsetAlong;
    float along1 = along0 + extAlong;
    float y0 = offsetY;
    float y1 = offsetY + extY;

    return wall switch
    {
      WallSide.East => new(new(Width / 2f - thickness, y0, along0), new(Width / 2f + thickness, y1, along1)),
      WallSide.West => new(new(-Width / 2f - thickness, y0, -along1), new(-Width / 2f + thickness, y1, -along0)),
      WallSide.North => new(new(along0, y0, -Depth / 2f - thickness), new(along1, y1, -Depth / 2f + thickness)),
      WallSide.South => new(new(-along1, y0, Depth / 2f - thickness), new(-along0, y1, Depth / 2f + thickness)),
      _ => default
    };
  }

  // ── Mesh generation ──

  /// <summary>
  /// Builds vertex (pos3 + normal3 + color3 = 9 floats) and index data for the room.
  /// Portal openings are left as holes in the walls.
  /// </summary>
  public (float[] Verts, uint[] Inds) BuildMesh()
  {
    var verts = new List<float>();
    var inds = new List<uint>();

    // Floor
    AddQuad(verts, inds,
      new(-Width / 2f, 0, -Depth / 2f),
      new(Width / 2f, 0, -Depth / 2f),
      new(Width / 2f, 0, Depth / 2f),
      new(-Width / 2f, 0, Depth / 2f),
      new(0, 1, 0), FloorColor);

    // Ceiling
    AddQuad(verts, inds,
      new(-Width / 2f, Height, Depth / 2f),
      new(Width / 2f, Height, Depth / 2f),
      new(Width / 2f, Height, -Depth / 2f),
      new(-Width / 2f, Height, -Depth / 2f),
      new(0, -1, 0), CeilColor);

    // Walls with portal holes
    BuildWallMesh(verts, inds, WallSide.East);
    BuildWallMesh(verts, inds, WallSide.West);
    BuildWallMesh(verts, inds, WallSide.North);
    BuildWallMesh(verts, inds, WallSide.South);

    // Decorations (boxes)
    foreach (var (min, max, color) in Decorations)
      AddBox(verts, inds, min, max, color);

    return (verts.ToArray(), inds.ToArray());
  }

  private void BuildWallMesh(List<float> verts, List<uint> inds, WallSide wall)
  {
    float wallExt = WallExtent(wall);
    var portalsOnWall = Portals.Where(p => p.Wall == wall).OrderBy(p => p.OffsetAlongWall).ToList();
    var outN = OutwardNormal(wall);
    Vec3<float> normal = new(-outN.X, -outN.Y, -outN.Z); // inward normal for rendering

    if (portalsOnWall.Count == 0)
    {
      AddWallQuad(verts, inds, wall, 0, 0, wallExt, Height, normal, WallColor);
      return;
    }

    // Build wall segments around each portal hole
    foreach (var p in portalsOnWall)
    {
      float left = p.OffsetAlongWall;
      float right = p.OffsetAlongWall + p.Width;
      float bottom = p.OffsetY;
      float top = p.OffsetY + p.Height;

      // Bottom strip (full width)
      if (bottom > 0.01f)
        AddWallQuad(verts, inds, wall, 0, 0, wallExt, bottom, normal, WallColor);
      // Top strip (full width)
      if (top < Height - 0.01f)
        AddWallQuad(verts, inds, wall, 0, top, wallExt, Height - top, normal, WallColor);
      // Left strip (portal height only)
      if (left > 0.01f)
        AddWallQuad(verts, inds, wall, 0, bottom, left, top - bottom, normal, WallColor);
      // Right strip (portal height only)
      if (right < wallExt - 0.01f)
        AddWallQuad(verts, inds, wall, right, bottom, wallExt - right, top - bottom, normal, WallColor);
    }
  }

  /// <summary>
  /// Adds a wall quad segment. offsetAlong/offsetY from wall bottom-left, extAlong/extY size.
  /// </summary>
  private void AddWallQuad(List<float> verts, List<uint> inds,
    WallSide wall, float offsetAlong, float offsetY, float extAlong, float extY,
    Vec3<float> normal, Vec3<float> color)
  {
    float wallExt = WallExtent(wall);
    float a0 = -wallExt / 2f + offsetAlong;
    float a1 = a0 + extAlong;
    float y0 = offsetY;
    float y1 = offsetY + extY;

    Vec3<float> bl, br, tr, tl;
    switch (wall)
    {
      case WallSide.East:
        bl = new(Width / 2f, y0, a0); br = new(Width / 2f, y0, a1);
        tr = new(Width / 2f, y1, a1); tl = new(Width / 2f, y1, a0);
        break;
      case WallSide.West:
        bl = new(-Width / 2f, y0, -a1); br = new(-Width / 2f, y0, -a0);
        tr = new(-Width / 2f, y1, -a0); tl = new(-Width / 2f, y1, -a1);
        break;
      case WallSide.North:
        bl = new(a0, y0, -Depth / 2f); br = new(a1, y0, -Depth / 2f);
        tr = new(a1, y1, -Depth / 2f); tl = new(a0, y1, -Depth / 2f);
        break;
      case WallSide.South:
        bl = new(-a1, y0, Depth / 2f); br = new(-a0, y0, Depth / 2f);
        tr = new(-a0, y1, Depth / 2f); tl = new(-a1, y1, Depth / 2f);
        break;
      default: return;
    }

    AddQuad(verts, inds, bl, br, tr, tl, normal, color);
  }

  private static void AddQuad(List<float> verts, List<uint> inds,
    Vec3<float> a, Vec3<float> b, Vec3<float> c, Vec3<float> d,
    Vec3<float> normal, Vec3<float> color)
  {
    uint baseIdx = (uint)(verts.Count / 9);
    AddVert(verts, a, normal, color);
    AddVert(verts, b, normal, color);
    AddVert(verts, c, normal, color);
    AddVert(verts, d, normal, color);

    // Auto-detect winding: cross(b-a, c-a) should align with desired normal
    float e1x = b.X - a.X, e1y = b.Y - a.Y, e1z = b.Z - a.Z;
    float e2x = c.X - a.X, e2y = c.Y - a.Y, e2z = c.Z - a.Z;
    float cx = e1y * e2z - e1z * e2y;
    float cy = e1z * e2x - e1x * e2z;
    float cz = e1x * e2y - e1y * e2x;
    float dot = cx * normal.X + cy * normal.Y + cz * normal.Z;

    if (dot >= 0)
      inds.AddRange([baseIdx, baseIdx + 1, baseIdx + 2, baseIdx + 2, baseIdx + 3, baseIdx]);
    else
      inds.AddRange([baseIdx, baseIdx + 3, baseIdx + 2, baseIdx + 2, baseIdx + 1, baseIdx]);
  }

  private static void AddVert(List<float> verts, Vec3<float> pos, Vec3<float> normal, Vec3<float> color)
  {
    verts.AddRange([pos.X, pos.Y, pos.Z, normal.X, normal.Y, normal.Z, color.X, color.Y, color.Z]);
  }

  private static void AddBox(List<float> verts, List<uint> inds,
    Vec3<float> min, Vec3<float> max, Vec3<float> color)
  {
    var p000 = min;
    var p100 = new Vec3<float>(max.X, min.Y, min.Z);
    var p110 = new Vec3<float>(max.X, max.Y, min.Z);
    var p010 = new Vec3<float>(min.X, max.Y, min.Z);
    var p001 = new Vec3<float>(min.X, min.Y, max.Z);
    var p101 = new Vec3<float>(max.X, min.Y, max.Z);
    var p111 = max;
    var p011 = new Vec3<float>(min.X, max.Y, max.Z);

    // Front (-Z)
    AddQuad(verts, inds, p000, p100, p110, p010, new(0, 0, -1), color);
    // Back (+Z)
    AddQuad(verts, inds, p101, p001, p011, p111, new(0, 0, 1), color);
    // Right (+X)
    AddQuad(verts, inds, p100, p101, p111, p110, new(1, 0, 0), color);
    // Left (-X)
    AddQuad(verts, inds, p001, p000, p010, p011, new(-1, 0, 0), color);
    // Top (+Y)
    AddQuad(verts, inds, p010, p110, p111, p011, new(0, 1, 0), color);
    // Bottom (-Y)
    AddQuad(verts, inds, p001, p101, p100, p000, new(0, -1, 0), color);
  }

  /// <summary>
  /// Builds the quad geometry for a single portal opening (used to render the portal texture).
  /// Returns 4 vertices: position(3) + texcoord(2) = 5 floats each.
  /// </summary>
  public (float[] Verts, uint[] Inds) BuildPortalQuad(PortalOpening p)
  {
    float wallExt = WallExtent(p.Wall);
    float a0 = -wallExt / 2f + p.OffsetAlongWall;
    float a1 = a0 + p.Width;
    float y0 = p.OffsetY;
    float y1 = p.OffsetY + p.Height;

    // Slight inset so the quad sits just inside the wall
    const float inset = 0.001f;
    Vec3<float> bl, br, tr, tl;
    switch (p.Wall)
    {
      case WallSide.East:
        float ex = Width / 2f - inset;
        bl = new(ex, y0, a0); br = new(ex, y0, a1);
        tr = new(ex, y1, a1); tl = new(ex, y1, a0);
        break;
      case WallSide.West:
        float wx = -Width / 2f + inset;
        bl = new(wx, y0, -a1); br = new(wx, y0, -a0);
        tr = new(wx, y1, -a0); tl = new(wx, y1, -a1);
        break;
      case WallSide.North:
        float nz = -Depth / 2f + inset;
        bl = new(a0, y0, nz); br = new(a1, y0, nz);
        tr = new(a1, y1, nz); tl = new(a0, y1, nz);
        break;
      case WallSide.South:
        float sz = Depth / 2f - inset;
        bl = new(-a1, y0, sz); br = new(-a0, y0, sz);
        tr = new(-a0, y1, sz); tl = new(-a1, y1, sz);
        break;
      default:
        bl = br = tr = tl = default;
        break;
    }

    float[] verts =
    [
      bl.X, bl.Y, bl.Z, 0, 0,
      br.X, br.Y, br.Z, 1, 0,
      tr.X, tr.Y, tr.Z, 1, 1,
      tl.X, tl.Y, tl.Z, 0, 1,
    ];
    uint[] indices = [0, 1, 2, 2, 3, 0];
    return (verts, indices);
  }
}

// ── World builder: creates the non-euclidean room layout ──

public static class WorldBuilder
{
  public const float PortalWidth = 2.0f;
  public const float PortalHeight = 2.5f;

  public static Room[] BuildWorld()
  {
    // Room 0: The Lobby — warm orange
    var lobby = new Room
    {
      Name = "The Lobby",
      Width = 8, Height = 3, Depth = 8,
      WallColor = new(0.85f, 0.55f, 0.25f),
      FloorColor = new(0.35f, 0.25f, 0.15f),
      CeilColor = new(0.6f, 0.45f, 0.3f),
    };
    // Decorative table
    lobby.Decorations.Add((new(-1f, 0, -1f), new(1f, 0.8f, 1f), new(0.5f, 0.3f, 0.15f)));
    // Chairs around table
    lobby.Decorations.Add((new(-1.6f, 0, -0.3f), new(-1.2f, 0.5f, 0.3f), new(0.45f, 0.25f, 0.1f)));
    lobby.Decorations.Add((new(1.2f, 0, -0.3f), new(1.6f, 0.5f, 0.3f), new(0.45f, 0.25f, 0.1f)));
    // Shelving unit against south wall
    lobby.Decorations.Add((new(-3.5f, 0, 3.2f), new(-2.0f, 2.2f, 3.7f), new(0.55f, 0.35f, 0.2f)));
    // Floor rug (thin flat box)
    lobby.Decorations.Add((new(-2f, 0.005f, -2f), new(2f, 0.015f, 2f), new(0.6f, 0.2f, 0.15f)));
    // Wall sconce (small box on wall)
    lobby.Decorations.Add((new(3.85f, 2.0f, -1f), new(3.95f, 2.5f, 1f), new(0.9f, 0.8f, 0.4f)));

    // Room 1: The Gallery — cool blue, tall narrow
    var gallery = new Room
    {
      Name = "The Gallery",
      Width = 6, Height = 4, Depth = 14,
      WallColor = new(0.3f, 0.45f, 0.75f),
      FloorColor = new(0.2f, 0.2f, 0.3f),
      CeilColor = new(0.25f, 0.35f, 0.55f),
    };
    // Pillars along the gallery
    for (float z = -5f; z <= 5f; z += 5f)
    {
      gallery.Decorations.Add((new(-2.5f, 0, z - 0.3f), new(-2.0f, 4, z + 0.3f), new(0.4f, 0.4f, 0.5f)));
      gallery.Decorations.Add((new(2.0f, 0, z - 0.3f), new(2.5f, 4, z + 0.3f), new(0.4f, 0.4f, 0.5f)));
    }
    // Pedestals with "art" between pillars
    gallery.Decorations.Add((new(-1.5f, 0, -4.5f), new(-0.8f, 1.0f, -3.8f), new(0.5f, 0.5f, 0.55f)));
    gallery.Decorations.Add((new(-1.3f, 1.0f, -4.3f), new(-1.0f, 1.8f, -4.0f), new(0.7f, 0.3f, 0.3f)));
    gallery.Decorations.Add((new(0.8f, 0, 0.5f), new(1.5f, 1.0f, 1.2f), new(0.5f, 0.5f, 0.55f)));
    gallery.Decorations.Add((new(1.0f, 1.0f, 0.7f), new(1.3f, 1.6f, 1.0f), new(0.3f, 0.6f, 0.8f)));
    // Bench
    gallery.Decorations.Add((new(-1.2f, 0, 3.5f), new(1.2f, 0.45f, 4.0f), new(0.35f, 0.3f, 0.25f)));
    // Frame on wall (flat)
    gallery.Decorations.Add((new(-2.95f, 1.5f, -1.5f), new(-2.9f, 3.0f, 0.5f), new(0.8f, 0.7f, 0.3f)));

    // Room 2: The Atrium — green, large
    var atrium = new Room
    {
      Name = "The Atrium",
      Width = 12, Height = 5, Depth = 12,
      WallColor = new(0.3f, 0.65f, 0.35f),
      FloorColor = new(0.15f, 0.3f, 0.15f),
      CeilColor = new(0.25f, 0.5f, 0.3f),
    };
    // Central pillar
    atrium.Decorations.Add((new(-0.8f, 0, -0.8f), new(0.8f, 5, 0.8f), new(0.5f, 0.55f, 0.4f)));
    // Corner pillars
    atrium.Decorations.Add((new(-5.5f, 0, -5.5f), new(-4.8f, 5, -4.8f), new(0.4f, 0.5f, 0.35f)));
    atrium.Decorations.Add((new(4.8f, 0, -5.5f), new(5.5f, 5, -4.8f), new(0.4f, 0.5f, 0.35f)));
    atrium.Decorations.Add((new(-5.5f, 0, 4.8f), new(-4.8f, 5, 5.5f), new(0.4f, 0.5f, 0.35f)));
    atrium.Decorations.Add((new(4.8f, 0, 4.8f), new(5.5f, 5, 5.5f), new(0.4f, 0.5f, 0.35f)));
    // Benches
    atrium.Decorations.Add((new(-3f, 0, -2f), new(-2f, 0.5f, -1.5f), new(0.3f, 0.4f, 0.25f)));
    atrium.Decorations.Add((new(2f, 0, 1.5f), new(3f, 0.5f, 2f), new(0.3f, 0.4f, 0.25f)));
    // Planter
    atrium.Decorations.Add((new(-4f, 0, 1f), new(-3f, 0.8f, 2f), new(0.45f, 0.35f, 0.2f)));
    atrium.Decorations.Add((new(-3.8f, 0.8f, 1.2f), new(-3.2f, 1.6f, 1.8f), new(0.2f, 0.55f, 0.2f)));

    // Room 3: The Chamber — golden yellow, small cozy
    var chamber = new Room
    {
      Name = "The Chamber",
      Width = 5, Height = 3, Depth = 5,
      WallColor = new(0.85f, 0.75f, 0.3f),
      FloorColor = new(0.4f, 0.35f, 0.15f),
      CeilColor = new(0.7f, 0.6f, 0.25f),
    };
    // Pedestal with glowing orb
    chamber.Decorations.Add((new(-0.4f, 0, -0.4f), new(0.4f, 1.2f, 0.4f), new(0.7f, 0.6f, 0.2f)));
    chamber.Decorations.Add((new(-0.2f, 1.2f, -0.2f), new(0.2f, 1.6f, 0.2f), new(1.0f, 0.95f, 0.5f)));
    // Shelves along walls
    chamber.Decorations.Add((new(-2.3f, 0, -2.3f), new(-1.5f, 2.0f, -1.9f), new(0.6f, 0.5f, 0.2f)));
    chamber.Decorations.Add((new(1.5f, 0, 1.9f), new(2.3f, 2.0f, 2.3f), new(0.6f, 0.5f, 0.2f)));
    // Rug
    chamber.Decorations.Add((new(-1.5f, 0.005f, -1.5f), new(1.5f, 0.015f, 1.5f), new(0.7f, 0.55f, 0.15f)));

    // Room 4: The Void — dark purple, very tall
    var voidRoom = new Room
    {
      Name = "The Void",
      Width = 10, Height = 8, Depth = 10,
      WallColor = new(0.35f, 0.15f, 0.55f),
      FloorColor = new(0.1f, 0.05f, 0.15f),
      CeilColor = new(0.2f, 0.1f, 0.35f),
    };
    // Floating cubes at various heights
    voidRoom.Decorations.Add((new(-2, 2, -2), new(-1, 3, -1), new(0.5f, 0.2f, 0.7f)));
    voidRoom.Decorations.Add((new(1, 3.5f, 1), new(2.5f, 5, 2.5f), new(0.4f, 0.15f, 0.6f)));
    voidRoom.Decorations.Add((new(-3, 5, 2), new(-1.5f, 6.5f, 3.5f), new(0.55f, 0.25f, 0.75f)));
    voidRoom.Decorations.Add((new(3, 1.5f, -3.5f), new(4, 2.5f, -2.5f), new(0.45f, 0.1f, 0.5f)));
    voidRoom.Decorations.Add((new(-4, 4, -4), new(-3.2f, 4.8f, -3.2f), new(0.6f, 0.3f, 0.8f)));
    voidRoom.Decorations.Add((new(0, 6, -1), new(1, 7, 0), new(0.3f, 0.1f, 0.45f)));
    // Stairway of blocks along one side
    for (int s = 0; s < 5; s++)
    {
      float sy = s * 0.5f;
      float sz = -4.5f + s * 1.2f;
      voidRoom.Decorations.Add((new(-4.5f, sy, sz), new(-3.5f, sy + 0.5f, sz + 0.8f), new(0.3f, 0.12f, 0.4f)));
    }
    // Tall thin obelisk
    voidRoom.Decorations.Add((new(3.5f, 0, -0.3f), new(4.0f, 6, 0.3f), new(0.25f, 0.1f, 0.4f)));

    var rooms = new[] { lobby, gallery, atrium, chamber, voidRoom };

    // ── Portal connections (non-euclidean!) ──

    // Lobby East ↔ Gallery West (normal)
    LinkPortals(rooms, 0, WallSide.East, 3f, 1, WallSide.West, 6f);

    // Gallery East ↔ Atrium West (normal)
    LinkPortals(rooms, 1, WallSide.East, 6f, 2, WallSide.West, 5f);

    // Atrium East ↔ Lobby West (THE LOOP — impossible in 3D!)
    LinkPortals(rooms, 2, WallSide.East, 5f, 0, WallSide.West, 3f);

    // Lobby North ↔ Chamber South
    LinkPortals(rooms, 0, WallSide.North, 3f, 3, WallSide.South, 1.5f);

    // Chamber North ↔ Atrium South (shortcut — skip Gallery)
    LinkPortals(rooms, 3, WallSide.North, 1.5f, 2, WallSide.South, 5f);

    // Atrium North ↔ Void South
    LinkPortals(rooms, 2, WallSide.North, 5f, 4, WallSide.South, 4f);

    // Void East ↔ Void West (same room portal!)
    LinkPortals(rooms, 4, WallSide.East, 4f, 4, WallSide.West, 4f);

    // Compute portal transforms
    ComputePortalTransforms(rooms);

    return rooms;
  }

  private static void LinkPortals(Room[] rooms, int srcRoom, WallSide srcWall, float srcAlongOffset,
    int dstRoom, WallSide dstWall, float dstAlongOffset)
  {
    var src = new PortalOpening
    {
      Wall = srcWall,
      OffsetAlongWall = srcAlongOffset,
      OffsetY = 0,
      Width = PortalWidth,
      Height = PortalHeight,
      DestRoomIndex = dstRoom,
      DestWall = dstWall,
    };

    var dst = new PortalOpening
    {
      Wall = dstWall,
      OffsetAlongWall = dstAlongOffset,
      OffsetY = 0,
      Width = PortalWidth,
      Height = PortalHeight,
      DestRoomIndex = srcRoom,
      DestWall = srcWall,
    };

    rooms[srcRoom].Portals.Add(src);
    rooms[dstRoom].Portals.Add(dst);
  }

  private static void ComputePortalTransforms(Room[] rooms)
  {
    foreach (var room in rooms)
    {
      foreach (var portal in room.Portals)
      {
        portal.Center = room.PortalCenter(portal);

        float srcYaw = Room.OutwardYaw(portal.Wall);
        float dstYaw = Room.OutwardYaw(portal.DestWall);
        portal.YawDelta = dstYaw + MathF.PI - srcYaw;

        // Build the transform matrix:
        // 1. Translate to source portal center (negate)
        // 2. Rotate by yaw delta around Y
        // 3. Translate to dest portal center
        var destRoom = rooms[portal.DestRoomIndex];
        // Find the matching dest portal opening
        var destPortal = destRoom.Portals.First(p =>
          p.DestRoomIndex == Array.IndexOf(rooms, room) && p.Wall == portal.DestWall);
        var destCenter = destRoom.PortalCenter(destPortal);

        float[] t1 = Mat4.Translation(-portal.Center.X, -portal.Center.Y, -portal.Center.Z);
        float[] rot = Mat4.RotationY(portal.YawDelta);
        float[] t2 = Mat4.Translation(destCenter.X, destCenter.Y, destCenter.Z);

        portal.Transform = Mat4.Multiply(t2, Mat4.Multiply(rot, t1));
      }
    }
  }
}
