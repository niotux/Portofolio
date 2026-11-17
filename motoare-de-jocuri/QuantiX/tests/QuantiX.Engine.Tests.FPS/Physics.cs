using QuantiX.Engine.Mathematics.Vectors;

namespace QuantiX.Engine.Tests.FPS;

public struct AABB
{
  public Vec3<float> Min;
  public Vec3<float> Max;

  public AABB(Vec3<float> min, Vec3<float> max) { Min = min; Max = max; }

  public AABB(Vec3<float> center, float halfW, float halfH, float halfD)
  {
    Min = new(center.X - halfW, center.Y - halfH, center.Z - halfD);
    Max = new(center.X + halfW, center.Y + halfH, center.Z + halfD);
  }

  public readonly Vec3<float> Center => new(
    (Min.X + Max.X) * 0.5f,
    (Min.Y + Max.Y) * 0.5f,
    (Min.Z + Max.Z) * 0.5f);

  public readonly Vec3<float> Size => Max - Min;

  public readonly bool Intersects(AABB other) =>
    Min.X <= other.Max.X && Max.X >= other.Min.X &&
    Min.Y <= other.Max.Y && Max.Y >= other.Min.Y &&
    Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;

  public readonly bool Contains(Vec3<float> point) =>
    point.X >= Min.X && point.X <= Max.X &&
    point.Y >= Min.Y && point.Y <= Max.Y &&
    point.Z >= Min.Z && point.Z <= Max.Z;

  public AABB Translated(Vec3<float> offset) =>
    new(Min + offset, Max + offset);
}

public static class Physics
{
  public const float Gravity = 20f;
  public const float JumpVelocity = 8f;
  public const float TerminalVelocity = 50f;

  /// <summary>
  /// Move an AABB through the world, resolving collisions against static AABBs.
  /// Returns the resolved position delta.
  /// </summary>
  public static Vec3<float> MoveAndSlide(
    AABB mover,
    Vec3<float> velocity,
    float dt,
    ReadOnlySpan<AABB> statics,
    out bool onGround)
  {
    onGround = false;
    Vec3<float> delta = velocity * dt;

    // Resolve each axis independently (sweep and resolve)
    // X axis
    AABB test = mover.Translated(new Vec3<float>(delta.X, 0, 0));
    bool blocked = false;
    foreach (ref readonly var s in statics)
    {
      if (test.Intersects(s)) { blocked = true; break; }
    }
    if (blocked) delta.X = 0;
    else mover = test;

    // Y axis
    test = mover.Translated(new Vec3<float>(0, delta.Y, 0));
    blocked = false;
    foreach (ref readonly var s in statics)
    {
      if (test.Intersects(s))
      {
        blocked = true;
        if (delta.Y < 0) onGround = true;
        break;
      }
    }
    if (blocked) delta.Y = 0;
    else mover = test;

    // Z axis
    test = mover.Translated(new Vec3<float>(0, 0, delta.Z));
    blocked = false;
    foreach (ref readonly var s in statics)
    {
      if (test.Intersects(s)) { blocked = true; break; }
    }
    if (blocked) delta.Z = 0;

    return delta;
  }

  /// <summary>
  /// Simple ray-AABB intersection. Returns distance or null.
  /// </summary>
  public static float? RayIntersect(Vec3<float> origin, Vec3<float> dir, AABB box)
  {
    float tmin = float.NegativeInfinity;
    float tmax = float.PositiveInfinity;

    if (MathF.Abs(dir.X) > 1e-8f)
    {
      float t1 = (box.Min.X - origin.X) / dir.X;
      float t2 = (box.Max.X - origin.X) / dir.X;
      if (t1 > t2) (t1, t2) = (t2, t1);
      tmin = MathF.Max(tmin, t1);
      tmax = MathF.Min(tmax, t2);
    }
    else if (origin.X < box.Min.X || origin.X > box.Max.X) return null;

    if (MathF.Abs(dir.Y) > 1e-8f)
    {
      float t1 = (box.Min.Y - origin.Y) / dir.Y;
      float t2 = (box.Max.Y - origin.Y) / dir.Y;
      if (t1 > t2) (t1, t2) = (t2, t1);
      tmin = MathF.Max(tmin, t1);
      tmax = MathF.Min(tmax, t2);
    }
    else if (origin.Y < box.Min.Y || origin.Y > box.Max.Y) return null;

    if (MathF.Abs(dir.Z) > 1e-8f)
    {
      float t1 = (box.Min.Z - origin.Z) / dir.Z;
      float t2 = (box.Max.Z - origin.Z) / dir.Z;
      if (t1 > t2) (t1, t2) = (t2, t1);
      tmin = MathF.Max(tmin, t1);
      tmax = MathF.Min(tmax, t2);
    }
    else if (origin.Z < box.Min.Z || origin.Z > box.Max.Z) return null;

    if (tmin > tmax || tmax < 0) return null;
    return tmin >= 0 ? tmin : tmax;
  }
}
