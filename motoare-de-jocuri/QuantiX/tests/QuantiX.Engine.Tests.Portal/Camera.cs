using QuantiX.Engine.Mathematics.Vectors;

namespace QuantiX.Engine.Tests.Portal;

public class Camera
{
  public Vec3<float> Position;
  public float Yaw, Pitch;

  public const float Fov = MathF.PI / 4f;
  private const float MoveSpeed = 4.0f;
  private const float LookSpeed = MathF.PI / 10f;
  public const float EyeHeight = 1.6f;
  public const float PlayerHeight = 1.8f;
  public const float PlayerRadius = 0.25f;

  public Vec3<float> Forward =>
    new(
      MathF.Sin(Yaw) * MathF.Cos(Pitch),
      MathF.Sin(Pitch),
      -MathF.Cos(Yaw) * MathF.Cos(Pitch)
    );

  public Vec3<float> Right => new(MathF.Cos(Yaw), 0, MathF.Sin(Yaw));
  public Vec3<float> Up => Vec3<float>.Cross(Right, Forward);

  public void MoveForward() { Position += new Vec3<float>(MathF.Sin(Yaw), 0, -MathF.Cos(Yaw)) * MoveSpeed * 0.016f; }
  public void MoveBackward() { Position -= new Vec3<float>(MathF.Sin(Yaw), 0, -MathF.Cos(Yaw)) * MoveSpeed * 0.016f; }
  public void MoveRight() { Position += new Vec3<float>(MathF.Cos(Yaw), 0, MathF.Sin(Yaw)) * MoveSpeed * 0.016f; }
  public void MoveLeft() { Position -= new Vec3<float>(MathF.Cos(Yaw), 0, MathF.Sin(Yaw)) * MoveSpeed * 0.016f; }

  public void LookLeft() => Yaw -= LookSpeed;
  public void LookRight() => Yaw += LookSpeed;
  public void LookUp() => Pitch = MathF.Min(Pitch + LookSpeed, MathF.PI / 2f - 0.01f);
  public void LookDown() => Pitch = MathF.Max(Pitch - LookSpeed, -MathF.PI / 2f + 0.01f);

  public float[] GetViewProjection(float aspect)
  {
    float[] view = Mat4.LookAt(Position, Forward, Right);
    float[] proj = Mat4.Perspective(Fov, aspect, 0.05f, 200f);
    return Mat4.Multiply(proj, view);
  }
}
