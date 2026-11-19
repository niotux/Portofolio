using System.Text;
using System.Text.Json.Nodes;
using Holodeck.Web.Settings;
using Xunit;

namespace Holodeck.Tests;

public sealed class SettingsSchemaTests
{
  // A library-defined schema exercising every part of the profile. The field names belong to this fixture only.
  public const string Schema = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "title": "Fixture settings",
      "additionalProperties": true,
      "required": ["personality", "model"],
      "x-ui": { "order": ["model", "personality"] },
      "properties": {
        "personality": { "type": "string", "title": "Personality", "description": "How it talks.", "maxLength": 400, "x-ui": { "control": "multiline" } },
        "model": {
          "type": "object",
          "title": "Model",
          "additionalProperties": false,
          "required": ["name"],
          "properties": {
            "name": { "type": "string", "minLength": 1, "maxLength": 80 },
            "key": { "type": "string", "x-ui": { "control": "password" } }
          }
        },
        "temperature": { "type": "number", "minimum": 0, "maximum": 2, "default": 0.7, "x-ui": { "group": "Generation" } },
        "rounds": { "type": "integer", "minimum": 1, "maximum": 20, "x-ui": { "group": "Generation" } },
        "tone": { "type": "string", "enum": ["brief", "detailed"], "$comment": "Library note." },
        "thinking": { "type": "boolean", "default": false }
      }
    }
    """;

  [Fact]
  public void ReadsTheProfileInDisplayOrder()
  {
    SettingsSchema schema = Parse(Schema);
    SettingsSchemaNode root = schema.Root;
    Assert.Equal(64, schema.Revision.Length);
    Assert.Equal(SettingsTypes.Object, root.Type);
    Assert.True(root.AdditionalProperties);
    Assert.Equal(["personality", "model"], root.Required);
    List<string> order = new();
    foreach (SettingsSchemaNode property in root.Properties!)
    {
      order.Add(property.Name);
    }

    Assert.Equal(["model", "personality", "temperature", "rounds", "tone", "thinking"], order);
    SettingsSchemaNode model = root.Properties![0];
    Assert.False(model.AdditionalProperties);
    Assert.Equal(SettingsControls.Password, model.Properties![1].Control);
    Assert.Equal(SettingsControls.Multiline, root.Properties[1].Control);
    Assert.Equal(400, root.Properties[1].MaxLength);
    Assert.Equal("Generation", root.Properties[2].Group);
    Assert.Equal(2, root.Properties[2].Maximum);
    Assert.Equal(0.7, root.Properties[2].Default!.Value.GetDouble());
    Assert.Equal(["brief", "detailed"], root.Properties[4].Enum);
  }

  [Theory]
  [InlineData("""{ "type": "string", "pattern": "a+" }""", "pattern")]
  [InlineData("""{ "type": "string", "format": "uri" }""", "format")]
  [InlineData("""{ "$ref": "https://example.com/schema.json" }""", "type")]
  [InlineData("""{ "type": "array", "items": { "type": "string" } }""", "array")]
  [InlineData("""{ "type": ["string", "null"] }""", "single type")]
  [InlineData("""{ "type": "string", "oneOf": [] }""", "oneOf")]
  [InlineData("""{ "type": "number", "exclusiveMinimum": 0 }""", "exclusiveMinimum")]
  [InlineData("""{ "type": "number", "multipleOf": 2 }""", "multipleOf")]
  [InlineData("""{ "type": "object", "properties": {} }""", "additionalProperties")]
  [InlineData("""{ "type": "object", "additionalProperties": { "type": "string" } }""", "additionalProperties")]
  [InlineData("""{ "type": "object", "additionalProperties": true, "required": ["missing"] }""", "missing")]
  [InlineData("""{ "type": "string", "x-ui": { "widget": "slider" } }""", "widget")]
  [InlineData("""{ "type": "number", "x-ui": { "control": "multiline" } }""", "control")]
  [InlineData("""{ "type": "string", "x-ui": { "control": "html" } }""", "html")]
  [InlineData("""{ "type": "string", "maxLength": 3, "default": "toolong" }""", "default")]
  [InlineData("""{ "type": "string", "minLength": 5, "maxLength": 2 }""", "lower bound")]
  [InlineData("""{ "type": "number", "minimum": 5, "maximum": 2 }""", "lower bound")]
  [InlineData("""{ "type": "string", "enum": [1, 2] }""", "enum")]
  [InlineData("""{ "type": "string", "enum": ["a", "a"] }""", "twice")]
  [InlineData("""{ "type": "string", "enum": [] }""", "enum")]
  [InlineData("""{ "type": "integer", "enum": ["1"] }""", "enum")]
  [InlineData("""{ "type": "string", "$schema": "x" }""", "$schema")]
  [InlineData("""{ "type": "string", "title": 5 }""", "title")]
  [InlineData("""{ "type": "string", "minLength": -1 }""", "minLength")]
  public void RefusesWhatTheProfileDoesNotSupport(string field, string expected)
  {
    string schema = $$"""{ "type": "object", "additionalProperties": true, "properties": { "field": {{field}} } }""";
    ArgumentException error = Assert.Throws<ArgumentException>(() => Parse(schema));
    Assert.Contains(expected, error.Message);
  }

  [Fact]
  public void RefusesInvalidRootsRepeatsAndOversizedSchemas()
  {
    Assert.Throws<ArgumentException>(() => Parse("""{ "type": "string" }"""));
    Assert.Throws<ArgumentException>(() => Parse("""{ "type": "object", "additionalProperties": true, "x-ui": { "group": "Top" } }"""));
    Assert.Throws<ArgumentException>(() => Parse("""{ "type": "object", "additionalProperties": true, "x-ui": { "order": ["nothing"] } }"""));
    Assert.ThrowsAny<System.Text.Json.JsonException>(() => Parse("""{ "type": "object", "type": "object", "additionalProperties": true }"""));
    Assert.ThrowsAny<System.Text.Json.JsonException>(() => Parse("""{ "type": "object", "additionalProperties": true, "properties": { "a": { "type": "string" }, "a": { "type": "string" } } }"""));
    Assert.ThrowsAny<System.Text.Json.JsonException>(() => Parse("{ \"type\": \"object\", // comment\n \"additionalProperties\": true }"));

    string nested = """{ "type": "string" }""";
    for (int level = 0; level <= SettingsLimits.MaxDepth; level++)
    {
      nested = $$"""{ "type": "object", "additionalProperties": true, "properties": { "inner": {{nested}} } }""";
    }

    Assert.ThrowsAny<Exception>(() => Parse(nested));
    StringBuilder wide = new("""{ "type": "object", "additionalProperties": true, "properties": {""");
    for (int index = 0; index <= SettingsLimits.MaxFields; index++)
    {
      wide.Append(index == 0 ? "" : ",").Append($$""" "f{{index}}": { "type": "boolean" }""");
    }

    wide.Append("} }");
    Assert.Throws<ArgumentException>(() => Parse(wide.ToString()));
  }

  [Fact]
  public void ValidatesValuesAndKeepsAllowedUnknownValues()
  {
    SettingsSchemaNode root = Parse(Schema).Root;
    Assert.Empty(Validate(root, """{ "personality": "warm 🙂", "model": { "name": "m" }, "temperature": 1.5, "rounds": 3.0, "tone": "brief", "thinking": true, "extra": { "kept": [1, 2] } }"""));

    List<SettingsFieldError> errors = Validate(root, """{ "model": { "name": "", "other": 1 }, "temperature": 3, "rounds": 2.5, "tone": "loud", "thinking": "yes" }""");
    Assert.Contains(new SettingsFieldError("/personality", "This value is required."), errors);
    Assert.Contains(errors, error => error.Path == "/model/name" && error.Message.Contains("at least 1"));
    Assert.Contains(errors, error => error.Path == "/model/other" && error.Message.Contains("does not allow"));
    Assert.Contains(errors, error => error.Path == "/temperature" && error.Message.Contains("2 or less"));
    Assert.Contains(errors, error => error.Path == "/rounds" && error.Message.Contains("whole number"));
    Assert.Contains(errors, error => error.Path == "/tone" && error.Message.Contains("listed values"));
    Assert.Contains(errors, error => error.Path == "/thinking" && error.Message.Contains("true or false"));
    Assert.Equal(7, errors.Count);

    // Lengths count code points: four emoji are four characters.
    Assert.Empty(Validate(root, """{ "personality": "🙂🙂🙂🙂", "model": { "name": "m" } }"""));
    Assert.Single(Validate(root, $$"""{ "personality": "{{new string('a', 401)}}", "model": { "name": "m" } }"""));
    Assert.Single(Validate(root, "[]"));
    Assert.Single(Validate(root, """{ "personality": null, "model": { "name": "m" } }"""));
  }

  [Fact]
  public void PointersEscapeAndParseNames()
  {
    Assert.Equal("/a~1b/c~0d", SettingsPointer.Child(SettingsPointer.Child("", "a/b"), "c~d"));
    Assert.Equal(["a/b", "c~d"], SettingsPointer.Parse("/a~1b/c~0d"));
    Assert.Throws<ArgumentException>(() => SettingsPointer.Parse("a"));
    Assert.Throws<ArgumentException>(() => SettingsPointer.Parse("/a~2"));
  }

  private static SettingsSchema Parse(string schema)
  {
    return SettingsSchemaReader.Parse(Encoding.UTF8.GetBytes(schema));
  }

  private static List<SettingsFieldError> Validate(SettingsSchemaNode root, string json)
  {
    return SettingsValidator.Validate(root, JsonNode.Parse(json));
  }
}
