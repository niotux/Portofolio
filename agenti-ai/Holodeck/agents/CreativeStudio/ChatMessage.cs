using System.Text.Json;
using System.Text.Json.Serialization;

namespace CreativeStudio;

// A content string for ordinary prose; an array of named parts only when a selected image is attached.
public sealed class ChatMessage
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public ChatMessage(string role, string text)
  {
    Role = role;
    Content = JsonSerializer.SerializeToElement(text);
    TextContent = text;
  }

  private ChatMessage(string role, string text, List<ContentPart> content, int images)
  {
    Role = role;
    Content = JsonSerializer.SerializeToElement(content, Json);
    TextContent = text;
    ImageCount = images;
  }

  public string Role { get; }
  public JsonElement Content { get; }
  [JsonIgnore] public string TextContent { get; }
  [JsonIgnore] public int ImageCount { get; }

  public static ChatMessage Text(string role, string text) => new(role, text);

  public static ChatMessage ImageUser(string text, string path, long maxBytes)
  {
    FileInfo file = new(path);
    if (!file.Exists)
    {
      throw new IOException("The selected image no longer exists.");
    }
    if (file.Length > maxBytes)
    {
      throw new IOException("The selected image exceeds the configured vision allowance.");
    }

    string mediaType = Path.GetExtension(path).ToLowerInvariant() switch
    {
      ".png" => "image/png",
      ".jpg" or ".jpeg" => "image/jpeg",
      ".webp" => "image/webp",
      ".gif" => "image/gif",
      _ => throw new IOException("Select a PNG, JPEG, WebP or GIF image for visual inspection.")
    };
    string encoded = Convert.ToBase64String(File.ReadAllBytes(path));
    List<ContentPart> parts = [new ContentPart("text", text, null), new ContentPart("image_url", null, new ContentImage("data:" + mediaType + ";base64," + encoded))];
    return new ChatMessage("user", text, parts, 1);
  }
}
