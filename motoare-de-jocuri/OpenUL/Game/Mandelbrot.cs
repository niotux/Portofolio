public static class MandelbrotGenerator
{
  /// <summary>
  /// Generates a Mandelbrot set as RGBA8 byte array (4 bytes per pixel: R,G,B,A).
  /// </summary>
  /// <param name="width">Image width in pixels</param>
  /// <param name="height">Image height in pixels</param>
  /// <param name="maxIterations">Detail level - higher = more detail/slower (try 100-2000)</param>
  /// <param name="centerX">Real-axis center of the view (default -0.5 for classic full view)</param>
  /// <param name="centerY">Imaginary-axis center of the view (default 0)</param>
  /// <param name="zoom">Zoom level - higher = more zoomed in (default 1.0 shows the whole set)</param>
  public static byte[] Generate(
    int width,
    int height,
    int maxIterations = 256,
    double centerX = -0.5,
    double centerY = 0.0,
    double zoom = 1.0
  )
  {
    byte[] pixels = new byte[width * height * 4];

    // Base view spans roughly [-2.5, 1.5] on real axis; scale by zoom & aspect ratio
    double baseRange = 3.0 / zoom;
    double aspect = (double)width / height;

    double minReal = centerX - baseRange * aspect / 2.0;
    double maxReal = centerX + baseRange * aspect / 2.0;
    double minImag = centerY - baseRange / 2.0;
    double maxImag = centerY + baseRange / 2.0;

    Parallel.For(0, height, py =>
    {
      double imag = minImag + (maxImag - minImag) * py / (height - 1);

      for (int px = 0; px < width; px++)
      {
        double real = minReal + (maxReal - minReal) * px / (width - 1);

        int iter = ComputeMandelbrot(real, imag, maxIterations);

        (byte r, byte g, byte b) = IterationToColor(iter, maxIterations);

        int idx = (py * width + px) * 4;
        pixels[idx + 0] = r;
        pixels[idx + 1] = g;
        pixels[idx + 2] = b;
        pixels[idx + 3] = 255; // fully opaque
      }
    });

    return pixels;
  }

  private static int ComputeMandelbrot(double cReal, double cImag, int maxIterations)
  {
    double zReal = 0.0, zImag = 0.0;
    int iter = 0;

    // Optional cardioid/bulb check for speed (skip obvious interior points)
    double q = (cReal - 0.25) * (cReal - 0.25) + cImag * cImag;
    if (q * (q + (cReal - 0.25)) < 0.25 * cImag * cImag)
      return maxIterations;
    if ((cReal + 1) * (cReal + 1) + cImag * cImag < 0.0625)
      return maxIterations;

    while (zReal * zReal + zImag * zImag <= 4.0 && iter < maxIterations)
    {
      double zRealTemp = zReal * zReal - zImag * zImag + cReal;
      zImag = 2.0 * zReal * zImag + cImag;
      zReal = zRealTemp;
      iter++;
    }

    return iter;
  }

  private static (byte, byte, byte) IterationToColor(int iter, int maxIterations)
  {
    if (iter >= maxIterations)
      return (0, 0, 0); // inside the set = black

    // Smooth-ish coloring using HSV-like cycling
    double t = (double)iter / maxIterations;

    // Simple palette: cycles through colors based on iteration count
    double hue = 360.0 * t * 5.0 % 360.0;
    return HsvToRgb(hue, 0.8, 1.0);
  }

  private static (byte, byte, byte) HsvToRgb(double h, double s, double v)
  {
    double c = v * s;
    double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
    double m = v - c;

    double r, g, b;
    if (h < 60) (r, g, b) = (c, x, 0);
    else if (h < 120) (r, g, b) = (x, c, 0);
    else if (h < 180) (r, g, b) = (0, c, x);
    else if (h < 240) (r, g, b) = (0, x, c);
    else if (h < 300) (r, g, b) = (x, 0, c);
    else (r, g, b) = (c, 0, x);

    return ((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
  }
}