using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Holodeck.Web.Rooms;
using Holodeck.Web.Settings;

namespace Holodeck.Web.Management;

public static class ManagementDefinitions
{
  public static ManagementDefinition Read(string library, string path)
  {
    if (new FileInfo(path).Length > ManagementLimits.MaxDeclarationBytes)
    {
      throw new ArgumentException("The actions declaration is too large.");
    }

    byte[] bytes = File.ReadAllBytes(path);
    using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowDuplicateProperties = false });
    ManagementManifest manifest = document.RootElement.Deserialize<ManagementManifest>() ?? throw new ArgumentException("Actions must contain an object.");
    if (manifest.FormatVersion != ManagementLimits.FormatVersion || manifest.Actions is null)
    {
      throw new ArgumentException("Actions require FormatVersion 1 and an Actions list.");
    }

    if (manifest.Actions.Count > ManagementLimits.MaxActions)
    {
      throw new ArgumentException($"Declare at most {ManagementLimits.MaxActions} actions.");
    }

    List<ManagementAction> actions = new();
    HashSet<string> ids = new(StringComparer.Ordinal);
    using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    hash.AppendData(SHA256.HashData(bytes));
    foreach (ManagementActionManifest declared in manifest.Actions)
    {
      if (declared is null)
      {
        throw new ArgumentException("Actions must be objects.");
      }

      string id = LibraryFiles.RequireId(declared.Id, "Action Id");
      RequireText(id, ManagementLimits.MaxIdentifierLength, "Action Id");
      if (!ids.Add(id))
      {
        throw new ArgumentException($"Duplicate action Id '{id}'.");
      }

      RequireText(declared.Title, ManagementLimits.MaxTitleLength, "Action Title");
      RequireText(declared.Description, ManagementLimits.MaxDescriptionLength, "Action Description");
      SettingsSchema input = ReadSchema(library, path, declared.InputSchema);
      SettingsSchema result = ReadSchema(library, path, declared.ResultSchema);
      hash.AppendData(Encoding.ASCII.GetBytes(input.Revision));
      hash.AppendData(Encoding.ASCII.GetBytes(result.Revision));
      List<string> followUps = declared.FollowUpActions ?? new List<string>();
      HashSet<string> seen = new(StringComparer.Ordinal);
      foreach (string followUp in followUps)
      {
        LibraryFiles.RequireId(followUp, "FollowUpActions entry");
        if (!seen.Add(followUp))
        {
          throw new ArgumentException("FollowUpActions cannot repeat an action.");
        }
      }

      actions.Add(new ManagementAction(id, declared.Title!, declared.Description!, input.Root, result.Root, followUps));
    }

    foreach (ManagementAction action in actions)
    {
      foreach (string followUp in action.FollowUpActions)
      {
        if (!ids.Contains(followUp))
        {
          throw new ArgumentException($"Unknown follow-up action '{followUp}'.");
        }
      }
    }

    return new ManagementDefinition(Convert.ToHexStringLower(hash.GetHashAndReset()), actions);
  }

  private static SettingsSchema ReadSchema(string library, string actionsPath, string? declared)
  {
    if (string.IsNullOrWhiteSpace(declared) || Path.IsPathRooted(declared))
    {
      throw new ArgumentException("An action must declare relative input and result schema paths.");
    }

    string relative = Path.GetRelativePath(library, Path.GetDirectoryName(actionsPath)!);
    return SettingsSchemaReader.Read(LibraryFiles.Inside(library, relative + "/" + declared));
  }

  private static void RequireText(string? value, int maximum, string label)
  {
    if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
    {
      throw new ArgumentException($"{label} must contain 1 to {maximum} characters.");
    }
  }
}
