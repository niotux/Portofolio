namespace QuantiX.Engine.Tests.Rendering;

internal static class Noise
{
  public static float Value(float x, float z, int seed)
  {
    int xi = (int)MathF.Floor(x);
    int zi = (int)MathF.Floor(z);
    float fx = x - xi, fz = z - zi;
    fx = fx * fx * (3 - 2 * fx);
    fz = fz * fz * (3 - 2 * fz);
    static float Lerp(float a, float b, float t) => a + (b - a) * t;
    float row0 = Lerp(Hash(xi, zi, seed), Hash(xi + 1, zi, seed), fx);
    float row1 = Lerp(Hash(xi, zi + 1, seed), Hash(xi + 1, zi + 1, seed), fx);
    return Lerp(row0, row1, fz);
  }

  public static float Fbm(float x, float z, int seed, int octaves = 4, float lacunarity = 2f, float gain = 0.5f)
  {
    float value = 0f, amplitude = 0.5f, maxVal = 0f, freq = 1f;
    for (int i = 0; i < octaves; i++)
    {
      value += Value(x * freq, z * freq, seed + i * 997) * amplitude;
      maxVal += amplitude;
      amplitude *= gain;
      freq *= lacunarity;
    }
    return value / maxVal;
  }

  public static float Ridge(float x, float z, int seed, int octaves = 3)
  {
    float value = 0f, amplitude = 0.5f, maxVal = 0f, freq = 1f;
    for (int i = 0; i < octaves; i++)
    {
      float n = Value(x * freq, z * freq, seed + i * 997);
      n = 1f - MathF.Abs(n * 2f - 1f);
      n *= n;
      value += n * amplitude;
      maxVal += amplitude;
      amplitude *= 0.5f;
      freq *= 2f;
    }
    return value / maxVal;
  }

  private static float Hash(int x, int z, int seed)
  {
    uint h = (uint)(x * 1619 + z * 31337 + seed * 6791);
    h ^= h >> 16;
    h *= 0x45d9f3b;
    h ^= h >> 16;
    return (h & 0x7FFFFF) / (float)0x800000;
  }
}
