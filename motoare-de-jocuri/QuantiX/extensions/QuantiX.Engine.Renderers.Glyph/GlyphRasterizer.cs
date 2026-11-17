using System.Runtime.InteropServices;
using QuantiX.Engine.Rendering;

namespace QuantiX.Engine.Renderers.Glyph;

/// <summary>
/// Software rasterizer that converts triangle pipelines into a character cell framebuffer.
/// Each cell stores a brightness value (0-1) and depth, which is later mapped to an ASCII glyph.
/// </summary>
internal sealed class GlyphRasterizer
{
  // Ordered from darkest to brightest
  private static readonly char[] BrightnessRamp = [' ', '.', ':', '-', '=', '+', '*', '#', '%', '@'];

  private uint _width;
  private uint _height;
  private float[] _colorBuffer;   // brightness per cell
  private float[] _depthBuffer;   // depth per cell (0..1, 1 = far)

  public GlyphRasterizer(uint width, uint height)
  {
    _width = width;
    _height = height;
    _colorBuffer = new float[width * height];
    _depthBuffer = new float[width * height];
    Clear();
  }

  public uint Width => _width;
  public uint Height => _height;

  public void Resize(uint width, uint height)
  {
    if (_width == width && _height == height) return;
    _width = width;
    _height = height;
    _colorBuffer = new float[width * height];
    _depthBuffer = new float[width * height];
    Clear();
  }

  public void Clear()
  {
    Array.Fill(_colorBuffer, 0f);
    Array.Fill(_depthBuffer, 1f);
  }

  public void Rasterize(Pipeline[] pipelines)
  {
    foreach (var pipeline in pipelines)
    {
      foreach (var entry in pipeline.DrawEntries)
      {
        RasterizeEntry(pipeline, entry);
      }
    }
  }

  private void RasterizeEntry(Pipeline pipeline, DrawEntry entry)
  {
    var vertexBuffer = GlyphRenderer.ResolveBuffer(entry.VertexBuffer);
    var indexBuffer = GlyphRenderer.ResolveBuffer(entry.IndexBuffer);
    var uniformBuffer = GlyphRenderer.ResolveBuffer(entry.UniformBuffer);
    if (vertexBuffer == null || indexBuffer == null) return;

    ReadOnlySpan<byte> vbData = vertexBuffer.Data;
    ReadOnlySpan<byte> ibData = indexBuffer.Data;

    // Read indices as uint32
    ReadOnlySpan<uint> indices = MemoryMarshal.Cast<byte, uint>(ibData);

    // Determine vertex stride and position offset from attributes
    uint stride = 0;
    uint posOffset = 0;
    uint posSize = 3;
    foreach (var attr in pipeline.VertexAttributes)
    {
      if (attr.Location == 0) // assume location 0 = position
      {
        posOffset = attr.Offset;
        posSize = attr.Size;
      }
      if (attr.Stride > stride) stride = attr.Stride;
    }
    if (stride == 0) return;

    var topology = pipeline.State.Topology;
    bool depthWrite = pipeline.State.DepthWrite;
    var depthFunc = pipeline.State.DepthFunc;

    if (topology == PrimitiveTopology.Triangles)
    {
      uint start = entry.IndexStart;
      uint count = entry.IndexCount;
      int baseVertex = entry.VertexOffset;

      for (uint i = start; i + 2 < start + count; i += 3)
      {
        uint i0 = indices[(int)i];
        uint i1 = indices[(int)(i + 1)];
        uint i2 = indices[(int)(i + 2)];

        var v0 = ReadPosition(vbData, (int)(i0 + baseVertex), stride, posOffset, posSize);
        var v1 = ReadPosition(vbData, (int)(i1 + baseVertex), stride, posOffset, posSize);
        var v2 = ReadPosition(vbData, (int)(i2 + baseVertex), stride, posOffset, posSize);

        RasterizeTriangle(v0, v1, v2, depthWrite, depthFunc);
      }
    }
    else if (topology == PrimitiveTopology.Lines || topology == PrimitiveTopology.LineStrip)
    {
      uint start = entry.IndexStart;
      uint count = entry.IndexCount;
      int baseVertex = entry.VertexOffset;
      uint step = topology == PrimitiveTopology.Lines ? 2u : 1u;
      uint limit = topology == PrimitiveTopology.Lines ? 1u : 0u;

      for (uint i = start; i + limit < start + count; i += step)
      {
        uint i0 = indices[(int)i];
        uint i1 = indices[(int)(i + 1)];

        var v0 = ReadPosition(vbData, (int)(i0 + baseVertex), stride, posOffset, posSize);
        var v1 = ReadPosition(vbData, (int)(i1 + baseVertex), stride, posOffset, posSize);

        RasterizeLine(v0, v1, depthWrite, depthFunc);
      }
    }
    else if (topology == PrimitiveTopology.Points)
    {
      uint start = entry.IndexStart;
      uint count = entry.IndexCount;
      int baseVertex = entry.VertexOffset;

      for (uint i = start; i < start + count; i++)
      {
        uint idx = indices[(int)i];
        var v = ReadPosition(vbData, (int)(idx + baseVertex), stride, posOffset, posSize);
        PlotPoint(v, depthWrite, depthFunc);
      }
    }
  }

  private static (float X, float Y, float Z) ReadPosition(ReadOnlySpan<byte> vbData, int vertexIndex, uint stride, uint posOffset, uint posSize)
  {
    int byteStart = (int)(vertexIndex * stride + posOffset);
    if (byteStart + posSize * 4 > vbData.Length)
      return (0, 0, 0);

    ReadOnlySpan<float> floats = MemoryMarshal.Cast<byte, float>(vbData.Slice(byteStart, (int)(posSize * 4)));
    float x = floats[0];
    float y = floats.Length > 1 ? floats[1] : 0;
    float z = floats.Length > 2 ? floats[2] : 0;
    return (x, y, z);
  }

  // Maps NDC (-1..1) to screen coordinates
  private (int X, int Y) NdcToScreen(float x, float y)
  {
    int sx = (int)((x * 0.5f + 0.5f) * _width);
    int sy = (int)((-y * 0.5f + 0.5f) * _height); // flip Y: NDC Y-up, screen Y-down
    return (sx, sy);
  }

  private float NdcToDepth(float z)
  {
    return z * 0.5f + 0.5f; // map -1..1 to 0..1
  }

  private void RasterizeTriangle(
    (float X, float Y, float Z) v0,
    (float X, float Y, float Z) v1,
    (float X, float Y, float Z) v2,
    bool depthWrite, DepthFunc depthFunc)
  {
    var (sx0, sy0) = NdcToScreen(v0.X, v0.Y);
    var (sx1, sy1) = NdcToScreen(v1.X, v1.Y);
    var (sx2, sy2) = NdcToScreen(v2.X, v2.Y);
    float d0 = NdcToDepth(v0.Z);
    float d1 = NdcToDepth(v1.Z);
    float d2 = NdcToDepth(v2.Z);

    // Bounding box
    int minX = Math.Max(0, Math.Min(sx0, Math.Min(sx1, sx2)));
    int maxX = Math.Min((int)_width - 1, Math.Max(sx0, Math.Max(sx1, sx2)));
    int minY = Math.Max(0, Math.Min(sy0, Math.Min(sy1, sy2)));
    int maxY = Math.Min((int)_height - 1, Math.Max(sy0, Math.Max(sy1, sy2)));

    float area = EdgeFunction(sx0, sy0, sx1, sy1, sx2, sy2);
    if (MathF.Abs(area) < 0.001f) return; // degenerate

    // Simple flat shading: use triangle normal for brightness
    // Cross product of edges gives facing direction
    float ex1 = v1.X - v0.X, ey1 = v1.Y - v0.Y, ez1 = v1.Z - v0.Z;
    float ex2 = v2.X - v0.X, ey2 = v2.Y - v0.Y, ez2 = v2.Z - v0.Z;
    float nx = ey1 * ez2 - ez1 * ey2;
    float ny = ez1 * ex2 - ex1 * ez2;
    float nz = ex1 * ey2 - ey1 * ex2;
    float nLen = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
    if (nLen > 0.0001f) { nx /= nLen; ny /= nLen; nz /= nLen; }

    // Light direction: upper-right-front
    float brightness = Math.Clamp(nx * 0.3f + ny * 0.3f + nz * 0.7f, 0.1f, 1.0f);

    for (int y = minY; y <= maxY; y++)
    {
      for (int x = minX; x <= maxX; x++)
      {
        float w0 = EdgeFunction(sx1, sy1, sx2, sy2, x, y);
        float w1 = EdgeFunction(sx2, sy2, sx0, sy0, x, y);
        float w2 = EdgeFunction(sx0, sy0, sx1, sy1, x, y);

        // Check if point is inside triangle (both windings)
        bool inside = (area > 0) ? (w0 >= 0 && w1 >= 0 && w2 >= 0) : (w0 <= 0 && w1 <= 0 && w2 <= 0);
        if (!inside) continue;

        // Barycentric interpolation for depth
        float invArea = 1.0f / area;
        float b0 = w0 * invArea;
        float b1 = w1 * invArea;
        float b2 = w2 * invArea;
        float depth = b0 * d0 + b1 * d1 + b2 * d2;

        int idx = y * (int)_width + x;
        if (idx < 0 || idx >= _colorBuffer.Length) continue;

        if (!PassesDepthTest(depth, _depthBuffer[idx], depthFunc)) continue;

        _colorBuffer[idx] = brightness;
        if (depthWrite) _depthBuffer[idx] = depth;
      }
    }
  }

  private void RasterizeLine(
    (float X, float Y, float Z) v0,
    (float X, float Y, float Z) v1,
    bool depthWrite, DepthFunc depthFunc)
  {
    var (sx0, sy0) = NdcToScreen(v0.X, v0.Y);
    var (sx1, sy1) = NdcToScreen(v1.X, v1.Y);
    float d0 = NdcToDepth(v0.Z);
    float d1 = NdcToDepth(v1.Z);

    // Bresenham's line algorithm
    int dx = Math.Abs(sx1 - sx0);
    int dy = -Math.Abs(sy1 - sy0);
    int sx = sx0 < sx1 ? 1 : -1;
    int sy = sy0 < sy1 ? 1 : -1;
    int err = dx + dy;
    int totalSteps = Math.Max(dx, -dy);
    int step = 0;

    int cx = sx0, cy = sy0;
    while (true)
    {
      float t = totalSteps > 0 ? (float)step / totalSteps : 0;
      float depth = d0 + t * (d1 - d0);

      if (cx >= 0 && cx < (int)_width && cy >= 0 && cy < (int)_height)
      {
        int idx = cy * (int)_width + cx;
        if (PassesDepthTest(depth, _depthBuffer[idx], depthFunc))
        {
          _colorBuffer[idx] = 0.8f;
          if (depthWrite) _depthBuffer[idx] = depth;
        }
      }

      if (cx == sx1 && cy == sy1) break;
      int e2 = 2 * err;
      if (e2 >= dy) { err += dy; cx += sx; }
      if (e2 <= dx) { err += dx; cy += sy; }
      step++;
    }
  }

  private void PlotPoint(
    (float X, float Y, float Z) v,
    bool depthWrite, DepthFunc depthFunc)
  {
    var (sx, sy) = NdcToScreen(v.X, v.Y);
    float depth = NdcToDepth(v.Z);

    if (sx >= 0 && sx < (int)_width && sy >= 0 && sy < (int)_height)
    {
      int idx = sy * (int)_width + sx;
      if (PassesDepthTest(depth, _depthBuffer[idx], depthFunc))
      {
        _colorBuffer[idx] = 1.0f;
        if (depthWrite) _depthBuffer[idx] = depth;
      }
    }
  }

  private static float EdgeFunction(int ax, int ay, int bx, int by, int px, int py)
  {
    return (float)((px - ax) * (by - ay) - (py - ay) * (bx - ax));
  }

  private static bool PassesDepthTest(float incoming, float current, DepthFunc func)
  {
    return func switch
    {
      DepthFunc.Always => true,
      DepthFunc.Never => false,
      DepthFunc.Less => incoming < current,
      DepthFunc.LessOrEqual => incoming <= current,
      DepthFunc.Greater => incoming > current,
      DepthFunc.GreaterOrEqual => incoming >= current,
      DepthFunc.Equal => MathF.Abs(incoming - current) < 0.0001f,
      DepthFunc.NotEqual => MathF.Abs(incoming - current) >= 0.0001f,
      _ => true
    };
  }

  /// <summary>
  /// Renders the color buffer to a character array for console output.
  /// </summary>
  public void Present(char[] output)
  {
    int lineWidth = (int)_width + 1; // +1 for newline
    for (int y = 0; y < (int)_height; y++)
    {
      for (int x = 0; x < (int)_width; x++)
      {
        float brightness = _colorBuffer[y * (int)_width + x];
        int rampIdx = (int)(brightness * (BrightnessRamp.Length - 1));
        rampIdx = Math.Clamp(rampIdx, 0, BrightnessRamp.Length - 1);
        output[y * lineWidth + x] = BrightnessRamp[rampIdx];
      }
      output[y * lineWidth + (int)_width] = '\n';
    }
  }

  /// <summary>
  /// Returns the required output buffer size for Present().
  /// </summary>
  public int GetOutputBufferSize()
  {
    return (int)((_width + 1) * _height);
  }
}
