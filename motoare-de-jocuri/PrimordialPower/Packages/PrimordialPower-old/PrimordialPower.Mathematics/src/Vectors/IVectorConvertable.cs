using System;
using System.Numerics;

namespace PrimordialPower.Mathematics.Vectors;

public interface IVectorConvertible
{
  Vec2F ToVec2F();
  Vec3F ToVec3F();
  Vec4F ToVec4F();
  Vec5F ToVec5F();
  Vec2D ToVec2D();
  Vec3D ToVec3D();
  Vec4D ToVec4D();
  Vec5D ToVec5D();
  Vec2I ToVec2I();
  Vec3I ToVec3I();
  Vec4I ToVec4I();
  Vec5I ToVec5I();
}
