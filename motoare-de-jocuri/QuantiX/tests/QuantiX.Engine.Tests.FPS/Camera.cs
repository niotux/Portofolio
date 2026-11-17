using QuantiX.Engine.Mathematics.Vectors;

namespace QuantiX.Engine.Tests.FPS;

public class Camera
{
  public Vec3<float> Position;
  public float Yaw, Pitch;

  public const float Fov = MathF.PI / 4f;
  public const float NearPlane = 0.1f;
  public const float FarPlane = 500f;

  // Movement tuning
  public float MaxSpeed = 12f;
  public float Acceleration = 80f;
  public float Friction = 10f; // deceleration multiplier when no input

  // Look tuning
  public float LookSpeed = 3.0f; // radians/sec
  public float LookSmoothing = 20f; // interpolation speed

  // Eye height above feet position
  public const float EyeHeight = 1.6f;
  public const float PlayerHeight = 1.8f;
  public const float PlayerRadius = 0.3f;

  // Current velocities (updated each frame)
  public Vec3<float> Velocity;
  private float _yawVelocity;
  private float _pitchVelocity;

  public Vec3<float> Forward =>
    new(
      MathF.Sin(Yaw) * MathF.Cos(Pitch),
      MathF.Sin(Pitch),
      -MathF.Cos(Yaw) * MathF.Cos(Pitch)
    );

  public Vec3<float> Right => new(MathF.Cos(Yaw), 0, MathF.Sin(Yaw));

  public Vec3<float> Up => Vec3<float>.Cross(Right, Forward);

  // Horizontal forward (no pitch, for movement)
  public Vec3<float> FlatForward => new(MathF.Sin(Yaw), 0, -MathF.Cos(Yaw));

  /// <summary>
  /// Call each frame with desired movement direction (-1 to 1 per axis) and look input.
  /// </summary>
  public void UpdateMovement(float dt, float moveForward, float moveRight, float lookYaw, float lookPitch)
  {
    // ── Movement with acceleration + friction ──
    Vec3<float> wishDir = FlatForward * moveForward + Right * moveRight;

    // Normalize if combined input exceeds 1
    float wishLen = MathF.Sqrt(wishDir.X * wishDir.X + wishDir.Z * wishDir.Z);
    if (wishLen > 1f)
      wishDir = wishDir * (1f / wishLen);

    if (wishLen > 0.01f)
    {
      // Accelerate toward wish direction
      Velocity.X += wishDir.X * Acceleration * dt;
      Velocity.Z += wishDir.Z * Acceleration * dt;
    }
    else
    {
      // Apply friction (exponential decay)
      float decay = MathF.Exp(-Friction * dt);
      Velocity.X *= decay;
      Velocity.Z *= decay;
    }

    // Clamp horizontal speed
    float hSpeed = MathF.Sqrt(Velocity.X * Velocity.X + Velocity.Z * Velocity.Z);
    if (hSpeed > MaxSpeed)
    {
      float scale = MaxSpeed / hSpeed;
      Velocity.X *= scale;
      Velocity.Z *= scale;
    }

    // Apply horizontal velocity to position
    Position.X += Velocity.X * dt;
    Position.Z += Velocity.Z * dt;

    // ── Smooth look ──
    float targetYawVel = lookYaw * LookSpeed;
    float targetPitchVel = lookPitch * LookSpeed;

    // Smooth interpolation toward target angular velocity
    float lerpFactor = 1f - MathF.Exp(-LookSmoothing * dt);
    _yawVelocity += (targetYawVel - _yawVelocity) * lerpFactor;
    _pitchVelocity += (targetPitchVel - _pitchVelocity) * lerpFactor;

    Yaw += _yawVelocity * dt;
    Pitch += _pitchVelocity * dt;
    Pitch = Math.Clamp(Pitch, -MathF.PI / 2f + 0.01f, MathF.PI / 2f - 0.01f);
  }

  public float[] GetViewProjection(float aspect)
  {
    float[] view = Mat4.LookAt(Position, Forward, Right);
    float[] proj = Mat4.Perspective(Fov, aspect, NearPlane, FarPlane);
    return Mat4.Multiply(proj, view);
  }

  public float[] GetViewMatrix() => Mat4.LookAt(Position, Forward, Right);
  public float[] GetProjectionMatrix(float aspect) => Mat4.Perspective(Fov, aspect, NearPlane, FarPlane);
}
