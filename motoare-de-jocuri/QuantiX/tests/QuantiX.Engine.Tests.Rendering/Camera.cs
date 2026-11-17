using QuantiX.Engine.Mathematics.Vectors;

namespace QuantiX.Engine.Tests.Rendering;

public class Camera
{
  public Vec3<float> Position;
  public float Yaw, Pitch;

  private const float MoveSpeed = 3.5f;
  private const float LookSpeed = float.Pi / 12f;

  public const float Fov = float.Pi / 4f;

  public Vec3<float> Forward =>
    new
    (
      float.Sin(Yaw) * float.Cos(Pitch),
      float.Sin(Pitch),
      -float.Cos(Yaw) * float.Cos(Pitch)
    );

  public Vec3<float> Right => new(float.Cos(Yaw), 0, float.Sin(Yaw));

  public Vec3<float> Up => Vec3<float>.Cross(Right, Forward);

  public void MoveForward()
  {
    Position += new Vec3<float>(float.Sin(Yaw), 0, -float.Cos(Yaw)) * MoveSpeed;
  }

  public void MoveBackward()
  {
    Position -= new Vec3<float>(float.Sin(Yaw), 0, -float.Cos(Yaw)) * MoveSpeed;
  }

  public void MoveRight()
  {
    Position += new Vec3<float>(float.Cos(Yaw), 0, float.Sin(Yaw)) * MoveSpeed;
  }

  public void MoveLeft()
  {
    Position -= new Vec3<float>(float.Cos(Yaw), 0, float.Sin(Yaw)) * MoveSpeed;
  }

  public void MoveUp() => Position.Y += MoveSpeed;
  public void MoveDown() => Position.Y -= MoveSpeed;

  public void LookLeft() => Yaw -= LookSpeed;
  public void LookRight() => Yaw += LookSpeed;
  public void LookUp() => Pitch = float.Min(Pitch + LookSpeed, float.Pi / 2f - 0.01f);
  public void LookDown() => Pitch = float.Max(Pitch - LookSpeed, -float.Pi / 2f + 0.01f);

  public float[] GetViewProjection(float aspect)
  {
    Vec3<float> right = new(float.Cos(Yaw), 0, float.Sin(Yaw));
    float[] view = Mat4.LookAt(Position, Forward, right);
    float[] proj = Mat4.Perspective(float.Pi / 4f, aspect, 0.1f, 2000f);
    return Mat4.Multiply(proj, view);
  }
}
