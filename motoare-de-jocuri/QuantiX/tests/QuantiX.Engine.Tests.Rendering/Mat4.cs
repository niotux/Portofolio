using QuantiX.Engine.Mathematics.Vectors;

namespace QuantiX.Engine.Tests.Rendering;

public static class Mat4
{
  public static float[] Multiply(float[] a, float[] b)
  {
    float[] r = new float[16];
    for (int col = 0; col < 4; col++)
    {
      for (int row = 0; row < 4; row++)
      {
        float sum = 0;
        for (int k = 0; k < 4; k++)
        {
          sum += a[k * 4 + row] * b[col * 4 + k];
        }
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
      0, 0, (far + near) / (near - far),  -1,
      0, 0, 2 * far * near / (near - far),  0,
    ];
  }

  public static float[] Ortho(float left, float right, float bottom, float top, float near, float far)
  {
    return
    [
      2f / (right - left), 0, 0, 0,
      0, 2f / (top - bottom), 0, 0,
      0, 0, -2f / (far - near), 0,
      -(right + left) / (right - left), -(top + bottom) / (top - bottom), -(far + near) / (far - near), 1,
    ];
  }

  public static float[] LookAt(Vec3<float> eye, Vec3<float> forward, Vec3<float> right)
  {
    // up = cross(right, forward)
    Vec3<float> up = Vec3<float>.Cross(right, forward);

    return
    [
      right.X, up.X, -forward.X, 0,
      right.Y, up.Y, -forward.Y, 0,
      right.Z, up.Z, -forward.Z, 0,
      -Vec3<float>.Dot(right, eye), -Vec3<float>.Dot(up, eye), Vec3<float>.Dot(forward, eye), 1,
    ];
  }
}
