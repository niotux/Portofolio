namespace Holodeck.Mcp;

public sealed class HolodeckApiOptions
{
  public string Url { get; set; } = "";

  public static bool IsValid(HolodeckApiOptions options)
  {
    if (!Uri.TryCreate(options.Url, UriKind.Absolute, out Uri? uri))
    {
      return false;
    }

    return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
  }
}
