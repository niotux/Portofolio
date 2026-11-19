using System.Text.Json;
using System.Text.Json.Nodes;

namespace Holodeck.Web.Settings;

// Checks a settings document against a schema in the supported profile. Values the schema does not describe are
// accepted where the enclosing object allows additional properties and are never changed here.
public static class SettingsValidator
{
  public static List<SettingsFieldError> Validate(SettingsSchemaNode root, JsonNode? value)
  {
    List<SettingsFieldError> errors = new();
    Check(root, value, "", errors);
    return errors;
  }

  private static void Check(SettingsSchemaNode node, JsonNode? value, string path, List<SettingsFieldError> errors)
  {
    if (node.Type == SettingsTypes.Object)
    {
      CheckObject(node, value, path, errors);
    }
    else if (node.Type == SettingsTypes.String)
    {
      CheckString(node, value, path, errors);
    }
    else if (node.Type == SettingsTypes.Boolean)
    {
      if (KindOf(value) != JsonValueKind.True && KindOf(value) != JsonValueKind.False)
      {
        Add(errors, path, "Expected true or false.");
      }
    }
    else
    {
      CheckNumber(node, value, path, errors);
    }
  }

  private static void CheckObject(SettingsSchemaNode node, JsonNode? value, string path, List<SettingsFieldError> errors)
  {
    if (value is not JsonObject document)
    {
      Add(errors, path, "Expected an object.");
      return;
    }

    if (node.Required is not null)
    {
      foreach (string name in node.Required)
      {
        if (!document.ContainsKey(name))
        {
          Add(errors, SettingsPointer.Child(path, name), "This value is required.");
        }
      }
    }

    foreach (KeyValuePair<string, JsonNode?> entry in document)
    {
      SettingsSchemaNode? property = FindProperty(node, entry.Key);
      string child = SettingsPointer.Child(path, entry.Key);
      if (property is not null)
      {
        Check(property, entry.Value, child, errors);
      }
      else if (node.AdditionalProperties == false)
      {
        Add(errors, child, "This definition does not allow this value.");
      }
    }
  }

  private static void CheckString(SettingsSchemaNode node, JsonNode? value, string path, List<SettingsFieldError> errors)
  {
    if (KindOf(value) != JsonValueKind.String)
    {
      Add(errors, path, "Expected text.");
      return;
    }

    string text = value!.GetValue<string>();
    if (node.Enum is not null && !node.Enum.Contains(text))
    {
      Add(errors, path, "Choose one of the listed values.");
      return;
    }

    // JSON Schema counts characters as code points, so an emoji is one character.
    int length = 0;
    foreach (System.Text.Rune _ in text.EnumerateRunes())
    {
      length++;
    }

    if (node.MinLength is not null && length < node.MinLength.Value)
    {
      Add(errors, path, $"Use at least {node.MinLength.Value} characters.");
    }

    if (node.MaxLength is not null && length > node.MaxLength.Value)
    {
      Add(errors, path, $"Use at most {node.MaxLength.Value} characters.");
    }
  }

  private static void CheckNumber(SettingsSchemaNode node, JsonNode? value, string path, List<SettingsFieldError> errors)
  {
    if (KindOf(value) != JsonValueKind.Number)
    {
      Add(errors, path, node.Type == SettingsTypes.Integer ? "Expected a whole number." : "Expected a number.");
      return;
    }

    JsonElement element = value!.GetValue<JsonElement>();
    if (node.Type == SettingsTypes.Integer && !IsWhole(element))
    {
      Add(errors, path, "Expected a whole number.");
      return;
    }

    double number = element.GetDouble();
    if (node.Minimum is not null && number < node.Minimum.Value)
    {
      Add(errors, path, $"Use {node.Minimum.Value} or more.");
    }

    if (node.Maximum is not null && number > node.Maximum.Value)
    {
      Add(errors, path, $"Use {node.Maximum.Value} or less.");
    }
  }

  // 2 and 2.0 are both integers in JSON Schema.
  private static bool IsWhole(JsonElement element)
  {
    if (element.TryGetInt64(out long _))
    {
      return true;
    }

    if (element.TryGetDecimal(out decimal exact))
    {
      return decimal.Truncate(exact) == exact;
    }

    double approximate = element.GetDouble();
    return double.IsFinite(approximate) && Math.Floor(approximate) == approximate;
  }

  private static JsonValueKind KindOf(JsonNode? value)
  {
    if (value is null)
    {
      return JsonValueKind.Null;
    }

    return value.GetValueKind();
  }

  private static SettingsSchemaNode? FindProperty(SettingsSchemaNode node, string name)
  {
    if (node.Properties is null)
    {
      return null;
    }

    foreach (SettingsSchemaNode property in node.Properties)
    {
      if (property.Name == name)
      {
        return property;
      }
    }

    return null;
  }

  private static void Add(List<SettingsFieldError> errors, string path, string message)
  {
    if (errors.Count < SettingsLimits.MaxFieldErrors)
    {
      errors.Add(new SettingsFieldError(path, message));
    }
  }
}
