using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using static PiSharp.Tests.Agent.Fakes;

namespace PiSharp.Tests.Agent;

public class ToolArgumentValidatorTests
{
    private static DelegateTool Tool(string schema) => new() { Parameters = Json(schema) };

    private const string Primitives = """
        {"type":"object","properties":{
            "count":{"type":"integer"},
            "ratio":{"type":"number"},
            "flag":{"type":"boolean"},
            "name":{"type":"string"}
        }}
        """;

    [Fact]
    public void CoercesStringifiedPrimitives()
    {
        var result = ToolArgumentValidator.Validate(Tool(Primitives), Json("""{"count":"5","ratio":" 1.5 ","flag":"true","name":42}"""));

        Assert.Equal("5", result["count"]!.ToJsonString());
        Assert.Equal("1.5", result["ratio"]!.ToJsonString());
        Assert.Equal("true", result["flag"]!.ToJsonString());
        Assert.Equal("\"42\"", result["name"]!.ToJsonString());
    }

    [Fact]
    public void CoercesNumericBooleans()
    {
        var result = ToolArgumentValidator.Validate(Tool(Primitives), Json("""{"flag":1}"""));
        Assert.Equal("true", result["flag"]!.ToJsonString());

        result = ToolArgumentValidator.Validate(Tool(Primitives), Json("""{"flag":0}"""));
        Assert.Equal("false", result["flag"]!.ToJsonString());
    }

    [Fact]
    public void LeavesValidValuesUntouchedAndDoesNotMutateInput()
    {
        var input = Json("""{"count":3,"name":"x"}""");
        var result = ToolArgumentValidator.Validate(Tool(Primitives), input);

        Assert.Equal("""{"count":3,"name":"x"}""", result.ToJsonString());
        Assert.NotSame(input, result);
    }

    [Fact]
    public void RemovesExplicitNullForOptionalProperty()
    {
        var result = ToolArgumentValidator.Validate(Tool(Primitives), Json("""{"count":null,"name":"x"}"""));
        Assert.False(result.ContainsKey("count"));
        Assert.Equal("x", result["name"]!.GetValue<string>());
    }

    [Fact]
    public void MissingRequiredPropertyFails()
    {
        var tool = Tool("""{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""");

        var ex = Assert.Throws<ToolArgumentException>(() => ToolArgumentValidator.Validate(tool, Json("{}")));

        Assert.StartsWith("Validation failed for tool \"test_tool\":", ex.Message);
        Assert.Contains("  - path: Expected required property", ex.Message);
        Assert.Contains("Received arguments:", ex.Message);
    }

    [Fact]
    public void NonCoercibleTypeMismatchFails()
    {
        var ex = Assert.Throws<ToolArgumentException>(() => ToolArgumentValidator.Validate(Tool(Primitives), Json("""{"count":"five","flag":"maybe"}""")));

        Assert.Contains("count: Expected integer, received string", ex.Message);
        Assert.Contains("flag: Expected boolean, received string", ex.Message);
    }

    [Fact]
    public void NonIntegerNumberFailsIntegerSchema()
    {
        var ex = Assert.Throws<ToolArgumentException>(() => ToolArgumentValidator.Validate(Tool(Primitives), Json("""{"count":1.5}""")));
        Assert.Contains("count: Expected integer, received number", ex.Message);
    }

    [Fact]
    public void EnumMembershipIsChecked()
    {
        var tool = Tool("""{"type":"object","properties":{"mode":{"type":"string","enum":["read","write"]}}}""");

        Assert.Equal("read", ToolArgumentValidator.Validate(tool, Json("""{"mode":"read"}"""))["mode"]!.GetValue<string>());
        var ex = Assert.Throws<ToolArgumentException>(() => ToolArgumentValidator.Validate(tool, Json("""{"mode":"delete"}""")));
        Assert.Contains("mode: Expected one of \"read\", \"write\", received \"delete\"", ex.Message);
    }

    [Fact]
    public void RecursesIntoNestedObjectsAndArrays()
    {
        var tool = Tool("""
            {"type":"object","properties":{
                "opts":{"type":"object","properties":{"depth":{"type":"integer"}},"required":["depth"]},
                "ids":{"type":"array","items":{"type":"integer"}}
            }}
            """);

        var result = ToolArgumentValidator.Validate(tool, Json("""{"opts":{"depth":"2"},"ids":["1",2,"3"]}"""));
        Assert.Equal("""{"opts":{"depth":2},"ids":[1,2,3]}""", result.ToJsonString());

        var ex = Assert.Throws<ToolArgumentException>(() => ToolArgumentValidator.Validate(tool, Json("""{"opts":{},"ids":[1,"x"]}""")));
        Assert.Contains("opts.depth: Expected required property", ex.Message);
        Assert.Contains("ids.1: Expected integer, received string", ex.Message);
    }

    [Fact]
    public void UnionTypesAcceptAnyListedType()
    {
        var tool = Tool("""{"type":"object","properties":{"limit":{"type":["integer","null"]}}}""");

        Assert.Equal("10", ToolArgumentValidator.Validate(tool, Json("""{"limit":10}"""))["limit"]!.ToJsonString());
        var ex = Assert.Throws<ToolArgumentException>(() => ToolArgumentValidator.Validate(tool, Json("""{"limit":"lots"}""")));
        Assert.Contains("limit: Expected integer | null, received string", ex.Message);
    }

    [Fact]
    public void NonObjectRootFails()
    {
        var ex = Assert.Throws<ToolArgumentException>(() => ToolArgumentValidator.Validate(Tool(Primitives), JsonValue.Create("nope")));
        Assert.Contains("root: Expected object", ex.Message);

        ex = Assert.Throws<ToolArgumentException>(() => ToolArgumentValidator.Validate(Tool(Primitives), null));
        Assert.Contains("root: Expected object", ex.Message);
        Assert.EndsWith("Received arguments:\nnull", ex.Message);
    }

    [Fact]
    public void UnknownPropertiesPassThrough()
    {
        var result = ToolArgumentValidator.Validate(Tool(Primitives), Json("""{"extra":{"a":1}}"""));
        Assert.Equal("""{"extra":{"a":1}}""", result.ToJsonString());
    }

    // Shapes mirror the live ai-factory KB server's kb_write inputSchema (ModelContextProtocol.AspNetCore
    // 2.2.0 emits ["object","null"] for nullable record payload params and ["string","null"] for their
    // nullable fields): root requires only "op"; inside doc, entity+body are required. An explicit null for a
    // required field must NOT be rescued by the null type union and coerced to "".
    private const string KbWriteShaped = """
        {"type":"object","properties":{
            "op":{"type":"string"},
            "doc":{"type":["object","null"],"properties":{
                "entity":{"type":["string","null"]},
                "body":{"type":["string","null"]},
                "key":{"type":["string","null"]}
            },"required":["entity","body"]}
        },"required":["op"]}
        """;

    [Fact]
    public void NullRequiredFieldInNullableUnionFails()
    {
        var ex = Assert.Throws<ToolArgumentException>(() =>
            ToolArgumentValidator.Validate(Tool(KbWriteShaped),
                Json("""{"op":"doc","doc":{"entity":null,"body":"text","key":"k"}}""")));

        Assert.Contains("  - doc.entity: Required property is missing", ex.Message);

        ex = Assert.Throws<ToolArgumentException>(() =>
            ToolArgumentValidator.Validate(Tool(KbWriteShaped),
                Json("""{"op":"doc","doc":{"entity":"wi","body":null,"key":"k"}}""")));

        Assert.Contains("  - doc.body: Required property is missing", ex.Message);
    }

    [Fact]
    public void OmittedRequiredFieldInNullableUnionFails()
    {
        var ex = Assert.Throws<ToolArgumentException>(() =>
            ToolArgumentValidator.Validate(Tool(KbWriteShaped),
                Json("""{"op":"doc","doc":{"body":"text","key":"k"}}""")));

        Assert.Contains("  - doc.entity: Expected required property", ex.Message);
    }

    [Fact]
    public void NullOptionalPayloadIsRemovedWithoutError()
    {
        var result = ToolArgumentValidator.Validate(Tool(KbWriteShaped), Json("""{"op":"doc","doc":null}"""));
        Assert.Equal("""{"op":"doc"}""", result.ToJsonString());
    }

    [Fact]
    public void CompleteNullableUnionPayloadPassesUnchanged()
    {
        var input = Json("""{"op":"doc","doc":{"entity":"wi","body":"text","key":"k"}}""");
        var result = ToolArgumentValidator.Validate(Tool(KbWriteShaped), input);

        Assert.Equal(input.ToJsonString(), result.ToJsonString());
    }

    // Double-serialized object: the KB server declares entity as ["object","null"] but the model
    // sends a JSON string. CoercePrimitive must parse it back into a JsonObject so validation
    // passes and the server receives a real object (pi-sharp-wi-kb-write-doc-double-serialize).
    private const string ObjectUnion = """
        {"type":"object","properties":{
            "entity":{"type":["object","null"]}
        }}
        """;

    [Fact]
    public void CoercesStringifiedObjectToJsonObject()
    {
        var result = ToolArgumentValidator.Validate(Tool(ObjectUnion), Json("""{"entity":"{\"slug\":\"wi-12\"}"}"""));

        Assert.IsType<JsonObject>(result["entity"]);
        Assert.Equal("wi-12", result["entity"]!["slug"]!.GetValue<string>());
    }

    [Fact]
    public void StringifiedObjectInNullableUnionCoerces()
    {
        // Mirrors the live kb_write doc.entity shape: ["object","null"].
        var tool = Tool("""{"type":"object","properties":{"entity":{"type":["object","null"]}},"required":["entity"]}""");
        var result = ToolArgumentValidator.Validate(tool, Json("""{"entity":"{\"op\":\"note\"}"}"""));

        Assert.IsType<JsonObject>(result["entity"]);
        Assert.Equal("note", result["entity"]!["op"]!.GetValue<string>());
    }

    [Fact]
    public void StringifiedArrayCoercesToJsonArray()
    {
        var tool = Tool("""{"type":"object","properties":{"items":{"type":"array"}}}""");
        var result = ToolArgumentValidator.Validate(tool, Json("""{"items":"[1,2,3]"}"""));

        Assert.IsType<JsonArray>(result["items"]);
        Assert.Equal(3, result["items"]!.AsArray().Count);
    }

    [Fact]
    public void RootSentAsJsonStringIsParsed()
    {
        var result = ToolArgumentValidator.Validate(Tool(Primitives), JsonValue.Create("""{"count":"4","name":"x"}"""));
        Assert.Equal("4", result["count"]!.ToJsonString());
        Assert.Equal("x", result["name"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("\"5.0\"", "5")]
    [InlineData("\" 7 \"", "7")]
    public void IntegralNumberStringCoercesToInteger(string sent, string expected)
    {
        var result = ToolArgumentValidator.Validate(Tool(Primitives), Json($$"""{"count":{{sent}}}"""));
        Assert.Equal(expected, result["count"]!.ToJsonString());
    }

    [Fact]
    public void FractionalStringStillFailsInteger()
    {
        var ex = Assert.Throws<ToolArgumentException>(() => ToolArgumentValidator.Validate(Tool(Primitives), Json("""{"count":"5.5"}""")));
        Assert.Contains("count: Expected integer, received string", ex.Message);
    }

    [Theory]
    [InlineData("\"True\"", "true")]
    [InlineData("\"FALSE\"", "false")]
    [InlineData("\" true \"", "true")]
    [InlineData("\"1\"", "true")]
    [InlineData("\"0\"", "false")]
    public void BooleanStringsCoerce(string sent, string expected)
    {
        var result = ToolArgumentValidator.Validate(Tool(Primitives), Json($$"""{"flag":{{sent}}}"""));
        Assert.Equal(expected, result["flag"]!.ToJsonString());
    }

    [Fact]
    public void SingleObjectWhereObjectListExpectedIsWrapped()
    {
        var tool = Tool("""{"type":"object","properties":{"edits":{"type":"array","items":{"type":"object","properties":{"n":{"type":"integer"}}}}}}""");
        var result = ToolArgumentValidator.Validate(tool, Json("""{"edits":{"n":"2"}}"""));
        Assert.Equal("""{"edits":[{"n":2}]}""", result.ToJsonString());
    }

    [Fact]
    public void ObjectWhereStringListExpectedIsNotWrapped()
    {
        var tool = Tool("""{"type":"object","properties":{"tags":{"type":"array","items":{"type":"string"}}}}""");
        var ex = Assert.Throws<ToolArgumentException>(() => ToolArgumentValidator.Validate(tool, Json("""{"tags":{"a":1}}""")));
        Assert.Contains("tags: Expected array, received object", ex.Message);
    }

    [Fact]
    public void NonJsonStringForObjectSchemaFails()
    {
        var ex = Assert.Throws<ToolArgumentException>(() =>
            ToolArgumentValidator.Validate(Tool(ObjectUnion), Json("""{"entity":"not json"}""")));
        Assert.Contains("entity: Expected object | null, received string", ex.Message);
    }
}
