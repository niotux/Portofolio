using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Holodeck.Web.Settings;

// Reads a library's settings schema in Holodeck's limited JSON Schema profile (see agents/README.md). Any keyword
// outside the profile is an error, so a schema is never enforced only partly.
public static class SettingsSchemaReader
{
  private const string UiKeyword = "x-ui";
  private static readonly string[] CommonKeywords = ["type", "title", "description", "$comment", UiKeyword];
  private static readonly string[] ObjectKeywords = ["properties", "required", "additionalProperties"];
  private static readonly string[] StringKeywords = ["minLength", "maxLength", "enum", "default"];
  private static readonly string[] NumberKeywords = ["minimum", "maximum", "default"];
  private static readonly string[] BooleanKeywords = ["default"];
  private static readonly string[] RootKeywords = ["$schema"];
  private static readonly string[] Types = [SettingsTypes.Object, SettingsTypes.String, SettingsTypes.Boolean, SettingsTypes.Number, SettingsTypes.Integer];

  public static SettingsSchema Read(string path)
  {
    if (new FileInfo(path).Length > SettingsLimits.MaxSchemaBytes)
    {
      throw new ArgumentException($"A settings schema must be under {SettingsLimits.MaxSchemaBytes / 1024} KB.");
    }

    byte[] bytes = File.ReadAllBytes(path);
    return Parse(bytes);
  }

  public static SettingsSchema Parse(byte[] bytes)
  {
    // Each field level nests a schema object and its properties object, plus room for x-ui and enum arrays. Repeated
    // property names are refused while parsing.
    JsonDocumentOptions options = new() { MaxDepth = SettingsLimits.MaxDepth * 2 + 4, AllowDuplicateProperties = false };
    using JsonDocument document = JsonDocument.Parse(bytes, options);
    int fields = 0;
    SettingsSchemaNode root = ReadNode(document.RootElement, "", "", 0, ref fields);
    if (root.Type != SettingsTypes.Object)
    {
      throw new ArgumentException("A settings schema must describe an object.");
    }

    return new SettingsSchema(root, Convert.ToHexStringLower(SHA256.HashData(bytes)));
  }

  private static SettingsSchemaNode ReadNode(JsonElement element, string name, string path, int depth, ref int fields)
  {
    if (element.ValueKind != JsonValueKind.Object)
    {
      throw Invalid(path, "must be a schema object");
    }

    fields++;
    if (depth > SettingsLimits.MaxDepth || fields > SettingsLimits.MaxFields)
    {
      throw new ArgumentException($"A settings schema may nest {SettingsLimits.MaxDepth} levels and declare {SettingsLimits.MaxFields} fields.");
    }

    string type = ReadType(element, path);
    RequireKnownKeywords(element, type, depth == 0, path);
    if (type == SettingsTypes.Object)
    {
      return ReadObject(element, name, path, depth, ref fields);
    }

    SettingsUiHints hints = ReadHints(element, type, depth == 0, path);
    SettingsSchemaNode node = new()
    {
      Name = name,
      Type = type,
      Title = ReadText(element, "title", path),
      Description = ReadText(element, "description", path),
      Default = ReadDefault(element),
      Control = hints.Control,
      Group = hints.Group,
      Enum = ReadEnum(element, path),
      MinLength = ReadLength(element, "minLength", path),
      MaxLength = ReadLength(element, "maxLength", path),
      Minimum = ReadNumber(element, "minimum", path),
      Maximum = ReadNumber(element, "maximum", path)
    };
    CheckRanges(node, path);
    CheckDefault(node, path);
    return node;
  }

  private static SettingsSchemaNode ReadObject(JsonElement element, string name, string path, int depth, ref int fields)
  {
    if (!element.TryGetProperty("additionalProperties", out JsonElement additional) || !IsBoolean(additional))
    {
      throw Invalid(path, "must declare additionalProperties as true or false");
    }

    List<SettingsSchemaNode> properties = new();
    if (element.TryGetProperty("properties", out JsonElement declared))
    {
      if (declared.ValueKind != JsonValueKind.Object)
      {
        throw Invalid(path, "properties must be an object");
      }

      foreach (JsonProperty property in declared.EnumerateObject())
      {
        RequireName(property.Name, path);
        properties.Add(ReadNode(property.Value, property.Name, SettingsPointer.Child(path, property.Name), depth + 1, ref fields));
      }
    }

    SettingsUiHints hints = ReadHints(element, SettingsTypes.Object, depth == 0, path);
    return new SettingsSchemaNode
    {
      Name = name,
      Type = SettingsTypes.Object,
      Title = ReadText(element, "title", path),
      Description = ReadText(element, "description", path),
      Group = hints.Group,
      Properties = Order(properties, hints.Order, path),
      Required = ReadRequired(element, properties, path),
      AdditionalProperties = additional.GetBoolean()
    };
  }

  private static string ReadType(JsonElement element, string path)
  {
    if (!element.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String)
    {
      throw Invalid(path, "needs a single type: object, string, boolean, number, or integer");
    }

    string value = type.GetString()!;
    if (Array.IndexOf(Types, value) < 0)
    {
      throw Invalid(path, $"uses type '{value}', which this profile does not support");
    }

    return value;
  }

  private static void RequireKnownKeywords(JsonElement element, string type, bool isRoot, string path)
  {
    foreach (JsonProperty property in element.EnumerateObject())
    {
      bool isKnown = Array.IndexOf(CommonKeywords, property.Name) >= 0 || Array.IndexOf(KeywordsFor(type), property.Name) >= 0;
      bool isRootKeyword = isRoot && Array.IndexOf(RootKeywords, property.Name) >= 0;
      if (!isKnown && !isRootKeyword)
      {
        throw Invalid(path, $"uses '{property.Name}', which this profile does not support for {type} fields");
      }
    }
  }

  private static string[] KeywordsFor(string type)
  {
    if (type == SettingsTypes.Object)
    {
      return ObjectKeywords;
    }

    if (type == SettingsTypes.String)
    {
      return StringKeywords;
    }

    if (type == SettingsTypes.Boolean)
    {
      return BooleanKeywords;
    }

    return NumberKeywords;
  }

  private static SettingsUiHints ReadHints(JsonElement element, string type, bool isRoot, string path)
  {
    if (!element.TryGetProperty(UiKeyword, out JsonElement hints))
    {
      return new SettingsUiHints(null, null, null);
    }

    if (hints.ValueKind != JsonValueKind.Object)
    {
      throw Invalid(path, "x-ui must be an object");
    }

    foreach (JsonProperty hint in hints.EnumerateObject())
    {
      bool isAllowed = (hint.Name == "order" && type == SettingsTypes.Object) || (hint.Name == "control" && type == SettingsTypes.String) || (hint.Name == "group" && !isRoot);
      if (!isAllowed)
      {
        throw Invalid(path, $"uses x-ui '{hint.Name}', which is not available here");
      }
    }

    string? control = ReadText(hints, "control", path);
    bool isKnownControl = control is null || control == SettingsControls.Multiline || control == SettingsControls.Password;
    if (!isKnownControl)
    {
      throw Invalid(path, $"uses x-ui control '{control}'; use multiline or password");
    }

    return new SettingsUiHints(control, ReadText(hints, "group", path), ReadNames(hints, "order", path));
  }

  private static List<SettingsSchemaNode> Order(List<SettingsSchemaNode> properties, List<string>? order, string path)
  {
    if (order is null)
    {
      return properties;
    }

    List<SettingsSchemaNode> ordered = new();
    foreach (string name in order)
    {
      SettingsSchemaNode? property = FindByName(properties, name);
      if (property is null || ordered.Contains(property))
      {
        throw Invalid(path, $"x-ui order names '{name}', which is not a property or is listed twice");
      }

      ordered.Add(property);
    }

    foreach (SettingsSchemaNode property in properties)
    {
      if (!ordered.Contains(property))
      {
        ordered.Add(property);
      }
    }

    return ordered;
  }

  private static List<string>? ReadRequired(JsonElement element, List<SettingsSchemaNode> properties, string path)
  {
    List<string>? required = ReadNames(element, "required", path);
    if (required is null)
    {
      return null;
    }

    for (int index = 0; index < required.Count; index++)
    {
      bool isRepeated = required.IndexOf(required[index]) != index;
      if (FindByName(properties, required[index]) is null || isRepeated)
      {
        throw Invalid(path, $"requires '{required[index]}', which is not a declared property or is listed twice");
      }
    }

    return required;
  }

  private static List<string>? ReadEnum(JsonElement element, string path)
  {
    List<string>? values = ReadNames(element, "enum", path);
    if (values is null)
    {
      return null;
    }

    if (values.Count == 0 || values.Count > SettingsLimits.MaxEnumValues)
    {
      throw Invalid(path, $"enum must list 1 to {SettingsLimits.MaxEnumValues} values");
    }

    for (int index = 0; index < values.Count; index++)
    {
      if (values.IndexOf(values[index]) != index)
      {
        throw Invalid(path, $"enum lists '{values[index]}' twice");
      }
    }

    return values;
  }

  private static List<string>? ReadNames(JsonElement element, string keyword, string path)
  {
    if (!element.TryGetProperty(keyword, out JsonElement list))
    {
      return null;
    }

    if (list.ValueKind != JsonValueKind.Array)
    {
      throw Invalid(path, $"{keyword} must be a list of strings");
    }

    List<string> names = new();
    foreach (JsonElement item in list.EnumerateArray())
    {
      if (item.ValueKind != JsonValueKind.String)
      {
        throw Invalid(path, $"{keyword} must be a list of strings");
      }

      string name = item.GetString()!;
      RequireName(name, path);
      names.Add(name);
    }

    return names;
  }

  private static string? ReadText(JsonElement element, string keyword, string path)
  {
    if (!element.TryGetProperty(keyword, out JsonElement text))
    {
      return null;
    }

    if (text.ValueKind != JsonValueKind.String || text.GetString()!.Length > SettingsLimits.MaxTextLength)
    {
      throw Invalid(path, $"{keyword} must be text of at most {SettingsLimits.MaxTextLength} characters");
    }

    return text.GetString();
  }

  private static int? ReadLength(JsonElement element, string keyword, string path)
  {
    if (!element.TryGetProperty(keyword, out JsonElement length))
    {
      return null;
    }

    bool isWhole = length.ValueKind == JsonValueKind.Number && length.TryGetInt32(out int value) && value >= 0;
    if (!isWhole)
    {
      throw Invalid(path, $"{keyword} must be a whole number of 0 or more");
    }

    return length.GetInt32();
  }

  private static double? ReadNumber(JsonElement element, string keyword, string path)
  {
    if (!element.TryGetProperty(keyword, out JsonElement number))
    {
      return null;
    }

    if (number.ValueKind != JsonValueKind.Number || !double.IsFinite(number.GetDouble()))
    {
      throw Invalid(path, $"{keyword} must be a number");
    }

    return number.GetDouble();
  }

  private static JsonElement? ReadDefault(JsonElement element)
  {
    if (!element.TryGetProperty("default", out JsonElement value))
    {
      return null;
    }

    return value.Clone();
  }

  private static void CheckRanges(SettingsSchemaNode node, string path)
  {
    bool isLengthInverted = node.MinLength is not null && node.MaxLength is not null && node.MinLength.Value > node.MaxLength.Value;
    bool isRangeInverted = node.Minimum is not null && node.Maximum is not null && node.Minimum.Value > node.Maximum.Value;
    if (isLengthInverted || isRangeInverted)
    {
      throw Invalid(path, "has a lower bound above its upper bound");
    }
  }

  private static void CheckDefault(SettingsSchemaNode node, string path)
  {
    if (node.Default is null)
    {
      return;
    }

    JsonNode? value = JsonNode.Parse(node.Default.Value.GetRawText());
    if (SettingsValidator.Validate(node, value).Count > 0)
    {
      throw Invalid(path, "has a default that its own constraints refuse");
    }
  }

  private static void RequireName(string name, string path)
  {
    if (name.Length == 0 || name.Length > SettingsLimits.MaxNameLength)
    {
      throw Invalid(path, $"uses a name that is empty or longer than {SettingsLimits.MaxNameLength} characters");
    }
  }

  private static SettingsSchemaNode? FindByName(List<SettingsSchemaNode> properties, string name)
  {
    foreach (SettingsSchemaNode property in properties)
    {
      if (property.Name == name)
      {
        return property;
      }
    }

    return null;
  }

  private static bool IsBoolean(JsonElement element)
  {
    return element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False;
  }

  private static ArgumentException Invalid(string path, string problem)
  {
    string field = path.Length == 0 ? "The settings schema root" : $"Settings field '{path}'";
    return new ArgumentException($"{field} {problem}.");
  }
}
