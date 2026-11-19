using System.Text.Json;
using System.Text.Json.Nodes;

namespace Holodeck.Web.Settings;

// Applies form field changes to a stored settings document. Only the named fields change; values the form does not
// show, and the order of everything else, stay exactly as stored.
internal static class SettingsChanges
{
  public static SettingsEdit Apply(SettingsSchemaNode root, JsonObject document, List<SettingsChange> changes)
  {
    List<SettingsFieldError> errors = new();
    if (changes.Count > SettingsLimits.MaxFields)
    {
      errors.Add(new SettingsFieldError("", $"Send at most {SettingsLimits.MaxFields} changes at once."));
      return new SettingsEdit(null, errors);
    }

    foreach (SettingsChange change in changes)
    {
      Apply(root, document, change, errors);
    }

    if (errors.Count > 0)
    {
      return new SettingsEdit(null, errors);
    }

    return new SettingsEdit(SettingsDocument.Write(document), errors);
  }

  private static void Apply(SettingsSchemaNode root, JsonObject document, SettingsChange change, List<SettingsFieldError> errors)
  {
    string path = change.Path ?? "";
    List<string> names = SettingsPointer.Parse(path);
    if (FindField(root, names) is null)
    {
      errors.Add(new SettingsFieldError(path, "This is not a field of the settings form."));
      return;
    }

    bool hasValue = change.Value is not null && change.Value.Value.ValueKind != JsonValueKind.Undefined;
    if (change.Remove == hasValue)
    {
      errors.Add(new SettingsFieldError(path, "Send either a value or a removal."));
      return;
    }

    JsonObject? parent = FindParent(document, names, path, !change.Remove, errors);
    string name = names[names.Count - 1];
    if (parent is null)
    {
      return;
    }

    if (change.Remove)
    {
      parent.Remove(name);
      return;
    }

    parent[name] = SettingsDocument.Parse(change.Value!.Value.GetRawText());
  }

  // Missing groups are created only when a value is set inside them.
  private static JsonObject? FindParent(JsonObject document, List<string> names, string path, bool create, List<SettingsFieldError> errors)
  {
    JsonObject current = document;
    string location = "";
    for (int index = 0; index < names.Count - 1; index++)
    {
      location = SettingsPointer.Child(location, names[index]);
      JsonNode? next = current[names[index]];
      if (next is null && !current.ContainsKey(names[index]))
      {
        if (!create)
        {
          return null;
        }

        next = new JsonObject();
        current[names[index]] = next;
      }

      if (next is not JsonObject group)
      {
        errors.Add(new SettingsFieldError(location, "The stored value here is not a group of settings. Repair it in the settings text first."));
        return null;
      }

      current = group;
    }

    return current;
  }

  // Changes name leaf fields only, so a change cannot replace a whole group.
  private static SettingsSchemaNode? FindField(SettingsSchemaNode root, List<string> names)
  {
    SettingsSchemaNode current = root;
    foreach (string name in names)
    {
      SettingsSchemaNode? next = null;
      if (current.Properties is not null)
      {
        foreach (SettingsSchemaNode property in current.Properties)
        {
          if (property.Name == name)
          {
            next = property;
            break;
          }
        }
      }

      if (next is null)
      {
        return null;
      }

      current = next;
    }

    if (current.Type == SettingsTypes.Object)
    {
      return null;
    }

    return current;
  }
}
