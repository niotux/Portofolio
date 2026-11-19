using System.Text.Json;

namespace Humans;

// Filenames are lookup keys for authored assets, never a model-authored mental schema.
public sealed record PromptBundle(Dictionary<string, string> Files)
{
  // Bounds one version's whole prompt set.
  public const int MaxBytes = 64 * 1024;
  private static readonly string[] RequiredNames = ["page.json", "continue.txt", "recognise.txt", "look.txt", "search.txt", "remember.txt", "form.txt", "compact.txt"];

  public string Get(string name)
  {
    if (!Files.TryGetValue(name, out string? content))
    {
      throw new IOException("The selected prompt set has no " + name + ".");
    }
    return content;
  }

  public PromptBundle Copy()
  {
    return new PromptBundle(new Dictionary<string, string>(Files, StringComparer.Ordinal));
  }

  public void Validate()
  {
    if (Files is null)
    {
      throw new ArgumentException("A prompt bundle needs named files.");
    }
    foreach (string name in RequiredNames)
    {
      if (!Files.ContainsKey(name))
      {
        throw new ArgumentException("A prompt bundle is missing " + name + ".");
      }
    }
    if (JsonSerializer.SerializeToUtf8Bytes(this).Length > MaxBytes)
    {
      throw new ArgumentException("A prompt bundle is too large.");
    }
    foreach (KeyValuePair<string, string> file in Files)
    {
      ValidateFile(file.Key, file.Value);
    }
  }

  public static void ValidateFile(string name, string content)
  {
    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(content))
    {
      throw new ArgumentException("Choose an existing prompt filename and nonempty content.");
    }
    bool filename = name == Path.GetFileName(name) && !name.Contains('\\') && !name.Contains('/');
    bool supported = name == "page.json" || name.EndsWith(".txt", StringComparison.Ordinal);
    if (!filename || !supported)
    {
      throw new ArgumentException("Choose an existing prompt filename and nonempty content.");
    }
    if (JsonSerializer.SerializeToUtf8Bytes(content).Length > MaxBytes)
    {
      throw new ArgumentException("This prompt is too large.");
    }
    if (name != "page.json")
    {
      return;
    }
    PageText page;
    try
    {
      page = JsonSerializer.Deserialize<PageText>(content) ?? throw new ArgumentException("Page labels must contain an object.");
    }
    catch (JsonException exception)
    {
      throw new ArgumentException("Page labels must be valid JSON.", exception);
    }
    string[] labels = [page.Header, page.Self, page.Personality, page.Other, page.Summary, page.Recognise, page.Remember, page.Thought, page.ThoughtAfterRecall, page.Close];
    foreach (string label in labels)
    {
      if (string.IsNullOrWhiteSpace(label))
      {
        throw new ArgumentException("Every page label must contain text.");
      }
    }
  }
}
