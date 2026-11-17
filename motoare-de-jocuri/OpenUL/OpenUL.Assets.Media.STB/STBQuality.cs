namespace OpenUL.Assets.Media.STB;

public readonly record struct STBQuality
{
  public int Percentage { get; }
  private STBQuality(int percentage)
  {
    if (percentage > 100)
    {
      percentage = 100;
    }
    Percentage = percentage;
  }

  private STBQuality(float fraction)
  {
    if (fraction > 1f)
    {
      fraction = 1f;
    }
    Percentage = (int)(fraction * 100f);
  }

  private STBQuality(double fraction)
  {
    if (fraction > 1f)
    {
      fraction = 1f;
    }
    Percentage = (int)(fraction * 100d);
  }

  public static STBQuality FromPercentage(int percentage) => new(percentage);
  public static STBQuality FromFraction(float fraction) => new(fraction);
  public static STBQuality FromFraction(double fraction) => new(fraction);


  public float ToSingle()
    => (float)Percentage / 100f;

  public double ToDouble()
    => (double)Percentage / 100d;
}