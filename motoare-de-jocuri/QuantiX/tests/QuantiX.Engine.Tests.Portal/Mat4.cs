using QuantiX.Engine.Mathematics.Vectors;

namespace QuantiX.Engine.Tests.Portal;

public static class Mat4
{
  public static float[] Identity() =>
  [
    1, 0, 0, 0,
    0, 1, 0, 0,
    0, 0, 1, 0,
    0, 0, 0, 1,
  ];

  public static float[] Multiply(float[] a, float[] b)
  {
    float[] r = new float[16];
    for (int col = 0; col < 4; col++)
    {
      for (int row = 0; row < 4; row++)
      {
        float sum = 0;
        for (int k = 0; k < 4; k++)
          sum += a[k * 4 + row] * b[col * 4 + k];
        r[col * 4 + row] = sum;
      }
    }
    return r;
  }

  public static float[] Perspective(float fovRadians, float aspect, float near, float far)
  {
    float f = 1f / MathF.Tan(fovRadians / 2f);
    return
    [
      f / aspect, 0, 0, 0,
      0, f, 0, 0,
      0, 0, (far + near) / (near - far), -1,
      0, 0, 2 * far * near / (near - far), 0,
    ];
  }

  public static float[] LookAt(Vec3<float> eye, Vec3<float> forward, Vec3<float> right)
  {
    Vec3<float> up = Vec3<float>.Cross(right, forward);
    return
    [
      right.X, up.X, -forward.X, 0,
      right.Y, up.Y, -forward.Y, 0,
      right.Z, up.Z, -forward.Z, 0,
      -Vec3<float>.Dot(right, eye), -Vec3<float>.Dot(up, eye), Vec3<float>.Dot(forward, eye), 1,
    ];
  }

  public static float[] Translation(float x, float y, float z) =>
  [
    1, 0, 0, 0,
    0, 1, 0, 0,
    0, 0, 1, 0,
    x, y, z, 1,
  ];

  public static float[] RotationY(float radians)
  {
    float c = MathF.Cos(radians), s = MathF.Sin(radians);
    return
    [
      c, 0, s, 0,
      0, 1, 0, 0,
      -s, 0, c, 0,
      0, 0, 0, 1,
    ];
  }

  /// <summary>
  /// Transforms a point by a 4x4 matrix (assumes w=1).
  /// </summary>
  public static Vec3<float> TransformPoint(float[] m, Vec3<float> p) =>
    new(
      m[0] * p.X + m[4] * p.Y + m[8] * p.Z + m[12],
      m[1] * p.X + m[5] * p.Y + m[9] * p.Z + m[13],
      m[2] * p.X + m[6] * p.Y + m[10] * p.Z + m[14]
    );

  /// <summary>
  /// Transforms a direction by a 4x4 matrix (no translation).
  /// </summary>
  public static Vec3<float> TransformDir(float[] m, Vec3<float> d) =>
    new(
      m[0] * d.X + m[4] * d.Y + m[8] * d.Z,
      m[1] * d.X + m[5] * d.Y + m[9] * d.Z,
      m[2] * d.X + m[6] * d.Y + m[10] * d.Z
    );
}
