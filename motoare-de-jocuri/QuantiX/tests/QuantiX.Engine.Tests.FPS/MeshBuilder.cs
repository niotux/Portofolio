using QuantiX.Engine.Mathematics.Vectors;

namespace QuantiX.Engine.Tests.FPS;

/// <summary>
/// Builds vertex/index arrays for 3D primitives.
/// Vertex format: pos(3) + normal(3) + uv(2) + ao(1) = 9 floats per vertex.
/// </summary>
public class MeshBuilder
{
  private readonly List<float> _verts = new();
  private readonly List<uint> _inds = new();

  public int VertexCount => _verts.Count / 9;
  public float[] GetVertices() => _verts.ToArray();
  public uint[] GetIndices() => _inds.ToArray();

  public void Clear()
  {
    _verts.Clear();
    _inds.Clear();
  }

  private void AddVertex(float px, float py, float pz, float nx, float ny, float nz, float u, float v, float ao)
  {
    _verts.Add(px); _verts.Add(py); _verts.Add(pz);
    _verts.Add(nx); _verts.Add(ny); _verts.Add(nz);
    _verts.Add(u); _verts.Add(v);
    _verts.Add(ao);
  }

  private void AddQuad(uint a, uint b, uint c, uint d)
  {
    _inds.Add(a); _inds.Add(b); _inds.Add(c);
    _inds.Add(c); _inds.Add(d); _inds.Add(a);
  }

  /// <summary>
  /// Add an axis-aligned box with texture region and AO.
  /// uvMin/uvMax define the texture atlas region.
  /// </summary>
  public void AddBox(Vec3<float> min, Vec3<float> max, float uMin, float vMin, float uMax, float vMax, float ao = 1f)
  {
    uint b = (uint)VertexCount;
    float x0 = min.X, y0 = min.Y, z0 = min.Z;
    float x1 = max.X, y1 = max.Y, z1 = max.Z;

    float sx = x1 - x0, sy = y1 - y0, sz = z1 - z0;

    // Front face (z+)
    AddVertex(x0, y0, z1, 0, 0, 1, uMin, vMin, ao);
    AddVertex(x1, y0, z1, 0, 0, 1, uMin + (uMax - uMin) * sx / MathF.Max(sx, 0.01f), vMin, ao);
    AddVertex(x1, y1, z1, 0, 0, 1, uMin + (uMax - uMin) * sx / MathF.Max(sx, 0.01f), vMax, ao);
    AddVertex(x0, y1, z1, 0, 0, 1, uMin, vMax, ao);
    AddQuad(b, b + 1, b + 2, b + 3);

    // Back face (z-)
    b = (uint)VertexCount;
    AddVertex(x1, y0, z0, 0, 0, -1, uMin, vMin, ao);
    AddVertex(x0, y0, z0, 0, 0, -1, uMax, vMin, ao);
    AddVertex(x0, y1, z0, 0, 0, -1, uMax, vMax, ao);
    AddVertex(x1, y1, z0, 0, 0, -1, uMin, vMax, ao);
    AddQuad(b, b + 1, b + 2, b + 3);

    // Right face (x+)
    b = (uint)VertexCount;
    AddVertex(x1, y0, z1, 1, 0, 0, uMin, vMin, ao);
    AddVertex(x1, y0, z0, 1, 0, 0, uMax, vMin, ao);
    AddVertex(x1, y1, z0, 1, 0, 0, uMax, vMax, ao);
    AddVertex(x1, y1, z1, 1, 0, 0, uMin, vMax, ao);
    AddQuad(b, b + 1, b + 2, b + 3);

    // Left face (x-)
    b = (uint)VertexCount;
    AddVertex(x0, y0, z0, -1, 0, 0, uMin, vMin, ao);
    AddVertex(x0, y0, z1, -1, 0, 0, uMax, vMin, ao);
    AddVertex(x0, y1, z1, -1, 0, 0, uMax, vMax, ao);
    AddVertex(x0, y1, z0, -1, 0, 0, uMin, vMax, ao);
    AddQuad(b, b + 1, b + 2, b + 3);

    // Top face (y+)
    b = (uint)VertexCount;
    AddVertex(x0, y1, z0, 0, 1, 0, uMin, vMin, ao);
    AddVertex(x0, y1, z1, 0, 1, 0, uMin, vMax, ao);
    AddVertex(x1, y1, z1, 0, 1, 0, uMax, vMax, ao);
    AddVertex(x1, y1, z0, 0, 1, 0, uMax, vMin, ao);
    AddQuad(b, b + 1, b + 2, b + 3);

    // Bottom face (y-)
    b = (uint)VertexCount;
    AddVertex(x0, y0, z1, 0, -1, 0, uMin, vMin, ao * 0.6f);
    AddVertex(x0, y0, z0, 0, -1, 0, uMin, vMax, ao * 0.6f);
    AddVertex(x1, y0, z0, 0, -1, 0, uMax, vMax, ao * 0.6f);
    AddVertex(x1, y0, z1, 0, -1, 0, uMax, vMin, ao * 0.6f);
    AddQuad(b, b + 1, b + 2, b + 3);
  }

  /// <summary>
  /// Add a floor plane at given Y, tiled as a grid of quads so each tile
  /// stays within the atlas UV region (no atlas bleed from GL repeat).
  /// </summary>
  public void AddFloor(float x0, float z0, float x1, float z1, float y, float uMin, float vMin, float uMax, float vMax, float tileScale = 1f)
  {
    int tilesX = Math.Max(1, (int)MathF.Ceiling((x1 - x0) / tileScale));
    int tilesZ = Math.Max(1, (int)MathF.Ceiling((z1 - z0) / tileScale));

    for (int tz = 0; tz < tilesZ; tz++)
    {
      for (int tx = 0; tx < tilesX; tx++)
      {
        float qx0 = x0 + tx * tileScale;
        float qz0 = z0 + tz * tileScale;
        float qx1 = MathF.Min(qx0 + tileScale, x1);
        float qz1 = MathF.Min(qz0 + tileScale, z1);

        // Fractional UV within this tile
        float fu = (qx1 - qx0) / tileScale;
        float fv = (qz1 - qz0) / tileScale;
        float u1 = uMin + (uMax - uMin) * fu;
        float v1 = vMin + (vMax - vMin) * fv;

        uint b = (uint)VertexCount;
        AddVertex(qx0, y, qz0, 0, 1, 0, uMin, vMin, 1f);
        AddVertex(qx0, y, qz1, 0, 1, 0, uMin, v1, 1f);
        AddVertex(qx1, y, qz1, 0, 1, 0, u1, v1, 1f);
        AddVertex(qx1, y, qz0, 0, 1, 0, u1, vMin, 1f);
        AddQuad(b, b + 1, b + 2, b + 3);
      }
    }
  }

  /// <summary>
  /// Add a ramp (angled surface from y0 at z0 to y1 at z1).
  /// </summary>
  public void AddRamp(Vec3<float> min, Vec3<float> max, float uMin, float vMin, float uMax, float vMax)
  {
    float x0 = min.X, z0 = min.Z, y0 = min.Y;
    float x1 = max.X, z1 = max.Z, y1 = max.Y;

    // Ramp surface normal (points "up" from the slope)
    float dz = z1 - z0, dy = y1 - y0;
    float len = MathF.Sqrt(dz * dz + dy * dy);
    float nx = 0, ny = dz / len, nz = -dy / len;

    // Ramp top surface — CCW winding when viewed from above
    uint b = (uint)VertexCount;
    AddVertex(x0, y1, z1, nx, ny, nz, uMin, vMax, 1f);
    AddVertex(x1, y1, z1, nx, ny, nz, uMax, vMax, 1f);
    AddVertex(x1, y0, z0, nx, ny, nz, uMax, vMin, 1f);
    AddVertex(x0, y0, z0, nx, ny, nz, uMin, vMin, 1f);
    AddQuad(b, b + 1, b + 2, b + 3);

    // Bottom face
    b = (uint)VertexCount;
    AddVertex(x0, y0, z0, 0, -1, 0, uMin, vMin, 0.5f);
    AddVertex(x0, y0, z1, 0, -1, 0, uMin, vMax, 0.5f);
    AddVertex(x1, y0, z1, 0, -1, 0, uMax, vMax, 0.5f);
    AddVertex(x1, y0, z0, 0, -1, 0, uMax, vMin, 0.5f);
    AddQuad(b, b + 1, b + 2, b + 3);

    // Back wall (z1 side, from y0 to y1)
    b = (uint)VertexCount;
    AddVertex(x1, y0, z1, 0, 0, 1, uMin, vMin, 0.7f);
    AddVertex(x0, y0, z1, 0, 0, 1, uMax, vMin, 0.7f);
    AddVertex(x0, y1, z1, 0, 0, 1, uMax, vMax, 0.7f);
    AddVertex(x1, y1, z1, 0, 0, 1, uMin, vMax, 0.7f);
    AddQuad(b, b + 1, b + 2, b + 3);

    // Left side wall (x0) — triangle
    b = (uint)VertexCount;
    AddVertex(x0, y0, z1, -1, 0, 0, uMin, vMin, 0.7f);
    AddVertex(x0, y0, z0, -1, 0, 0, uMax, vMin, 0.7f);
    AddVertex(x0, y1, z1, -1, 0, 0, uMin, vMax, 0.7f);
    _inds.Add(b); _inds.Add(b + 1); _inds.Add(b + 2);

    // Right side wall (x1) — triangle
    b = (uint)VertexCount;
    AddVertex(x1, y0, z0, 1, 0, 0, uMin, vMin, 0.7f);
    AddVertex(x1, y0, z1, 1, 0, 0, uMax, vMin, 0.7f);
    AddVertex(x1, y1, z1, 1, 0, 0, uMax, vMax, 0.7f);
    _inds.Add(b); _inds.Add(b + 1); _inds.Add(b + 2);
  }

  /// <summary>
  /// Add a simple humanoid (box-man) at origin, to be transformed by model matrix.
  /// Total height ~1.8 units. Returns AABB for the model.
  /// </summary>
  public void AddHumanoid(float uMin, float vMin, float uMax, float vMax)
  {
    // Torso
    AddBox(new(-0.25f, 0.7f, -0.15f), new(0.25f, 1.3f, 0.15f), uMin, vMin, uMax, vMax);
    // Head
    AddBox(new(-0.15f, 1.3f, -0.15f), new(0.15f, 1.65f, 0.15f), uMin, vMin, uMax, vMax);
    // Left arm
    AddBox(new(-0.4f, 0.7f, -0.1f), new(-0.25f, 1.25f, 0.1f), uMin, vMin, uMax, vMax);
    // Right arm
    AddBox(new(0.25f, 0.7f, -0.1f), new(0.4f, 1.25f, 0.1f), uMin, vMin, uMax, vMax);
    // Left leg
    AddBox(new(-0.2f, 0f, -0.12f), new(-0.02f, 0.7f, 0.12f), uMin, vMin, uMax, vMax);
    // Right leg
    AddBox(new(0.02f, 0f, -0.12f), new(0.2f, 0.7f, 0.12f), uMin, vMin, uMax, vMax);
  }

  /// <summary>
  /// Add a cylinder approximation (8-sided prism) for barrels.
  /// </summary>
  public void AddCylinder(Vec3<float> center, float radius, float halfHeight, float uMin, float vMin, float uMax, float vMax, int segments = 8)
  {
    float y0 = center.Y - halfHeight, y1 = center.Y + halfHeight;

    // Side faces
    for (int i = 0; i < segments; i++)
    {
      float a0 = i * MathF.Tau / segments;
      float a1 = (i + 1) * MathF.Tau / segments;
      float c0 = MathF.Cos(a0), s0 = MathF.Sin(a0);
      float c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);

      float nx = (c0 + c1) * 0.5f, nz = (s0 + s1) * 0.5f;
      float nLen = MathF.Sqrt(nx * nx + nz * nz);
      nx /= nLen; nz /= nLen;

      float u0 = uMin + (uMax - uMin) * i / segments;
      float u1 = uMin + (uMax - uMin) * (i + 1) / segments;

      uint b = (uint)VertexCount;
      AddVertex(center.X + c0 * radius, y0, center.Z + s0 * radius, nx, 0, nz, u0, vMin, 1f);
      AddVertex(center.X + c1 * radius, y0, center.Z + s1 * radius, nx, 0, nz, u1, vMin, 1f);
      AddVertex(center.X + c1 * radius, y1, center.Z + s1 * radius, nx, 0, nz, u1, vMax, 1f);
      AddVertex(center.X + c0 * radius, y1, center.Z + s0 * radius, nx, 0, nz, u0, vMax, 1f);
      AddQuad(b, b + 1, b + 2, b + 3);
    }

    // Top cap
    uint topCenter = (uint)VertexCount;
    AddVertex(center.X, y1, center.Z, 0, 1, 0, (uMin + uMax) * 0.5f, (vMin + vMax) * 0.5f, 1f);
    for (int i = 0; i < segments; i++)
    {
      float a0 = i * MathF.Tau / segments;
      float a1 = (i + 1) * MathF.Tau / segments;
      uint b = (uint)VertexCount;
      AddVertex(center.X + MathF.Cos(a0) * radius, y1, center.Z + MathF.Sin(a0) * radius, 0, 1, 0, uMin, vMin, 1f);
      AddVertex(center.X + MathF.Cos(a1) * radius, y1, center.Z + MathF.Sin(a1) * radius, 0, 1, 0, uMax, vMin, 1f);
      _inds.Add(topCenter); _inds.Add(b); _inds.Add(b + 1);
    }

    // Bottom cap
    uint botCenter = (uint)VertexCount;
    AddVertex(center.X, y0, center.Z, 0, -1, 0, (uMin + uMax) * 0.5f, (vMin + vMax) * 0.5f, 0.5f);
    for (int i = 0; i < segments; i++)
    {
      float a0 = i * MathF.Tau / segments;
      float a1 = (i + 1) * MathF.Tau / segments;
      uint b = (uint)VertexCount;
      AddVertex(center.X + MathF.Cos(a1) * radius, y0, center.Z + MathF.Sin(a1) * radius, 0, -1, 0, uMin, vMin, 0.5f);
      AddVertex(center.X + MathF.Cos(a0) * radius, y0, center.Z + MathF.Sin(a0) * radius, 0, -1, 0, uMax, vMin, 0.5f);
      _inds.Add(botCenter); _inds.Add(b); _inds.Add(b + 1);
    }
  }
}
