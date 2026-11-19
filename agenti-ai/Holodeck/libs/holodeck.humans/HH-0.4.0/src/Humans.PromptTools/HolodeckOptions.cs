namespace Humans.PromptTools;

// Where Holodeck's user API is and which installed package's shared database holds the prompts.
public sealed class HolodeckOptions
{
  public string Url { get; set; } = "";
  public string Library { get; set; } = "";
  public string LibraryVersion { get; set; } = "";

  public static bool IsValid(HolodeckOptions options)
  {
    bool http = Uri.TryCreate(options.Url, UriKind.Absolute, out Uri? url) && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);
    return http && options.Library.Length > 0 && options.LibraryVersion.Length > 0;
  }
}
