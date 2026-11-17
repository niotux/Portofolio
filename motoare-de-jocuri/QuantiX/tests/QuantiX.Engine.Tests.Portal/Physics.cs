using QuantiX.Engine.Mathematics.Vectors;

namespace QuantiX.Engine.Tests.Portal;

public struct AABB
{
  public Vec3<float> Min;
  public Vec3<float> Max;

  public AABB(Vec3<float> min, Vec3<float> max) { Min = min; Max = max; }

  public readonly bool Intersects(AABB other) =>
    Min.X < other.Max.X && Max.X > other.Min.X &&
    Min.Y < other.Max.Y && Max.Y > other.Min.Y &&
    Min.Z < other.Max.Z && Max.Z > other.Min.Z;

  public AABB Translated(Vec3<float> offset) =>
    new(Min + offset, Max + offset);
}

public static class Physics
{
  public static Vec3<float> MoveAndSlide(
    AABB mover,
    Vec3<float> delta,
    ReadOnlySpan<AABB> statics)
  {
    // X axis
    AABB test = mover.Translated(new Vec3<float>(delta.X, 0, 0));
    bool blocked = false;
    foreach (ref readonly var s in statics)
      if (test.Intersects(s)) { blocked = true; break; }
    if (blocked) delta.X = 0;
    else mover = test;

    // Y axis
    test = mover.Translated(new Vec3<float>(0, delta.Y, 0));
    blocked = false;
    foreach (ref readonly var s in statics)
      if (test.Intersects(s)) { blocked = true; break; }
    if (blocked) delta.Y = 0;
    else mover = test;

    // Z axis
    test = mover.Translated(new Vec3<float>(0, 0, delta.Z));
    blocked = false;
    foreach (ref readonly var s in statics)
      if (test.Intersects(s)) { blocked = true; break; }
    if (blocked) delta.Z = 0;

    return delta;
  }
}
