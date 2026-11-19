using System.Text.Json;

namespace Humans;

// Filenames are lookup keys for authored assets, never a model-authored mental schema. The catalog checks only what
// holds for every HH version: named, nonempty, bounded text, and JSON files that are objects of nonempty text. Which
// files a version needs is that version's business; an edit can never drop a label a JSON file already has.
public sealed record PromptBundle(Dictionary<string, string> Files)
{
  // Bounds one version's whole prompt set.
  public const int MaxBytes = 96 * 1024;

  public string Get(string name)
  {
    if (!Files.TryGetValue(name, out string? content))
    {
      throw new IOException("The selected prompt set has no " + name + ".");
    }
    return content;
  }

  public bool Has(string name)
  {
    return Files.ContainsKey(name);
  }

  public PromptBundle Copy()
  {
    return new PromptBundle(new Dictionary<string, string>(Files, StringComparer.Ordinal));
  }

  public void Validate()
  {
    if (Files is null || Files.Count == 0)
    {
      throw new ArgumentException("A prompt bundle needs named files.");
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

  // A version's runtime names the prompts it cannot run without.
  public void Require(List<string> names)
  {
    foreach (string name in names)
    {
      if (!Files.ContainsKey(name))
      {
        throw new ArgumentException("This prompt set is missing " + name + ", which this HH version needs.");
      }
    }
  }

  public static void ValidateFile(string name, string content)
  {
    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(content))
    {
      throw new ArgumentException("Choose a prompt filename and nonempty content.");
    }
    bool filename = name == Path.GetFileName(name) && !name.Contains('\\') && !name.Contains('/');
    bool supported = name.EndsWith(".json", StringComparison.Ordinal) || name.EndsWith(".txt", StringComparison.Ordinal);
    if (!filename || !supported)
    {
      throw new ArgumentException("A prompt is a plain .txt or .json filename.");
    }
    if (JsonSerializer.SerializeToUtf8Bytes(content).Length > MaxBytes)
    {
      throw new ArgumentException("This prompt is too large.");
    }
    if (name.EndsWith(".json", StringComparison.Ordinal))
    {
      Labels(name, content);
    }
  }

  // Replacing a JSON file keeps every label it had, so a running person never loses one it reads.
  public static void ValidateReplacement(string name, string before, string after)
  {
    ValidateFile(name, after);
    if (!name.EndsWith(".json", StringComparison.Ordinal))
    {
      return;
    }
    Dictionary<string, string> kept = Labels(name, after);
    foreach (string label in Labels(name, before).Keys)
    {
      if (!kept.ContainsKey(label))
      {
        throw new ArgumentException(name + " must keep its label " + label + ".");
      }
    }
  }

  public static Dictionary<string, string> Labels(string name, string content)
  {
    Dictionary<string, string>? labels;
    try
    {
      labels = JsonSerializer.Deserialize<Dictionary<string, string>>(content);
    }
    catch (JsonException exception)
    {
      throw new ArgumentException(name + " must be a JSON object of text labels.", exception);
    }
    if (labels is null || labels.Count == 0)
    {
      throw new ArgumentException(name + " must be a JSON object of text labels.");
    }
    foreach (KeyValuePair<string, string> label in labels)
    {
      if (string.IsNullOrWhiteSpace(label.Value))
      {
        throw new ArgumentException("Every label in " + name + " must contain text.");
      }
    }
    return labels;
  }
}
