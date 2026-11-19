using System.Text;

namespace CreativeStudio;

public sealed class StudioTurn(StoredReceipt receipt)
{
  private readonly Dictionary<string, string> references = new(StringComparer.Ordinal);
  private readonly Dictionary<string, string> entries = new(StringComparer.Ordinal);

  public StoredReceipt Receipt { get; } = receipt;
  public List<ChatMessage> Experiences { get; } = new();
  public List<string> InspectedFiles { get; } = new();
  public GenerationPlan? Generation { get; set; }

  public string AddReference(string path)
  {
    foreach (KeyValuePair<string, string> reference in references)
    {
      if (reference.Value == path)
      {
        return reference.Key;
      }
    }

    string handle = "reference" + (references.Count + 1);
    references.Add(handle, path);
    return handle;
  }

  public string Reference(string handle)
  {
    return references.TryGetValue(handle, out string? path) ? path : throw new ArgumentException("Use a reference handle already available in this exchange.");
  }

  public string AddEntry(string id)
  {
    foreach (KeyValuePair<string, string> entry in entries)
    {
      if (entry.Value == id)
      {
        return entry.Key;
      }
    }

    string handle = "entry" + (entries.Count + 1);
    entries.Add(handle, id);
    return handle;
  }

  public string Entry(string handle)
  {
    return entries.TryGetValue(handle, out string? id) ? id : throw new ArgumentException("Search the notebook first and use one of its entry handles.");
  }

  public string CurrentMessage(List<StoredReference> recent)
  {
    StringBuilder text = new();
    text.Append(Receipt.From).Append(": ").Append(Receipt.Note);
    foreach (string file in Receipt.Files)
    {
      text.Append("\nAttached file ").Append(AddReference(file)).Append(": ").Append(Path.GetFileName(file));
    }

    foreach (StoredReference file in recent)
    {
      if (!Receipt.Files.Contains(file.Path))
      {
        text.Append("\nAvailable earlier file ").Append(AddReference(file.Path)).Append(": ").Append(Path.GetFileName(file.Path)).Append("\n").Append(file.Description);
      }
    }

    return text.ToString();
  }
}
